import { test, expect } from '@playwright/test';

const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);

test('rapid query and Enter always choose the current result, not the old visual list', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics.frames >= 1, null, { timeout: 90000 });
  await page.waitForTimeout(750);
  const diagram = (await state(page)).commands['block-diagram'];
  await page.mouse.click(diagram.x + diagram.width / 2, diagram.y + diagram.height / 2);
  await expect.poll(async () => (await state(page)).view).toBe('BlockDiagram');
  for (const [query, kind] of [['Increment', 'increment'], ['Reverse 1D array', 'reverse-array'], ['Natural logarithm', 'log']]) {
    await page.keyboard.press('Control+Space');
    await expect.poll(async () => (await state(page)).overlay).toBe('functions');
    const r = (await state(page)).overlayFields['quick-drop-query'];
    expect(r.width).toBeGreaterThan(0);
    await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
    // Intentionally no delay, poll, DOM fill, or model mutation between input and Enter.
    await page.keyboard.type(query);
    await page.keyboard.press('Enter');
    await expect.poll(async () => (await state(page)).placement).toBe(kind);
    await page.keyboard.press('Escape');
    await expect.poll(async () => (await state(page)).placement).toBe(null);
  }
  expect((await state(page)).nodes).toHaveLength(13);
  expect(errors).toEqual([]);
});
