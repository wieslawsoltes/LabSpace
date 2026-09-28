"""Collect the real Uno distribution and record source-derived deployment provenance."""
from pathlib import Path
import json
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
if len(sys.argv) != 3:
    raise SystemExit('Usage: collect-site.py <publish-directory> <site-directory>')
source, destination = map(Path, sys.argv[1:3])
candidates = sorted(source.rglob('index.html'), key=lambda p: len(p.parts))
root = next((p.parent for p in candidates if any(p.parent.rglob('*.wasm'))), None)
if root is None:
    raise SystemExit('No real Uno WebAssembly distribution was found.')
source_path, destination_path = root.resolve(), destination.resolve()
if source_path == destination_path or source_path in destination_path.parents or destination_path in source_path.parents:
    raise SystemExit('Source and destination must not contain one another.')
sha = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
if not re.fullmatch(r'[0-9a-fA-F]{40}', sha):
    raise SystemExit('A full Git commit SHA is required for deployment provenance.')
version = ET.parse(repo / 'Directory.Build.props').findtext('./PropertyGroup/Version')
sdk = json.loads((repo / 'global.json').read_text(encoding='utf-8'))
if not version:
    raise SystemExit('Directory.Build.props must declare a package version.')
if destination.exists():
    shutil.rmtree(destination)
shutil.copytree(root, destination)
(destination / '.nojekyll').write_text('', encoding='utf-8')
(destination / 'build-info.json').write_text(json.dumps({
    'application': 'LabSpace', 'version': version, 'commit': sha,
    'runtime': 'Uno WebAssembly', 'unoSdk': sdk['msbuild-sdks']['Uno.Sdk'],
    'dotnetSdk': sdk['sdk']['version']
}, indent=2) + '\n', encoding='utf-8')
print('Collected', root, 'to', destination, 'version', version, 'commit', sha)
