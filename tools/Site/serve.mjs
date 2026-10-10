import http from 'node:http';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'artifacts', 'site');
const port = Number(process.argv[2] || 4173);
const types = { '.html': 'text/html', '.css': 'text/css', '.mjs': 'text/javascript', '.js': 'text/javascript', '.json': 'application/json', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg', '.gif': 'image/gif', '.mp4': 'video/mp4', '.vtt': 'text/vtt', '.txt': 'text/plain', '.srt': 'text/plain' };
const server = http.createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const relative = pathname.replace(/^\/phantom-fudge(?=\/|$)/, '').replace(/^\/+/, '');
    const file = path.resolve(root, relative || 'index.html');
    if (file !== root && !file.startsWith(root + path.sep)) {
      response.writeHead(403); response.end(); return;
    }
    const info = await stat(file);
    if (!info.isFile()) { response.writeHead(404); response.end(); return; }
    response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
    response.setHeader('Accept-Ranges', 'bytes');
    response.setHeader('Cache-Control', 'no-store');
    let start = 0;
    let end = info.size - 1;
    if (request.headers.range) {
      const match = /^bytes=(\d+)-(\d*)$/.exec(request.headers.range);
      if (!match || Number(match[1]) >= info.size || (match[2] && Number(match[2]) < Number(match[1]))) {
        response.writeHead(416, { 'Content-Range': `bytes */${info.size}` }); response.end(); return;
      }
      start = Number(match[1]);
      if (match[2]) end = Math.min(Number(match[2]), end);
      response.writeHead(206, { 'Content-Range': `bytes ${start}-${end}/${info.size}`, 'Content-Length': end - start + 1 });
    } else response.writeHead(200, { 'Content-Length': info.size });
    if (request.method === 'HEAD') { response.end(); return; }
    const stream = createReadStream(file, { start, end });
    stream.on('error', error => response.destroy(error));
    response.on('close', () => stream.destroy());
    stream.pipe(response);
  } catch (error) {
    if (error.code === 'ENOENT') { response.writeHead(404); response.end('Not found'); return; }
    console.error(error);
    response.writeHead(500); response.end('Preview server error');
  }
});
server.listen(port, '127.0.0.1', () => console.log(`Preview: http://127.0.0.1:${port}/phantom-fudge/`));
