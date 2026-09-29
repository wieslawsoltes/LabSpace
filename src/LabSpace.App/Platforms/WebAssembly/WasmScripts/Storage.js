(() => {
  'use strict';
  const MAX = 8 * 1024 * 1024;
  const testMode = new URLSearchParams(location.search).get('test') === '1';
  const encoder = new TextEncoder();
  let dbPromise;
  let pickerActive = false;
  function checkedText(value) {
    if (typeof value !== 'string') throw new TypeError('Project data must be text.');
    if (value.length > MAX || encoder.encode(value).length > MAX) throw new Error('Project exceeds the 8 MiB limit.');
    return value;
  }
  function database() {
    if (dbPromise) return dbPromise;
    let resolve, reject, settled = false;
    const pending = new Promise((yes, no) => { resolve = yes; reject = no; });
    dbPromise = pending;
    const forget = () => { if (dbPromise === pending) dbPromise = undefined; };
    const fail = error => { if (settled) return; settled = true; forget(); reject(error); };
    try {
      const request = indexedDB.open('LabSpace', 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains('projects')) request.result.createObjectStore('projects');
      };
      request.onerror = () => fail(request.error ?? new Error('Could not open project storage.'));
      request.onblocked = () => fail(new Error('Close other LabSpace tabs to update local storage, then retry.'));
      request.onsuccess = () => {
        const db = request.result;
        // A blocked/error request may later succeed. Never retain an orphan connection or clear a newer attempt.
        if (settled) { db.close(); return; }
        settled = true;
        db.onversionchange = () => { forget(); db.close(); };
        db.onclose = forget;
        resolve(db);
      };
    } catch (error) { fail(error); }
    return pending;
  }
  async function transaction(mode, operation) {
    const db = await database();
    return new Promise((resolve, reject) => {
      let tx, result;
      try {
        tx = db.transaction('projects', mode);
        tx.oncomplete = () => resolve(result);
        tx.onerror = () => reject(tx.error ?? new Error('Project storage transaction failed.'));
        tx.onabort = () => reject(tx.error ?? new Error('Project storage transaction aborted.'));
        const request = operation(tx.objectStore('projects'));
        request.onerror = () => reject(request.error ?? new Error('Project storage request failed.'));
        request.onsuccess = () => { result = request.result; };
      } catch (error) {
        if (error?.name === 'InvalidStateError') { dbPromise = undefined; db.close(); }
        reject(error);
      }
    });
  }
  globalThis.labSpaceStorage = Object.freeze({
    async load() {
      const result = await transaction('readonly', store => store.get('recovery'));
      // A malformed recovery record is an error, not an empty project that autosave may overwrite.
      return result === undefined ? '' : checkedText(result);
    },
    async save(json) {
      checkedText(json);
      await transaction('readwrite', store => store.put(json, 'recovery'));
      return 'saved';
    },
    open() {
      if (pickerActive) return Promise.reject(new Error('A project picker is already open.'));
      pickerActive = true;
      return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file'; input.accept = '.json,application/json'; input.style.display = 'none';
        let finished = false, reading = false, focusTimer;
        const complete = (value, error) => {
          if (finished) return;
          finished = true; pickerActive = false;
          clearTimeout(focusTimer); globalThis.removeEventListener('focus', onFocus); input.remove();
          error ? reject(error) : resolve(value);
        };
        // Fallback for hosts that do not emit HTMLInputElement.cancel. A selected file or active read wins.
        const onFocus = () => {
          clearTimeout(focusTimer);
          focusTimer = setTimeout(() => { if (!reading && !input.files?.length) complete(''); }, 300);
        };
        input.addEventListener('cancel', () => complete(''), { once: true });
        input.addEventListener('change', async () => {
          reading = true;
          try {
            const file = input.files?.[0];
            if (!file) { complete(''); return; }
            if (file.size > MAX) throw new Error('Project exceeds the 8 MiB limit.');
            complete(checkedText(await file.text()));
          } catch (error) { complete('', error); }
        }, { once: true });
        globalThis.addEventListener('focus', onFocus);
        try { document.body.append(input); input.click(); } catch (error) { complete('', error); }
      });
    },
    async download(name, content, contentType) {
      checkedText(content);
      const blob = new Blob([content], { type: contentType }); const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      try {
        anchor.href = url; anchor.download = String(name).replace(/[\\/\u0000-\u001f\u007f]/g, '_').slice(0, 180) || 'LabSpace-project.json';
        document.body.append(anchor); anchor.click();
      } finally { anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000); }
      return 'downloaded';
    },
    isTestMode: () => testMode,
    publishDiagnostics(json) { if (testMode) globalThis.labSpaceDiagnostics = JSON.parse(json); }
  });
})();
