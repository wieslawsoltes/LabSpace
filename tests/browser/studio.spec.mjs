import { test, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { PNG } from 'pngjs';
const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);
const pageErrors = new WeakMap();
test.beforeEach(async ({ page }) => { const errors = []; pageErrors.set(page, errors); page.on('pageerror', error => errors.push(error.message)); });
test.afterEach(async ({ page }) => { expect(pageErrors.get(page)).toEqual([]); });
async function boot(page) {
  await page.goto(url + (url.includes('?') ? '&' : '?') + 'test=1');
  await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics?.frames >= 1, null, { timeout: 90000 });
  // The .NET model can be ready slightly before Uno removes its loading layer.
  await page.waitForTimeout(750);
  await mkdir('artifacts/screenshots', { recursive: true });
}
async function clickCommand(page, key) {
  const rect = (await state(page)).commands[key];
  expect(rect.width, key).toBeGreaterThan(0);
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
}
async function diagram(page) { await clickCommand(page, 'block-diagram'); await expect.poll(async () => (await state(page)).view).toBe('BlockDiagram'); await page.waitForTimeout(400); }
function pixels(buffer) {
  const png = PNG.sync.read(buffer); let dark = 0, chromatic = 0;
  for (let i = 0; i < png.data.length; i += 4) {
    const r = png.data[i], g = png.data[i + 1], b = png.data[i + 2];
    if (r + g + b < 210) dark++;
    if (Math.max(r, g, b) - Math.min(r, g, b) > 35) chromatic++;
  }
  return { dark, chromatic };
}
async function screenshot(page, name, minimumDark = 1000) {
  let buffer;
  await expect.poll(async () => { buffer = await page.screenshot(); return pixels(buffer).dark; }, { timeout: 15000 }).toBeGreaterThan(minimumDark);
  expect(pixels(buffer).chromatic).toBeGreaterThan(1500);
  await writeFile('artifacts/screenshots/' + name + '.png', buffer);
}

test('real Uno front panel runs simulated acquisition and both views render', async ({ page }) => {
  await boot(page);
  const s = await state(page); expect(s.nodes).toHaveLength(13); expect(s.wires).toHaveLength(12); expect(s.errors).toBe(0);
  expect(s.nodes.find(n => n.kind === 'rms').number).toBeGreaterThan(1.5);
  expect(s.nodes.find(n => n.kind === 'simulate').samples).toBe(512);
  await screenshot(page, 'front-panel', 20000);
  await diagram(page); await screenshot(page, 'block-diagram');
  await clickCommand(page, 'split'); await page.waitForTimeout(400); await screenshot(page, 'split-view');
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
  const file = await download.path(); const project = JSON.parse(await readFile(file, 'utf8')); expect(project.formatVersion).toBe(3); expect(project.instruments[0].diagram.nodes).toHaveLength(14);
  await page.waitForTimeout(1200); await page.reload(); await page.waitForFunction(() => globalThis.labSpaceDiagnostics?.ready);
  await expect.poll(async () => (await state(page)).nodes.length).toBe(14);
});

test('front-panel knob drives real computed measurements and undo restores it', async ({ page }) => {
  await boot(page); const s = await state(page); const knob = s.panel.find(p => p.widget === 'Knob'); const node = s.nodes.find(n => n.id === knob.nodeId); const r = knob.bounds;
  await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2); await page.mouse.down(); await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2 - 30, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).nodes.find(n => n.id === node.id).value).toBeGreaterThan(node.value);
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'rms').number).toBeGreaterThan(2);
  await clickCommand(page, 'undo'); await expect.poll(async () => (await state(page)).nodes.find(n => n.id === node.id).value).toBe(node.value);
});

test('nested For Loop opens a real editable body and executes after return', async ({ page }) => {
  await boot(page); const entries = Object.values((await state(page)).instruments); const r = entries[2];
  await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2); await expect.poll(async () => (await state(page)).instrument).toBe('Stateful Loop.vi');
  await diagram(page); let loop = (await state(page)).nodes.find(n => n.kind === 'for'); let b = loop.bounds;
  await page.mouse.dblclick(b.x + b.width / 2, b.y + b.height / 2); await expect.poll(async () => (await state(page)).depth).toBe(1);
  expect((await state(page)).nodes.some(n => n.kind === 'output')).toBe(true);
  await screenshot(page, 'nested-loop');
  await clickCommand(page, 'up'); await expect.poll(async () => (await state(page)).depth).toBe(0); await clickCommand(page, 'run');
  await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'indicator').number).toBe(10);
});

async function clickRect(page, r) { expect(r?.width).toBeGreaterThan(0); await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2); }
async function indexedExample(page) {
  await boot(page); await clickRect(page, Object.values((await state(page)).instruments)[3]);
  await expect.poll(async () => (await state(page)).instrument).toBe('Indexed Accumulator.vi'); await diagram(page);
}
async function overlayField(page, name) { await expect.poll(async () => (await state(page)).overlayFields[name]?.width || 0).toBeGreaterThan(0); return (await state(page)).overlayFields[name]; }

