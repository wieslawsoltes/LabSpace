import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);
const errors = new WeakMap();
test.beforeEach(async ({ page }) => { const list = []; errors.set(page, list); page.on('pageerror', e => list.push(e.message)); });
test.afterEach(async ({ page }) => expect(errors.get(page)).toEqual([]));
async function click(page, r) { expect(r?.width).toBeGreaterThan(0); await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2); }
async function command(page, name) { await click(page, (await state(page)).commands[name]); }
async function field(page, name) { await expect.poll(async () => (await state(page)).debugFields[name]?.width || 0).toBeGreaterThan(0); return (await state(page)).debugFields[name]; }
async function boot(page, index = 7) {
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics.frames >= 1, null, { timeout: 90000 });
  await page.waitForTimeout(500); const s = await state(page); await click(page, Object.values(s.instruments)[index]);
  await expect.poll(async () => (await state(page)).activeId).not.toBe(s.activeId);
  await command(page, 'block-diagram'); await command(page, 'run');
  await expect.poll(async () => (await state(page)).status).toBe('Execution complete');
}
test('Formula control flow executes distinct outputs and the Debug window shows real values', async ({ page }) => {
  await boot(page); await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'formula')?.namedValues.result).toBe('84');
  expect((await state(page)).nodes.find(n => n.kind === 'formula').namedValues.iterations).toBe('9');
  await command(page, 'debug-window'); await expect.poll(async () => (await state(page)).debugWindow).toBe(true);
  await expect.poll(async () => (await state(page)).probes.find(p => p.instrument === 'Formula Control Flow.vi')?.value).toBe('84');
  await mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/debug-window.png' });
});
test('Probe values survive switching VIs and Locate reveals their source', async ({ page }) => {
  await boot(page); const original = (await state(page)).activeId;
  await click(page, Object.values((await state(page)).instruments)[1]); await expect.poll(async () => (await state(page)).instrument).toBe('Arithmetic.vi');
  await command(page, 'debug-window'); await expect.poll(async () => (await state(page)).debugWindow).toBe(true);
  const probe = (await state(page)).probes.find(p => p.instrument === 'Formula Control Flow.vi'); expect(probe.value).toBe('84'); expect(probe.retained).toBe(true);
  await click(page, await field(page, 'debug-locate-0')); await expect.poll(async () => (await state(page)).activeId).toBe(original);
});
test('Debugger options do not dirty the document or create undo records', async ({ page }) => {
  await boot(page); const before = await state(page); await command(page, 'debug-window');
  await click(page, await field(page, 'debug-allow')); await expect.poll(async () => (await state(page)).debuggingEnabled).toBe(false);
  await click(page, await field(page, 'debug-retain')); await expect.poll(async () => (await state(page)).debugBytes).toBe(0);
  const after = await state(page); expect(after.revision).toBe(before.revision); expect(after.dirty).toBe(before.dirty); expect(after.canUndo).toBe(before.canUndo);
  await click(page, await field(page, 'debug-allow')); await expect.poll(async () => (await state(page)).debuggingEnabled).toBe(true);
});
test('Hidden Debug window performs no refresh work while execution continues', async ({ page }) => {
  await boot(page); await command(page, 'debug-window'); await expect.poll(async () => (await state(page)).debugWindow).toBe(true);
  await command(page, 'debug-close'); await expect.poll(async () => (await state(page)).debugWindow).toBe(false); const count = (await state(page)).debugRefreshes;
  await command(page, 'continuous'); await page.waitForTimeout(650); await command(page, 'abort'); expect((await state(page)).debugRefreshes).toBe(count);
  await command(page, 'debug-window'); await expect.poll(async () => (await state(page)).debugRefreshes).toBeGreaterThan(count);
});
test('Breakpoint context command pauses without changing the project or forcing Debug open', async ({ page }) => {
  await boot(page, 1); const before = await state(page), node = before.nodes.find(n => n.kind === 'add');
  await page.mouse.click(node.bounds.x + node.bounds.width / 2, node.bounds.y + node.bounds.height / 2, { button: 'right' });
  await expect.poll(async () => (await state(page)).overlay).toBe('diagram-context');
  await click(page, (await state(page)).overlayFields['node-breakpoint']);
  await expect.poll(async () => (await state(page)).nodes.find(n => n.id === node.id).breakpoint).toBe(true);
  await command(page, 'run'); await expect.poll(async () => (await state(page)).paused).toBe(true);
  const paused = await state(page); expect(paused.debugWindow).toBe(false); expect(paused.revision).toBe(before.revision); expect(paused.dirty).toBe(before.dirty);
  await command(page, 'run'); await expect.poll(async () => (await state(page)).status).toBe('Execution complete');
});
