"""Collect the actual Uno distribution and record deployment provenance."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys

source, destination = map(Path, sys.argv[1:3])
candidates = sorted(source.rglob('index.html'), key=lambda p: len(p.parts))
if not candidates:
    raise SystemExit('Uno publish produced no index.html.')
root = next((p.parent for p in candidates if list(p.parent.rglob('*.wasm'))), None)
if root is None:
    raise SystemExit('No real WebAssembly distribution was found.')
if destination.exists():
    shutil.rmtree(destination)
shutil.copytree(root, destination)
(destination / '.nojekyll').write_text('')
sha = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
(destination / 'build-info.json').write_text(json.dumps({'application': 'LabSpace', 'version': '0.1.0-alpha.1', 'commit': sha, 'runtime': 'Uno WebAssembly', 'unoSdk': '6.7.30', 'dotnetSdk': '10.0.401'}, indent=2))
print('Collected', root, 'to', destination, 'commit', sha)
