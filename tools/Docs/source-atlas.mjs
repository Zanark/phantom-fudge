import fs from 'node:fs/promises';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import puppeteer from 'puppeteer-core';
import { hash, safeRepoPath, slug } from './build.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..', '..');
const output = path.join(root, 'docs', 'source-atlas');
const rootInputs = new Set(['.dockerignore', '.gitignore', 'Directory.Build.props', 'PhantomFudge.slnx',
  'compose.yaml', 'global.json', 'docs/handbook/book.json', 'docs/handbook/catalogue.json']);
const extensions = new Set(['.cs', '.xaml', '.py', '.mjs', '.js', '.ps1', '.json', '.yaml', '.yml',
  '.toml', '.props', '.csproj', '.slnx', '.html', '.css', '.iss', '.manifest']);
const relative = file => path.relative(root, file).replaceAll(path.sep, '/');
export const escapeHtml = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
const fileId = file => 'file-' + slug(file) + '-' + hash(file).slice(0, 8);

export function physicalLines(text) {
  if (text.length === 0) return [];
  const lines = text.replace(/^\uFEFF/, '').replace(/\r\n|\r/g, '\n').split('\n');
  if (lines.at(-1) === '') lines.pop();
  return lines;
}

export function atlasChapterLink(chapter) {
  if (typeof chapter !== 'string' || !/^docs\/(?:handbook\/)?[A-Za-z0-9_-]+\.md$/.test(chapter))
    throw new Error('Atlas groups require a public documentation chapter.');
  return path.posix.relative('docs/source-atlas', chapter);
}

export function sourceKind(file) {
  if (rootInputs.has(file)) return 'annotated';
  if (!/^(?:src|tests|services|scripts|tools|spikes|\.github)\//.test(file)) return null;
  if (/(?:^|\/)(?:bin|obj|node_modules|\.venv|\.local|\.agent-context|\.git|__pycache__|TestResults)(?:\/|$)/i.test(file)) return null;
  if (file.includes('/results/')) return 'evidence';
  if (/(?:^|\/)(?:package-lock|packages\.lock)\.json$/.test(file)) return 'lockfile';
  if (extensions.has(path.posix.extname(file)) || path.posix.basename(file) === 'Dockerfile' ||
      /(?:^|\/)[^/]*requirements\.txt$/.test(file)) return 'annotated';
  if (file.endsWith('.txt')) return 'possible-mirror';
  return null;
}

async function readPublic(file) {
  const absolute = safeRepoPath(root, file);
  const real = await fs.realpath(absolute);
  safeRepoPath(root, real);
  if (path.resolve(real).toLowerCase() !== absolute.toLowerCase())
    throw new Error(`Source atlas does not follow symlink aliases: ${file}`);
  return fs.readFile(absolute);
}

export async function discoverSources() {
  const listed = execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'],
    { cwd: root, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 }).split('\0').filter(Boolean);
  const names = [...new Set(listed)].sort();
  const known = new Set(names);
  const entries = [];
  for (const file of names) {
    let kind = sourceKind(file);
    if (!kind) continue;
    const canonical = kind === 'possible-mirror' ? file.slice(0, -4) + '.ps1' : null;
    if (kind === 'possible-mirror') {
      if (!known.has(canonical)) continue;
      kind = 'mirror';
    }
    const bytes = await readPublic(file);
    if (kind === 'mirror' && !bytes.equals(await readPublic(canonical)))
      throw new Error(`PowerShell mirror differs from its canonical source: ${file}`);
    entries.push({ path: file, kind, sha256: hash(bytes), bytes: bytes.length,
      lines: physicalLines(bytes.toString('utf8')).length, ...(canonical ? { canonical } : {}) });
  }
  return entries;
}

function exactKeys(value, allowed, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      Object.keys(value).some(key => !allowed.includes(key))) throw new Error(`Invalid ${label} fields.`);
}

function text(value, label, minimum = 1) {
  if (typeof value !== 'string' || value.trim().length < minimum)
    throw new Error(`${label} needs substantive text.`);
}

