import { test } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync(new URL('../../src/LabSpace.App/Platforms/WebAssembly/WasmScripts/Storage.js', import.meta.url), 'utf8');
function harness() {
  const requests = [], transactions = [], elements = [], listeners = new Map(), timers = new Map(), revoked = [];
  let timer = 0, openError, transactionError;
  const context = {
    TextEncoder, URLSearchParams, Blob, location: { search: '?test=1' },
    URL: { createObjectURL: () => 'blob:test', revokeObjectURL: u => revoked.push(u) },
    setTimeout: fn => { timers.set(++timer, fn); return timer; }, clearTimeout: id => timers.delete(id),
    addEventListener: (event, fn) => listeners.set(event, fn), removeEventListener: (event, fn) => { if (listeners.get(event) === fn) listeners.delete(event); },
    indexedDB: { open() { if (openError) { const e = openError; openError = undefined; throw e; } const r = {}; requests.push(r); return r; } },
    document: {
      body: { append: e => { e.appended = true; } },
      createElement(tag) { const handlers = new Map(); const e = { tag, style: {}, files: [], addEventListener: (name, fn) => handlers.set(name, fn), remove() { this.removed = true; }, click() {}, emit: (name) => handlers.get(name)?.() }; elements.push(e); return e; }
    }
  };
  vm.runInNewContext(source, context);
  function succeed(r = requests.at(-1)) {
    const db = { closed: 0, objectStoreNames: { contains: () => true }, close() { this.closed++; }, transaction() {
      if (transactionError) { const e = transactionError; transactionError = undefined; throw e; }
      const req = {}; const tx = { request: req, objectStore: () => ({ get: () => req, put: () => req }) }; transactions.push(tx); return tx;
    } };
    r.result = db; r.onsuccess(); return db;
  }
  function complete(value, tx = transactions.at(-1)) { tx.request.result = value; tx.request.onsuccess(); tx.oncomplete(); }
  const tick = () => new Promise(resolve => setImmediate(resolve));
  return { api: context.labSpaceStorage, requests, transactions, elements, listeners, timers, revoked, succeed, complete, tick,
    openError: e => { openError = e; }, transactionError: e => { transactionError = e; }, context };
}
test('simultaneous reads share one connection and wait for transaction commit', async () => {
  const h = harness(); const a = h.api.load(), b = h.api.load(); assert.equal(h.requests.length, 1); h.succeed(); await h.tick();
  let resolved = false; a.then(() => { resolved = true; }); const tx = h.transactions[0]; tx.request.result = 'saved'; tx.request.onsuccess(); await h.tick(); assert.equal(resolved, false);
  tx.oncomplete(); h.complete(undefined, h.transactions[1]); assert.equal(await a, 'saved'); assert.equal(await b, '');
});
test('blocked open retries and closes a late orphan connection', async () => {
  const h = harness(); const first = h.api.load(); const old = h.requests[0]; old.onblocked(); await assert.rejects(first, /Close other/);
  const next = h.api.load(); assert.equal(h.requests.length, 2); const orphan = h.succeed(old); assert.equal(orphan.closed, 1); h.succeed(h.requests[1]); await h.tick(); h.complete('new'); assert.equal(await next, 'new');
});
test('open errors and synchronous storage exceptions are retryable', async () => {
  const h = harness(); h.openError(new Error('disabled')); await assert.rejects(h.api.load(), /disabled/);
  const read = h.api.load(); h.requests[0].error = new Error('denied'); h.requests[0].onerror(); await assert.rejects(read, /denied/);
  const next = h.api.load(); h.succeed(); await h.tick(); h.complete('retry'); assert.equal(await next, 'retry');
});
test('version change closes the old connection and invalidates its cache', async () => {
  const h = harness(); const p = h.api.load(); const db = h.succeed(); await h.tick(); h.complete('one'); await p;
  db.onversionchange(); assert.equal(db.closed, 1); const next = h.api.load(); assert.equal(h.requests.length, 2); h.succeed(); await h.tick(); h.complete('two'); assert.equal(await next, 'two');
});
test('request success followed by transaction abort does not report a successful read', async () => {
  const h = harness(); const p = h.api.load(); h.succeed(); await h.tick(); const tx = h.transactions[0]; tx.request.result = 'uncommitted'; tx.request.onsuccess(); tx.onabort(); await assert.rejects(p, /aborted/);
});
test('write success is reported only after commit and abort is rejected', async () => {
  const h = harness(); const p = h.api.save('{}'); h.succeed(); await h.tick(); h.transactions[0].onabort(); await assert.rejects(p, /aborted/);
  const next = h.api.save('{"x":1}'); await h.tick(); h.complete('recovery'); assert.equal(await next, 'saved');
});
test('nontext recovery records fail rather than silently becoming empty projects', async () => {
  const h = harness(); const p = h.api.load(); h.succeed(); await h.tick(); h.complete({ corrupted: true }); await assert.rejects(p, /must be text/);
});
test('size limits count UTF-8 bytes, not just UTF-16 units', async () => {
  const h = harness(); await assert.rejects(h.api.save('€'.repeat(3 * 1024 * 1024)), /8 MiB/); assert.equal(h.requests.length, 0);
});
test('invalid closed connection is discarded and a subsequent call can reopen', async () => {
  const h = harness(); const p = h.api.load(); const db = h.succeed(); h.transactionError(Object.assign(new Error('closed'), { name: 'InvalidStateError' })); await assert.rejects(p, /closed/); assert.equal(db.closed, 1);
  const next = h.api.load(); h.succeed(); await h.tick(); h.complete('{}'); assert.equal(await next, '{}');
});
test('cancel closes the picker and a later picker can reopen', async () => {
  const h = harness(); const p = h.api.open(); await assert.rejects(h.api.open(), /already open/); h.elements[0].emit('cancel'); assert.equal(await p, ''); assert.equal(h.elements[0].removed, true); assert.equal(h.listeners.size, 0);
  const next = h.api.open(); h.elements[1].emit('cancel'); assert.equal(await next, '');
});
test('focus cancellation fallback does not cancel an in-flight selected-file read', async () => {
  const h = harness(); const p = h.api.open(); let done;
  h.elements[0].files = [{ size: 2, text: () => new Promise(r => { done = r; }) }]; h.elements[0].emit('change'); h.listeners.get('focus')(); for (const fn of h.timers.values()) fn(); done('{}'); assert.equal(await p, '{}'); assert.equal(h.listeners.size, 0);
});
test('legacy focus cancellation resolves empty and cleans up listeners', async () => {
  const h = harness(); const p = h.api.open(); h.listeners.get('focus')(); for (const fn of [...h.timers.values()]) fn(); assert.equal(await p, ''); assert.equal(h.elements[0].removed, true);
});
test('oversized file is rejected before reading it', async () => {
  const h = harness(); const p = h.api.open(); h.elements[0].files = [{ size: 9 * 1024 * 1024, text: () => { throw new Error('must not read'); } }]; h.elements[0].emit('change'); await assert.rejects(p, /8 MiB/);
});
test('downloads sanitize filenames, remove anchors and revoke object URLs', async () => {
  const h = harness(); assert.equal(await h.api.download('../x\n.json', '{}', 'application/json'), 'downloaded'); assert.equal(h.elements[0].download, '.._x_.json'); assert.equal(h.elements[0].removed, true); for (const fn of h.timers.values()) fn(); assert.deepEqual(h.revoked, ['blob:test']);
});
