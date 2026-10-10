import { createReadStream } from 'node:fs';
import { cp, lstat, readFile, readdir, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const MAX_FILE_BYTES = 100 * 1024 * 1024;
export const MAX_SITE_BYTES = 1_000_000_000;

async function checkTree(directory) {
  if ((await lstat(directory)).isSymbolicLink()) throw new Error(`Refusing site symlink: ${directory}`);
  let bytes = 0;
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error(`Refusing site symlink: ${entry.name}`);
    if (entry.isDirectory()) bytes += await checkTree(file);
    else {
      if (!entry.isFile()) throw new Error(`Refusing non-file site entry: ${entry.name}`);
      const size = (await lstat(file)).size;
      if (size >= MAX_FILE_BYTES) throw new Error(`Site file must be below 100 MiB: ${file}`);
      bytes += size;
    }
  }
  return bytes;
}

export async function buildSite(source, output) {
  const bytes = await checkTree(source);
  if (bytes >= MAX_SITE_BYTES) throw new Error('Published site must be below 1 GB');
  const media = JSON.parse(await readFile(path.join(source, 'media.json'), 'utf8'));
  if (!/^[A-Za-z0-9_-]+\.mp4$/.test(media.file) ||
      !Number.isSafeInteger(media.bytes) || media.bytes <= 0 || media.bytes >= MAX_FILE_BYTES ||
      !/^[a-f0-9]{64}$/.test(media.sha256)) throw new Error('Invalid bundled tutorial identity in site/media.json');
  const video = path.join(source, 'media', media.file);
  if ((await lstat(video)).size !== media.bytes) throw new Error('Tutorial byte count does not match site/media.json');
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(video)) hash.update(chunk);
  if (hash.digest('hex') !== media.sha256) throw new Error('Tutorial SHA256 does not match site/media.json');

  // Validate every input before replacing the previous generated preview.
  await rm(output, { recursive: true, force: true });
  await cp(source, output, { recursive: true });
  return { media, bytes };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  if (process.argv.length !== 2) throw new Error('Usage: node tools/Site/build.mjs (uses the bundled site/media video)');
  const output = path.join(root, 'artifacts', 'site');
  const { media, bytes } = await buildSite(path.join(root, 'site'), output);
  console.log(`Built self-contained site: ${output}\nTotal: ${bytes} bytes\nTutorial: ${media.bytes} bytes, SHA256 ${media.sha256}`);
}