export function validateFileAnnotation(file, bytes) {
  exactKeys(file, ['path', 'sourceSha256', 'role', 'entryPoints', 'blocks'], 'file annotation');
  text(file.path, 'Source path');
  if (file.path.includes('\\') || file.path.startsWith('/') || file.path.split('/').includes('..'))
    throw new Error(`Use a portable repository-relative source path: ${file.path}`);
  if (file.sourceSha256 !== hash(bytes)) throw new Error(`Annotations are stale for ${file.path}; re-read the changed source.`);
  text(file.role, `${file.path} responsibility`, 25);
  if (!Array.isArray(file.entryPoints) || !file.entryPoints.length) throw new Error(`${file.path} needs entry points/readers.`);
  for (const entry of file.entryPoints) text(entry, 'Entry point');
  const lines = physicalLines(bytes.toString('utf8'));
  if (!Array.isArray(file.blocks) || (lines.length > 0 && !file.blocks.length)) throw new Error(`${file.path} needs authored blocks.`);
  let next = 1;
  for (const block of file.blocks) {
    exactKeys(block, ['start', 'end', 'title', 'explanation', 'focus'], 'block annotation');
    if (!Number.isInteger(block.start) || !Number.isInteger(block.end) || block.start !== next ||
        block.end < block.start || block.end > lines.length)
      throw new Error(`${file.path}: gap, overlap or invalid block at ${block.start}-${block.end}; expected ${next}.`);
    const maximum = /(?:\/tests\/|\/[^/]*Tests\/|Diagnostics\.cs$)/i.test(file.path) ? 120 : 80;
    if (block.end - block.start + 1 > maximum)
      throw new Error(`${file.path}:${block.start}-${block.end}: split this block at meaningful boundaries (max ${maximum}).`);
    text(block.title, 'Block title', 4);
    text(block.explanation, `${file.path}:${block.start} explanation`, 50);
    if (block.focus !== undefined && !Array.isArray(block.focus)) throw new Error('Focus must be a range array.');
    for (const focus of block.focus ?? []) {
      exactKeys(focus, ['start', 'end'], 'focus range');
      if (!Number.isInteger(focus.start) || !Number.isInteger(focus.end) || focus.start < block.start ||
          focus.end > block.end || focus.end < focus.start || focus.end - focus.start + 1 > 12)
        throw new Error(`${file.path}:${block.start}: focus must be inside the block and at most 12 lines.`);
    }
    next = block.end + 1;
  }
  if (next !== lines.length + 1) throw new Error(`${file.path}: annotations stop at ${next - 1}, source ends at ${lines.length}.`);
  return { lines, nonblankLines: lines.filter(line => line.trim()).length };
}

async function loadAtlas() {
  const inventory = await discoverSources();
  const expected = new Map(inventory.filter(entry => entry.kind === 'annotated').map(entry => [entry.path, entry]));
  const inputs = Object.fromEntries(inventory.map(entry => [entry.path, entry.sha256]));
  const files = [];
  const groups = [];
  const seen = new Set();
  const annotationRoot = path.join(output, 'annotations');
  const annotations = (await fs.readdir(annotationRoot)).filter(file => file.endsWith('.json')).sort();
  for (const name of annotations) {
    const file = relative(path.join(annotationRoot, name));
    const bytes = await readPublic(file);
    inputs[file] = hash(bytes);
    const group = JSON.parse(bytes.toString('utf8'));
    exactKeys(group, ['schemaVersion', 'id', 'title', 'chapter', 'files'], 'annotation group');
    if (group.schemaVersion !== 1 || groups.some(item => item.id === group.id)) throw new Error('Invalid/duplicate atlas group.');
    text(group.id, 'Group ID');
    text(group.title, 'Group title');
    if (!Array.isArray(group.files) || !group.files.length) throw new Error('An atlas group must explain source files.');
    atlasChapterLink(group.chapter);
    inputs[group.chapter] = hash(await readPublic(group.chapter));
    for (const item of group.files) {
      if (!expected.has(item.path) || seen.has(item.path)) throw new Error(`Unexpected or duplicate annotated source: ${item.path}`);
      const source = await readPublic(item.path);
      const validated = validateFileAnnotation(item, source);
      seen.add(item.path);
      files.push({ ...item, ...validated, id: fileId(item.path), group: group.id, chapter: group.chapter });
    }
    groups.push({ id: group.id, title: group.title, chapter: group.chapter, files: group.files.length });
  }
  const missing = [...expected.keys()].filter(file => !seen.has(file));
  if (missing.length) throw new Error(`Missing authored source coverage (${missing.length}):\n${missing.join('\n')}`);
  if (!files.length) throw new Error('An empty source atlas cannot be complete.');
  return { files, groups, inventory, inputs,
    lineCount: files.reduce((sum, file) => sum + file.lines.length, 0),
    nonblankLineCount: files.reduce((sum, file) => sum + file.nonblankLines, 0),
    blockCount: files.reduce((sum, file) => sum + file.blocks.length, 0) };
}

