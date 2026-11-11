/*
 * Vivarium — zero-dependency dev server
 * Serves the repo root and a /api/themes index for the browser preview harness.
 * Usage: node tools/dev-server.mjs   →   http://localhost:5179
 */
import http from 'node:http';
import path from 'node:path';
import { readFile, readdir, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = Number(process.env.PORT || 5179);

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.webp': 'image/webp',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.mp4': 'video/mp4',
  '.webm': 'video/webm',
  '.txt': 'text/plain; charset=utf-8',
  '.md': 'text/plain; charset=utf-8',
};

async function themeIndex() {
  const base = path.join(root, 'wallpapers');
  const entries = await readdir(base, { withFileTypes: true });
  const themes = [];
  for (const entry of entries) {
    if (!entry.isDirectory() || entry.name.startsWith('_')) continue;
    try {
      const raw = await readFile(path.join(base, entry.name, 'theme.json'), 'utf8');
      const manifest = JSON.parse(raw);
      themes.push({
        id: manifest.id || entry.name,
        name: manifest.name || entry.name,
        category: manifest.category || 'Other',
        tagline: manifest.tagline || '',
        desc: manifest.desc || '',
        tags: manifest.tags || [],
        thumb: manifest.thumb || null,
        params: manifest.params || [],
      });
    } catch {
      /* not a theme folder */
    }
  }
  themes.sort((a, b) => a.name.localeCompare(b.name));
  return themes;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url || '/', 'http://localhost');
    let pathname = decodeURIComponent(url.pathname);

    if (pathname === '/api/themes') {
      const body = JSON.stringify(await themeIndex());
      res.writeHead(200, { 'content-type': MIME['.json'], 'cache-control': 'no-store' });
      res.end(body);
      return;
    }

    /* Dev-only capture helper: the preview page posts a canvas data URL and
       the server stores it under docs/screenshots. Lets us QA scenes without
       a screenshot API. */
    if (pathname === '/api/shot' && req.method === 'POST') {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const dataUrl = Buffer.concat(chunks).toString('utf8');
      const match = /^data:image\/(png|jpeg);base64,(.+)$/s.exec(dataUrl);
      if (!match) {
        res.writeHead(400, { 'content-type': 'text/plain' });
        res.end('expected a canvas data URL');
        return;
      }
      const name = (url.searchParams.get('name') || 'shot').replace(/[^a-z0-9_-]/gi, '');
      const dir = path.join(root, 'docs', 'screenshots');
      await mkdir(dir, { recursive: true });
      const file = path.join(dir, (name || 'shot') + '.' + (match[1] === 'png' ? 'png' : 'jpg'));
      await writeFile(file, Buffer.from(match[2], 'base64'));
      res.writeHead(200, { 'content-type': MIME['.json'], 'cache-control': 'no-store' });
      res.end(JSON.stringify({ ok: true, file }));
      return;
    }

    if (pathname === '/') pathname = '/wallpapers/_runtime/preview.html';

    const file = path.normalize(path.join(root, pathname));
    if (!file.startsWith(root)) {
      res.writeHead(403);
      res.end('forbidden');
      return;
    }
    const data = await readFile(file);
    res.writeHead(200, {
      'content-type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream',
      'cache-control': 'no-store',
    });
    res.end(data);
  } catch {
    res.writeHead(404, { 'content-type': 'text/plain' });
    res.end('not found');
  }
});

server.listen(PORT, () => {
  console.log('Vivarium dev server → http://localhost:' + PORT);
  console.log('Preview harness  → http://localhost:' + PORT + '/wallpapers/_runtime/preview.html');
});
