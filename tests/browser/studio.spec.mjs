import { test, expect } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);
async function boot(page) {
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics?.frames >= 1, { timeout: 90000 });
  await mkdir('artifacts/screenshots', { recursive: true });
}
async function clickCommand(page, key) {
  const rect = (await state(page)).commands[key];
  expect(rect.width, key).toBeGreaterThan(0);
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
}
async function diagram(page) { await clickCommand(page, 'block-diagram'); await expect.poll(async () => (await state(page)).view).toBe('BlockDiagram'); await page.waitForTimeout(400); }

test('real Uno front panel runs simulated acquisition and both views render', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await boot(page);
  let s = await state(page); expect(s.nodes).toHaveLength(13); expect(s.wires).toHaveLength(12); expect(s.errors).toBe(0);
  expect(s.nodes.find(n => n.kind === 'rms').number).toBeGreaterThan(1.5);
  expect(s.nodes.find(n => n.kind === 'simulate').samples).toBe(512);
  await page.screenshot({ path: 'artifacts/screenshots/front-panel.png' });
  await diagram(page);
  await page.screenshot({ path: 'artifacts/screenshots/block-diagram.png' });
  expect(errors).toEqual([]);
});

test('real pointer node drag creates one undo transaction', async ({ page }) => {
  await boot(page); await diagram(page);
  const before = await state(page); const node = before.nodes.find(n => n.kind === 'constant'); const r = node.bounds;
  await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2); await page.mouse.down(); await page.mouse.move(r.x + r.width / 2 + 45, r.y + r.height / 2 + 25, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).nodes.find(n => n.id === node.id).modelX).not.toBe(node.modelX);
  await clickCommand(page, 'undo');
  await expect.poll(async () => (await state(page)).nodes.find(n => n.id === node.id).modelX).toBe(node.modelX);
});

test('terminal wiring replaces a typed input source and remains executable', async ({ page }) => {
  await boot(page); await diagram(page);
  const s = await state(page); const from = s.nodes.find(n => n.kind === 'simulate'); const target = s.nodes.find(n => n.label === 'Acquired signal');
  await page.mouse.move(from.output.x, from.output.y); await page.mouse.down(); await page.mouse.move(target.inputs.x.x, target.inputs.x.y, { steps: 12 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).wires.find(w => w.to === target.id).from).toBe(from.id);
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).errors).toBe(0);
  expect((await state(page)).nodes.find(n => n.id === target.id).samples).toBe(512);
  await clickCommand(page, 'undo');
  await expect.poll(async () => (await state(page)).wires.find(w => w.to === target.id).from).toBe(s.nodes.find(n => n.kind === 'filter').id);
});

test('palette insertion, undo and continuous execution use real controls', async ({ page }) => {
  await boot(page);
  const palette = (await state(page)).palette['control:Numeric']; await page.mouse.click(palette.x + palette.width / 2, palette.y + palette.height / 2);
  await expect.poll(async () => (await state(page)).nodes.length).toBe(14);
  await clickCommand(page, 'undo'); await expect.poll(async () => (await state(page)).nodes.length).toBe(13);
  await clickCommand(page, 'continuous'); await expect.poll(async () => (await state(page)).frames).toBeGreaterThan(3);
  await clickCommand(page, 'abort'); await expect.poll(async () => (await state(page)).running).toBe(false);
  const frames = (await state(page)).frames; await page.waitForTimeout(300); expect((await state(page)).frames).toBe(frames);
});

test('project download is versioned JSON and local recovery survives reload', async ({ page }) => {
  await boot(page); const entry = (await state(page)).palette['bool-control:Switch']; await page.mouse.click(entry.x + entry.width / 2, entry.y + entry.height / 2);
  await expect.poll(async () => (await state(page)).nodes.length).toBe(14);
  const downloadPromise = page.waitForEvent('download'); await clickCommand(page, 'save'); const download = await downloadPromise;
  const file = await download.path(); const project = JSON.parse(await readFile(file, 'utf8')); expect(project.formatVersion).toBe(1); expect(project.instruments[0].diagram.nodes).toHaveLength(14);
  await page.waitForTimeout(1200); await page.reload(); await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready);
  await expect.poll(async () => (await state(page)).nodes.length).toBe(14);
});
