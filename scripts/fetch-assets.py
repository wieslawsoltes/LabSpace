"""Fetch OFL assets and verify immutable upstream Git blob hashes."""
import base64
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1] / 'src/LabSpace.App/Assets/Fonts'
ASSETS = {'Carlito-Regular.ttf': '427b95989fee609ab683d800ad00e22a4b14ecad', 'OFL.txt': '8d6b170e4d9a5580a34471dc4ac3e8fc5fbbd712'}

def git_hash(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()

def download(url, api=False):
    headers = {'User-Agent': 'LabSpace-asset-fetcher'}
    if api:
        headers['Accept'] = 'application/vnd.github+json'
        if os.environ.get('GH_TOKEN'):
            headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=45) as response:
        data = response.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024:
        raise RuntimeError('Asset exceeds the download limit.')
    return base64.b64decode(json.loads(data)['content']) if api else data

def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    for name, expected in ASSETS.items():
        path = ROOT / name
        if path.exists() and git_hash(path.read_bytes()) == expected:
            continue
        sources = [(f'https://raw.githubusercontent.com/google/fonts/main/ofl/carlito/{name}', False), (f'https://api.github.com/repos/google/fonts/git/blobs/{expected}', True)]
        last_error = None
        for attempt in range(3):
            for url, api in sources:
                try:
                    data = download(url, api)
                    if git_hash(data) != expected:
                        raise RuntimeError('Asset content hash mismatch: ' + name)
                    temporary = path.with_suffix(path.suffix + '.tmp')
                    temporary.write_bytes(data)
                    temporary.replace(path)
                    print('Verified', name, expected, flush=True)
                    break
                except Exception as error:
                    last_error = error
            else:
                time.sleep(2 ** attempt)
                continue
            break
        else:
            raise RuntimeError('Could not retrieve verified asset ' + name) from last_error

if __name__ == '__main__':
    main()