const paragraphs = value => value.split(/\n\s*\n/).map(part => `<p>${escapeHtml(part)}</p>`).join('');
const sourceRows = (file, start, end, full) => file.lines.slice(start - 1, end).map((line, index) =>
  `<div class="source-line"${full ? ` id="${file.id}-L${start + index}"` : ''}>` +
  `<a class="line-number" href="#${file.id}-L${start + index}">${start + index}</a>` +
  `<code>${escapeHtml(line) || ' '}</code></div>`).join('\n');

function renderFile(file) {
  const blocks = file.blocks.map(block => {
    const focus = block.focus?.length ? block.focus : [{ start: block.start, end: Math.min(block.end, block.start + 7) }];
    return `<section class="annotation" id="${file.id}-B${block.start}"><h3>` +
      `<a href="#${file.id}-L${block.start}">L${block.start}-${block.end}</a>: ${escapeHtml(block.title)}</h3>` +
      paragraphs(block.explanation) + focus.map(range =>
        `<div class="excerpt">${sourceRows(file, range.start, range.end, false)}</div>`).join('') + '</section>';
  }).join('\n');
  return `<article class="source-file" id="${file.id}" data-group="${escapeHtml(file.group)}">` +
    `<h2>${escapeHtml(file.path)}</h2><p class="role">${escapeHtml(file.role)}</p>` +
    `<p class="metadata">${file.lines.length} physical lines; ${file.blocks.length} authored blocks. ` +
    `Entry points/readers: ${file.entryPoints.map(escapeHtml).join(', ')}. ` +
    `<a href="${escapeHtml(atlasChapterLink(file.chapter))}">Deep walkthrough</a></p>` +
    `<details class="full-source"><summary>Open complete line-numbered source (${file.lines.length} lines)</summary>` +
    `<div class="source-code">${sourceRows(file, 1, file.lines.length, true)}</div></details>` +
    blocks + '<p class="top-link"><a href="#top">Back to index</a></p></article>';
}

async function makeHtml(atlas) {
  const css = await fs.readFile(path.join(here, 'source-atlas.css'), 'utf8');
  const client = await fs.readFile(path.join(here, 'source-atlas-client.mjs'), 'utf8');
  if (/<\/script/i.test(client)) throw new Error('Atlas client cannot contain a script-closing sequence.');
  return '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">' +
    `<title>Phantom Fudge Annotated Source Atlas</title><style>${css}</style></head><body>` +
    `<header id="top"><p class="eyebrow">Source-level companion</p><h1>Phantom Fudge: Annotated Source Atlas</h1>` +
    `<p>${atlas.files.length} first-party files &middot; ${atlas.lineCount.toLocaleString('en-US')} physical lines &middot; ` +
    `${atlas.blockCount} authored explanations</p>` +
    '<p>Every in-scope physical line belongs to an explanation block. This is a coverage contract, not proof ' +
    'that every subtle behavior, external library or real-device interaction has been verified.</p>' +
    '<p><a href="../handbook/Phantom-Fudge-Engineering-Handbook.html">Engineering handbook</a> &middot; ' +
    '<a href="Phantom-Fudge-Source-Atlas.pdf">Printable annotations</a> &middot; <a href="coverage.json">Coverage ledger</a></p></header>' +
    '<aside class="search"><label for="search">Find code, an identifier, a file or an explanation</label>' +
    '<input id="search" type="search" autocomplete="off" placeholder="Try: BeginConcurrentShutdown or 8192">' +
    `<p id="matches" aria-live="polite">${atlas.files.length} files shown</p></aside>` +
    `<nav class="file-index" aria-label="Source files">${atlas.groups.map(group => `<section><h2>${escapeHtml(group.title)}</h2>` +
      atlas.files.filter(file => file.group === group.id).map(file =>
        `<a data-file="${file.id}" href="#${file.id}">${escapeHtml(file.path)}</a>`).join('') + '</section>').join('')}</nav>` +
    `<main>${atlas.files.map(renderFile).join('\n')}</main><script>${client}</script></body></html>`;
}

