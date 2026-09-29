import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
const url = process.env.LABSPACE_URL || 'http://127.0.0.1:4173/LabSpace/';
const state = page => page.evaluate(() => globalThis.labSpaceDiagnostics);
const errors = new WeakMap();
test.beforeEach(async ({ page }) => { const list=[]; errors.set(page,list); page.on('pageerror',error=>list.push(error.message)); });
test.afterEach(async ({ page }) => expect(errors.get(page)).toEqual([]));
async function click(page,r) { expect(r?.width).toBeGreaterThan(0); await page.mouse.click(r.x+r.width/2,r.y+r.height/2); }
async function command(page,name) { await click(page,(await state(page)).commands[name]); }
async function field(page,name) { await expect.poll(async()=> (await state(page)).overlayFields[name]?.width || 0).toBeGreaterThan(0); return (await state(page)).overlayFields[name]; }
async function edit(page,name,text) { await click(page,await field(page,name)); await page.keyboard.press('Control+a'); await page.keyboard.insertText(text); await page.keyboard.press('Tab'); }
async function sample(page,index,title,diagram=true) {
  await page.goto(url+(url.includes('?')?'&':'?')+'test=1');
  await page.waitForFunction(()=>globalThis.labSpaceDiagnostics?.ready && globalThis.labSpaceDiagnostics?.frames>=1,null,{timeout:90000});
  await page.waitForTimeout(750); await click(page,Object.values((await state(page)).instruments)[index]);
  await expect.poll(async()=> (await state(page)).instrument).toBe(title);
  if(diagram) { await command(page,'block-diagram'); await expect.poll(async()=> (await state(page)).view).toBe('BlockDiagram'); }
  await mkdir('artifacts/screenshots',{recursive:true}); await page.waitForTimeout(300);
}
async function node(page,kind) { return (await state(page)).nodes.find(n=>n.kind===kind); }
async function shot(page,name) { await page.waitForTimeout(300); await page.screenshot({path:'artifacts/screenshots/'+name+'.png'}); }

test('typed Case labels edit atomically and visible frame is independent of dispatch',async({page})=>{
  await sample(page,4,'Case Dispatch.vi'); await command(page,'run');
  await expect.poll(async()=> (await node(page,'case-multi')).number).toBe(42);
  let c=await node(page,'case-multi'); await click(page,c.bounds); await command(page,'frames-edit');
  await expect.poll(async()=> (await state(page)).overlay).toBe('frames'); await shot(page,'case-editor');
  const revision=(await state(page)).revision;
  await edit(page,'frame-label-0','"LAUNCH"'); await click(page,await field(page,'frames-cancel'));
  await expect.poll(async()=> (await state(page)).overlay).toBe(''); expect((await state(page)).revision).toBe(revision);
  await command(page,'frames-edit'); await edit(page,'frame-label-0','"LAUNCH"'); await click(page,await field(page,'frames-apply'));
  await expect.poll(async()=> (await node(page,'case-multi')).frameLabels[0]).toBe('"LAUNCH"');
  await command(page,'run'); await expect.poll(async()=> (await node(page,'case-multi')).number).toBe(-1);
  await command(page,'undo'); await command(page,'run'); await expect.poll(async()=> (await node(page,'case-multi')).number).toBe(42);
  c=await node(page,'case-multi'); const r=c.bounds; await page.mouse.click(r.x+r.width/2+64,r.y+9);
  await expect.poll(async()=> (await node(page,'case-multi')).visibleFrame).toBe(1);
  await command(page,'run'); await expect.poll(async()=> (await node(page,'case-multi')).number).toBe(42); await shot(page,'multi-case');
});

test('sequence locals survive frame navigation and Formula Node edits run in the root context',async({page})=>{
  await sample(page,5,'Sequence Pipeline.vi'); await command(page,'run');
  await expect.poll(async()=> (await node(page,'sequence')).number).toBe(84); await shot(page,'sequence');
  const s=await node(page,'sequence'),b=s.bounds; await page.mouse.dblclick(b.x+b.width/2,b.y+b.height/2);
  await expect.poll(async()=> (await state(page)).depth).toBe(1); await command(page,'next-frame');
  await expect.poll(async()=> !!(await node(page,'formula'))).toBe(true);
  await click(page,(await node(page,'formula')).bounds); await command(page,'formula-edit');
  await expect.poll(async()=> (await state(page)).overlay).toBe('formula'); await shot(page,'formula-editor');
  const rev=(await state(page)).revision; await edit(page,'formula-source','result = unknown;'); await click(page,await field(page,'formula-apply'));
  await page.waitForTimeout(300); expect((await state(page)).overlay).toBe('formula'); expect((await state(page)).revision).toBe(rev);
  await edit(page,'formula-source','result = x * 5;'); await click(page,await field(page,'formula-apply')); await expect.poll(async()=> (await state(page)).overlay).toBe('');
  await expect.poll(async()=> (await node(page,'formula')).text).toBe('result = x * 5;');
  await command(page,'run'); await expect.poll(async()=> (await state(page)).status).toBe('Execution complete');
  await shot(page,'sequence-formula'); await command(page,'up'); await command(page,'run'); await expect.poll(async()=> (await node(page,'sequence')).number).toBe(105);
  await command(page,'undo'); await command(page,'run'); await expect.poll(async()=> (await node(page,'sequence')).number).toBe(84);
});

