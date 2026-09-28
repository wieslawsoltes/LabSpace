"""Serve the Pages base path locally; do not proxy unknown URLs or execute project input."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit

class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.json': 'application/json'}
    def do_GET(self):
        path = urlsplit(self.path).path
        if path in ('/', '/LabSpace'):
            self.send_response(302)
            self.send_header('Location', '/LabSpace/')
            self.end_headers()
            return
        if path.startswith('/LabSpace/'):
            self.path = self.path[len('/LabSpace'):]
        super().do_GET()
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()

parser = argparse.ArgumentParser()
parser.add_argument('--directory', required=True)
parser.add_argument('--port', type=int, default=4173)
args = parser.parse_args()
ThreadingHTTPServer(('127.0.0.1', args.port), partial(Handler, directory=args.directory)).serve_forever()