async function launchBrowser() {
  const candidates = [process.env.PHANTOM_DOCS_BROWSER,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'].filter(Boolean);
  let executablePath;
  for (const file of candidates) {
    try { await fs.access(file); executablePath = file; break; }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  if (!executablePath) throw new Error('Set PHANTOM_DOCS_BROWSER to an installed browser; no download is attempted.');
  return puppeteer.launch({ executablePath, headless: true, pipe: true,
    args: ['--disable-background-networking', '--disable-component-update', '--disable-sync',
      '--no-first-run', '--disable-default-apps', '--metrics-recording-only'] });
}

export function validateAtlasManifest(manifest, counts) {
  const expected = ['index.html', 'Phantom-Fudge-Source-Atlas.pdf', 'coverage.json', 'README.md']
    .map(name => `docs/source-atlas/${name}`);
  if (manifest.schemaVersion !== 1 || manifest.complete !== true || !manifest.pdf ||
      !Number.isInteger(manifest.pdf.pages) || manifest.pdf.pages < counts.files ||
      !Array.isArray(manifest.artifacts) || manifest.artifacts.length !== expected.length)
    throw new Error('Build a complete source atlas including PDF first.');
  for (const [name, value] of Object.entries(counts))
    if (manifest.counts?.[name] !== value) throw new Error(`Source atlas count mismatch: ${name}`);
  const artifacts = new Map(manifest.artifacts.map(item => [item.file, item.sha256]));
  if (artifacts.size !== expected.length || expected.some(file => !/^[a-f0-9]{64}$/.test(artifacts.get(file) ?? '')) ||
      manifest.pdf.file !== expected[1] || manifest.pdf.sha256 !== artifacts.get(expected[1]))
    throw new Error('Source atlas output manifest is incomplete or inconsistent.');
}

export async function verifySourceAtlas() {
  const atlas = await loadAtlas();
  const manifest = JSON.parse((await readPublic('docs/source-atlas/manifest.json')).toString('utf8'));
  validateAtlasManifest(manifest, { files: atlas.files.length, lines: atlas.lineCount,
    nonblankLines: atlas.nonblankLineCount, blocks: atlas.blockCount, groups: atlas.groups.length });
  if (JSON.stringify(Object.keys(atlas.inputs).sort()) !== JSON.stringify(Object.keys(manifest.inputs).sort()))
    throw new Error('Source atlas inventory changed; update annotations and rebuild.');
  for (const [file, expected] of Object.entries(manifest.inputs))
    if (atlas.inputs[file] !== expected) throw new Error(`Source atlas input changed: ${file}`);
  for (const artifact of manifest.artifacts)
    if (hash(await readPublic(artifact.file)) !== artifact.sha256) throw new Error(`Stale source atlas output: ${artifact.file}`);
  return manifest;
}

export async function main(args = process.argv.slice(2)) {
  if (args.some(arg => !['--check', '--validate', '--inventory', '--no-pdf'].includes(arg))) throw new Error('Unknown source atlas argument.');
  if (args.includes('--inventory')) { console.log(JSON.stringify(await discoverSources(), null, 2)); return; }
  if (args.includes('--check')) {
    const manifest = await verifySourceAtlas();
    console.log(JSON.stringify({ status: 'current', ...manifest.counts, pdfPages: manifest.pdf.pages }));
    return;
  }
  const atlas = await loadAtlas();
  const counts = { files: atlas.files.length, lines: atlas.lineCount, nonblankLines: atlas.nonblankLineCount,
    blocks: atlas.blockCount, groups: atlas.groups.length };
  if (args.includes('--validate')) { console.log(JSON.stringify({ status: 'coverage-valid', ...counts })); return; }
  const html = await makeHtml(atlas);
  const htmlStage = path.join(output, `.index-${process.pid}.html`);
  const pdfStage = path.join(output, `.atlas-${process.pid}.pdf`);
  const artifacts = [];
  let pdf = null;
  let browser;
  try {
    await fs.writeFile(htmlStage, html, 'utf8');
    if (!args.includes('--no-pdf')) {
      browser = await launchBrowser();
      const page = await browser.newPage();
      await page.setRequestInterception(true);
      page.on('request', request => /^(?:file:|data:|about:|blob:)/.test(request.url())
        ? request.continue() : request.abort('blockedbyclient'));
      await page.goto(pathToFileURL(htmlStage).href, { waitUntil: 'networkidle0' });
      await page.evaluate(() => document.fonts.ready);
      for (const width of [1440, 768, 390]) {
        await page.setViewport({ width, height: 1000 });
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2);
        if (overflow) throw new Error(`Source atlas overflows at ${width}px.`);
      }
      await page.setViewport({ width: 674, height: 1000 });
      await page.emulateMediaType('print');
      await page.pdf({ path: pdfStage, format: 'A4', preferCSSPageSize: true, printBackground: true,
        tagged: true, outline: true, timeout: 180000, displayHeaderFooter: true, headerTemplate: '<div></div>',
        footerTemplate: '<div style="font:9px Arial;width:100%;text-align:center">' +
          'Phantom Fudge Source Atlas | <span class="pageNumber"></span> / <span class="totalPages"></span></div>' });
      const bytes = await fs.readFile(pdfStage);
      const pages = [...bytes.toString('latin1').matchAll(/\/Type\s*\/Page\b/g)].length;
      if (!bytes.subarray(0, 5).equals(Buffer.from('%PDF-')) || pages < atlas.files.length)
        throw new Error('Source atlas PDF is incomplete.');
      pdf = { file: 'docs/source-atlas/Phantom-Fudge-Source-Atlas.pdf', sha256: hash(bytes), pages, bytes: bytes.length };
      await fs.rename(pdfStage, path.join(output, 'Phantom-Fudge-Source-Atlas.pdf'));
      artifacts.push(pdf);
    }
    await fs.rename(htmlStage, path.join(output, 'index.html'));
    artifacts.push({ file: 'docs/source-atlas/index.html', sha256: hash(html) });
    const coverage = { schemaVersion: 1, counts,
      meaning: 'Every in-scope physical line is assigned to an authored block; this is not proof of complete semantic understanding.',
      annotated: atlas.files.map(file => ({ path: file.path, sourceSha256: file.sourceSha256, lines: file.lines.length,
        nonblankLines: file.nonblankLines, blocks: file.blocks.map(({ start, end, title }) => ({ start, end, title })),
        chapter: file.chapter, anchor: file.id })),
      excludedOrMirrored: atlas.inventory.filter(entry => entry.kind !== 'annotated') };
    const readme = '# Annotated source atlas\n\n' +
      '[Open the searchable source reader](index.html) | [Read the printable annotations](Phantom-Fudge-Source-Atlas.pdf) | ' +
      '[Coverage ledger](coverage.json) | [Engineering handbook](../handbook/README.md)\n\n' +
      `${counts.files} first-party files, ${counts.lines} physical lines and ${counts.blocks} authored explanation blocks.\n\n` +
      'Open any file to read its purpose, callers/readers, annotated ranges and focused code excerpts. Expand the full source ' +
      'for stable line links. Search matches code as well as explanations. The PDF prints explanations and selected snippets, ' +
      'not a second dump of every source file.\n\n' +
      '## Coverage boundary\n\nPhysical-line assignment is mechanically checked; it does not prove that every subtlety is understood. ' +
      'External packages, runtime credentials, model weights, build outputs, private context and live device/provider behavior are outside this source walkthrough. ' +
      'Lockfiles and generated evidence receipts are indexed as such. PowerShell `.txt` mirrors are byte-checked against their annotated `.ps1` source.\n\n' +
      '## Source groups\n\n' + atlas.groups.map(group =>
        `- [${group.title}](${atlasChapterLink(group.chapter)}): ${group.files} files.`).join('\n') +
      '\n\n## Maintenance\n\nUpdate the relevant authored JSON in `annotations` after reading changed source, including its source hash and line partitions. ' +
      'Then run `node .\\tools\\Docs\\source-atlas.mjs`, followed by `scripts\\Build-Handbook.ps1`. ' +
      '`node .\\tools\\Docs\\source-atlas.mjs --check` rejects stale source, gaps, missing files, mirror drift or an HTML-only edition. ' +
      'Do not update a hash merely to bypass a stale explanation.\n';
    for (const [name, contents] of [['coverage.json', JSON.stringify(coverage, null, 2) + '\n'], ['README.md', readme]]) {
      await fs.writeFile(path.join(output, name), contents, 'utf8');
      artifacts.push({ file: `docs/source-atlas/${name}`, sha256: hash(contents) });
    }
    const manifest = { schemaVersion: 1, generatedAtUtc: new Date().toISOString(), complete: pdf !== null,
      counts, inputs: atlas.inputs, pdf, artifacts };
    await fs.writeFile(path.join(output, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');
    console.log(JSON.stringify({ status: pdf ? 'complete' : 'intermediate-html-only', ...counts, pdfPages: pdf?.pages }));
  } finally {
    if (browser) await browser.close();
    await fs.rm(htmlStage, { force: true });
    await fs.rm(pdfStage, { force: true });
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url)
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