test('sequence frame reorder produces dependency diagnostics and undo recovers',async({page})=>{
  await sample(page,5,'Sequence Pipeline.vi'); await click(page,(await node(page,'sequence')).bounds); await command(page,'frames-edit');
  await click(page,await field(page,'frame-down-0')); await click(page,await field(page,'frames-apply'));
  await expect.poll(async()=> (await state(page)).overlay).toBe(''); await expect.poll(async()=> (await state(page)).errors).toBeGreaterThan(0);
  await command(page,'undo'); await expect.poll(async()=> (await state(page)).errors).toBe(0);
  await command(page,'run'); await expect.poll(async()=> (await node(page,'sequence')).number).toBe(84);
});

test('error cluster and complex panel controls change actual typed results',async({page})=>{
  await sample(page,6,'Errors and Complex.vi',false); await command(page,'run');
  await expect.poll(async()=> (await node(page,'complex-magnitude')).number).toBe(5); await shot(page,'error-complex-panel');
  let s=await state(page); const errorNode=s.nodes.find(n=>n.kind==='error-control');
  await click(page,s.panel.find(p=>p.nodeId===errorNode.id).bounds); await expect.poll(async()=> (await state(page)).overlay).toBe('error');
  await edit(page,'error-code','42'); await edit(page,'error-source','Instrument channel unavailable'); await click(page,await field(page,'error-apply'));
  await expect.poll(async()=> (await state(page)).overlay).toBe(''); await command(page,'run');
  await expect.poll(async()=> (await node(page,'error-indicator')).errorCode).toBe(42);
  expect((await node(page,'error-indicator')).errorSource).toBe('Instrument channel unavailable');
  s=await state(page); const complex=s.nodes.find(n=>n.kind==='complex-control'); await click(page,s.panel.find(p=>p.nodeId===complex.id).bounds);
  await expect.poll(async()=> (await state(page)).overlay).toBe('complex'); await edit(page,'complex-real','5'); await edit(page,'complex-imaginary','12');
  await click(page,await field(page,'complex-apply')); await expect.poll(async()=> (await state(page)).overlay).toBe(''); await command(page,'run');
  await expect.poll(async()=> (await node(page,'complex-magnitude')).number).toBe(13);
  await command(page,'undo'); await command(page,'run'); await expect.poll(async()=> (await node(page,'complex-magnitude')).number).toBe(5);
});

test('terminal context creates a typed constant and undo restores the old connection',async({page})=>{
  await sample(page,6,'Errors and Complex.vi'); const target=await node(page,'complex-magnitude'),point=target.inputs.x;
  const before=await state(page),wire=before.wires.find(w=>w.to===target.id);
  await page.mouse.click(point.x,point.y,{button:'right'}); await expect.poll(async()=> (await state(page)).overlay).toBe('diagram-context');
  await shot(page,'terminal-context'); await click(page,await field(page,'terminal-constant'));
  await expect.poll(async()=> (await state(page)).nodes.length).toBe(before.nodes.length+1);
  const added=(await state(page)).nodes.find(n=>!before.nodes.some(old=>old.id===n.id)); expect(added.kind).toBe('complex');
  await command(page,'run'); await expect.poll(async()=> (await node(page,'complex-magnitude')).number).toBe(0);
  await command(page,'undo'); await expect.poll(async()=> (await state(page)).wires.find(w=>w.to===target.id)?.from).toBe(wire.from);
});

test('structure resize and nested step-out are usable from the studio',async({page})=>{
  await sample(page,5,'Sequence Pipeline.vi'); const before=await node(page,'sequence'); await click(page,before.bounds);
  let r=(await node(page,'sequence')).bounds; await page.mouse.move(r.x+r.width-3,r.y+r.height-3); await page.mouse.down();
  await page.mouse.move(r.x+r.width+50,r.y+r.height+30,{steps:10}); await page.mouse.up();
  await expect.poll(async()=> (await node(page,'sequence')).bounds.width).toBeGreaterThan(before.bounds.width);
  await command(page,'undo'); await expect.poll(async()=> Math.abs((await node(page,'sequence')).bounds.width-before.bounds.width)).toBeLessThan(1);
  for(let i=0;i<10 && (await state(page)).debugDepth===0;i++){ await command(page,'step-into'); await page.waitForTimeout(230); }
  expect((await state(page)).paused).toBe(true); expect((await state(page)).debugDepth).toBeGreaterThan(0);
  await command(page,'step-out'); await expect.poll(async()=> (await state(page)).status).toContain('Step out');
  await command(page,'run'); await expect.poll(async()=> (await node(page,'sequence')).number).toBe(84);
});


test('Quick Drop Case Structure selects the multi-frame implementation', async ({page}) => {
  await sample(page,0,'Signal Analysis.vi'); await page.keyboard.press('Control+Space');
  await expect.poll(async()=>(await state(page)).overlay).toBe('functions');
  await click(page,await field(page,'quick-drop-query')); await page.keyboard.insertText('Case Structure'); await page.keyboard.press('Enter');
  await expect.poll(async()=>(await state(page)).placement).toBe('case-multi');
  const r=(await state(page)).diagramBounds; await page.mouse.click(r.x+r.width*.45,r.y+r.height*.5);
  await expect.poll(async()=>(await state(page)).nodes.some(n=>n.kind==='case-multi' && n.frameCount===2)).toBe(true);
  const placed=await node(page,'case-multi'), b=placed.bounds;
  await page.mouse.move(b.x+12,b.y+5); await page.mouse.down();
  await page.mouse.move(b.x+52,b.y+35,{steps:8}); await page.mouse.up();
  await expect.poll(async()=>(await node(page,'case-multi')).modelX).not.toBe(placed.modelX);
  expect((await state(page)).overlay).toBe('');
  await command(page,'undo'); await expect.poll(async()=>(await node(page,'case-multi')).modelX).toBe(placed.modelX);
  await command(page,'undo'); await expect.poll(async()=>(await state(page)).nodes.some(n=>n.kind==='case-multi')).toBe(false);
});