test('Quick Drop searches with the keyboard and waits for pointer placement', async ({ page }) => {
  await boot(page); await diagram(page); await page.keyboard.press('Control+Space');
  await expect.poll(async () => (await state(page)).overlay).toBe('functions');
  await clickRect(page, await overlayField(page, 'quick-drop-query')); await page.keyboard.type('Increment'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).placement).toBe('increment'); expect((await state(page)).nodes).toHaveLength(13);
  const r = (await state(page)).diagramBounds; await page.mouse.click(r.x + r.width * .55, r.y + r.height * .7);
  await expect.poll(async () => (await state(page)).nodes.length).toBe(14); expect((await state(page)).nodes.some(n => n.kind === 'increment')).toBe(true);
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).status).toContain('VI is broken');
  await clickCommand(page, 'undo'); await expect.poll(async () => (await state(page)).nodes.length).toBe(13);
});

test('named output terminals are wired through actual pointer events', async ({ page }) => {
  await indexedExample(page); await clickCommand(page, 'run');
  await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'indicator')?.number).toBe(15);
  const s = await state(page); const loop = s.nodes.find(n => n.kind === 'for'), target = s.nodes.find(n => n.kind === 'array-indicator');
  expect(s.nodes.find(n => n.id === target.id).sampleValues).toEqual([1, 3, 6, 10, 15]);
  const oldWire = s.wires.find(w => w.to === target.id).id;
  const from = loop.outputs.totals, to = target.inputs.x;
  await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(to.x, to.y, { steps: 12 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).wires.find(w => w.to === target.id)?.id).not.toBe(oldWire);
  expect((await state(page)).wires.find(w => w.to === target.id).output).toBe('totals');
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).nodes.find(n => n.id === target.id)?.sampleValues).toEqual([1, 3, 6, 10, 15]);
  await screenshot(page, 'typed-structure');
});

test('contract editor applies persistent shift registers and undo restores initialization', async ({ page }) => {
  await indexedExample(page); const loop = (await state(page)).nodes.find(n => n.kind === 'for'); await clickRect(page, loop.bounds); await clickCommand(page, 'structure');
  await expect.poll(async () => (await state(page)).overlay).toBe('structure'); await screenshot(page, 'connector-editor');
  await clickRect(page, await overlayField(page, 'register-initialized-0')); await clickRect(page, await overlayField(page, 'contract-apply'));
  await expect.poll(async () => (await state(page)).overlay).toBe('');
  expect((await state(page)).wires.some(w => w.input === 'initial:state')).toBe(false);
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'indicator')?.number).toBe(15);
  await clickCommand(page, 'run'); await expect.poll(async () => (await state(page)).nodes.find(n => n.kind === 'indicator')?.number).toBe(30);
  await clickCommand(page, 'undo'); await expect.poll(async () => (await state(page)).wires.some(w => w.input === 'initial:state')).toBe(true);
});

test('canceling a connector draft does not change the document', async ({ page }) => {
  await indexedExample(page); const before = await state(page), loop = before.nodes.find(n => n.kind === 'for');
  await clickRect(page, loop.bounds); await clickCommand(page, 'structure'); await clickRect(page, await overlayField(page, 'register-initialized-0'));
  await clickRect(page, await overlayField(page, 'contract-cancel')); await expect.poll(async () => (await state(page)).overlay).toBe('');
  expect((await state(page)).revision).toBe(before.revision); expect((await state(page)).nodes.find(n => n.id === loop.id).registerInitialized).toBe(true);
});

test('step into exposes the active nested execution frame without navigating away', async ({ page }) => {
  await indexedExample(page);
  for (let i = 0; i < 7 && (await state(page)).debugDepth === 0; i++) { await clickCommand(page, 'step-into'); await page.waitForTimeout(230); }
  const s = await state(page); expect(s.paused).toBe(true); expect(s.debugDepth).toBeGreaterThan(0); expect(s.depth).toBe(0);
  await clickCommand(page, 'abort'); await expect.poll(async () => (await state(page)).paused).toBe(false);
});

test('right-click palette can be dismissed and reopened without document changes', async ({ page }) => {
  await boot(page); await diagram(page); const before = await state(page), r = before.diagramBounds;
  for (let i = 0; i < 2; i++) {
    await page.mouse.click(r.x + r.width * .7, r.y + r.height * .7, { button: 'right' });
    await expect.poll(async () => (await state(page)).overlay).toBe('functions');
    await clickRect(page, await overlayField(page, 'quick-drop-query')); await page.keyboard.type('Add');
    if (i === 1) await screenshot(page, 'quick-drop');
    await page.keyboard.press('Escape'); await expect.poll(async () => (await state(page)).overlay).toBe('');
  }
  expect((await state(page)).revision).toBe(before.revision);
});

test('front-panel Quick Drop inserts a knob at the chosen pointer location as one edit', async ({ page }) => {
  await boot(page); const before = await state(page);
  await page.keyboard.press('Control+Space'); await expect.poll(async () => (await state(page)).overlay).toBe('controls');
  await clickRect(page, await overlayField(page, 'quick-drop-query')); await page.keyboard.type('Knob'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).placement).toBe('control');
  const r = (await state(page)).panelBounds; const x = r.x + r.width * .12, y = r.y + r.height * .68;
  await page.mouse.move(x, y); await page.mouse.click(x, y);
  await expect.poll(async () => (await state(page)).panel.length).toBe(before.panel.length + 1);
  const added = (await state(page)).panel.find(p => !before.panel.some(old => old.id === p.id));
  expect(added.widget).toBe('Knob'); expect(Math.abs(added.bounds.x - x)).toBeLessThan(15); expect(Math.abs(added.bounds.y - y)).toBeLessThan(15);
  await clickCommand(page, 'undo'); await expect.poll(async () => (await state(page)).panel.length).toBe(before.panel.length);
});
