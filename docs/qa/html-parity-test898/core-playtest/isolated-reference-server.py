from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path
from urllib.request import urlopen
from urllib.parse import urlsplit
ROOT=Path(r'D:/repos/AshenSpire-Unity/docs/qa/html-parity-test898/core-playtest')
class Handler(BaseHTTPRequestHandler):
 def do_GET(self):
  path=urlsplit(self.path).path
  if path in ('/','/index.html'):
   payload=(ROOT/'published898.html').read_bytes(); mime='text/html'
  elif path=='/asset-base.json': payload=b'{"base":"./"}'; mime='application/json'
  elif path.startswith(('/packs/','/objects/')):
   try:
    with urlopen('https://cehinds.github.io/AshenSpire'+path,timeout=30) as response: payload=response.read(); mime=response.headers.get('Content-Type','application/octet-stream')
   except Exception as error: self.send_error(502,str(error)); return
  else: self.send_error(404); return
  self.send_response(200); self.send_header('Content-Type',mime); self.send_header('Content-Length',str(len(payload))); self.end_headers(); self.wfile.write(payload)
ThreadingHTTPServer(('127.0.0.1',8899),Handler).serve_forever()
