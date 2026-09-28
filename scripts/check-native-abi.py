"""Audit the resolved Skia managed/native NuGet versions, not only requested versions."""
import json
from pathlib import Path
import sys

path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('src/LabSpace.App/obj/project.assets.json')
expected = '3.119.4'
libraries = json.loads(path.read_text(encoding='utf-8-sig'))['libraries']
resolved = {name.rsplit('/', 1)[0]: name.rsplit('/', 1)[1] for name in libraries if name.startswith('SkiaSharp/') or name.startswith('SkiaSharp.NativeAssets.')}
print(json.dumps({'expected': expected, 'resolved': resolved}, indent=2))
if resolved.get('SkiaSharp') != expected:
    raise SystemExit('Managed Skia version does not match the pinned Uno-compatible version.')
if not any(name.startswith('SkiaSharp.NativeAssets.') for name in resolved):
    raise SystemExit('No native Skia assets were resolved.')
if any(version != expected for version in resolved.values()):
    raise SystemExit('Mixed managed/native Skia NuGet versions must not be shipped.')
