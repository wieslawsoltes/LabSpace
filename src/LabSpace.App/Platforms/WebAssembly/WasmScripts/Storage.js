(() => {
  'use strict';
  const MAX = 8 * 1024 * 1024;
  const testMode = new URLSearchParams(location.search).get('test') === '1';
  let dbPromise;
  function database() {
    return dbPromise ??= new Promise((resolve, reject) => {
      const request = indexedDB.open('LabSpace', 1);
      request.onupgradeneeded = () => request.result.createObjectStore('projects');
      request.onerror = () => { dbPromise = null; reject(request.error); };
      request.onsuccess = () => { const db = request.result; db.onversionchange = () => db.close(); resolve(db); };
      request.onblocked = () => reject(new Error('Close other LabSpace tabs to update local storage.'));
    });
  }
  globalThis.labSpaceStorage = Object.freeze({
    async load() {
      const db = await database();
      return new Promise((resolve, reject) => {
        const tx = db.transaction('projects', 'readonly'); const request = tx.objectStore('projects').get('recovery');
        request.onsuccess = () => resolve(typeof request.result === 'string' ? request.result : ''); request.onerror = () => reject(request.error);
      });
    },
    async save(json) {
      if (new TextEncoder().encode(json).length > MAX) throw new Error('Recovery exceeds the 8 MiB limit.');
      const db = await database();
      return new Promise((resolve, reject) => { const tx = db.transaction('projects', 'readwrite'); tx.objectStore('projects').put(json, 'recovery'); tx.oncomplete = () => resolve('saved'); tx.onerror = () => reject(tx.error); tx.onabort = () => reject(tx.error ?? new Error('Recovery transaction aborted.')); });
    },
    open() {
      return new Promise((resolve, reject) => {
        const input = document.createElement('input'); input.type = 'file'; input.accept = '.json,application/json'; input.style.display = 'none'; document.body.append(input); let finished = false;
        const complete = (value, error) => { if (finished) return; finished = true; input.remove(); error ? reject(error) : resolve(value); };
        input.addEventListener('cancel', () => complete(''), { once: true });
        input.addEventListener('change', async () => { try { const file = input.files?.[0]; if (!file) { complete(''); return; } if (file.size > MAX) throw new Error('Project exceeds the 8 MiB limit.'); complete(await file.text()); } catch (error) { complete('', error); } }, { once: true });
        input.click();
      });
    },
    async download(name, content, contentType) {
      const blob = new Blob([content], { type: contentType }); const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = name.replace(/[\\/\u0000-\u001f]/g, '_'); document.body.append(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000); return 'downloaded';
    },
    isTestMode: () => testMode,
    publishDiagnostics(json) { if (testMode) globalThis.labSpaceDiagnostics = JSON.parse(json); }
  });
})();
