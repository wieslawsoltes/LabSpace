import { test, expect } from '@playwright/test';

const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);

test('Quick Drop remains available while a toolbar button owns keyboard focus', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics.frames >= 1, null, { timeout: 90000 });
  await page.waitForTimeout(750);
  async function command(name) {
    const r = (await state(page)).commands[name];
    expect(r.width).toBeGreaterThan(0);
    await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
  }
  await command('block-diagram');
  await expect.poll(async () => (await state(page)).view).toBe('BlockDiagram');
  await command('run');
  await page.keyboard.press('Control+Space');
  await expect.poll(async () => (await state(page)).overlay).toBe('functions');
  await expect.poll(async () => (await state(page)).overlayFields['quick-drop-query']?.width || 0).toBeGreaterThan(0);
  const r = (await state(page)).overlayFields['quick-drop-query'];
  await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
  await page.keyboard.press('Escape');
  await expect.poll(async () => (await state(page)).overlay).toBe('');
  expect((await state(page)).nodes).toHaveLength(13);
  expect(errors).toEqual([]);
});
