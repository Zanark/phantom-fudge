import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import MarkdownIt from 'markdown-it';
import puppeteer from 'puppeteer-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..', '..');
const bookRoot = path.join(root, 'docs', 'handbook');
const require = createRequire(import.meta.url);
const theme = {
  theme: 'base', securityLevel: 'strict', startOnLoad: false,
  deterministicIds: true, fontFamily: 'Segoe UI, Arial, sans-serif', fontSize: 18,
  flowchart: { htmlLabels: false, useMaxWidth: true },
  sequence: { width: 110, actorMargin: 30, diagramMarginX: 10, wrap: true,
    useMaxWidth: true, messageMargin: 28 },
  themeVariables: {
    primaryColor: '#2d333b', primaryBorderColor: '#6d5dfc', primaryTextColor: '#e6edf3',
    secondaryColor: '#2d333b', tertiaryColor: '#161b22', lineColor: '#8b949e',
    textColor: '#e6edf3', mainBkg: '#2d333b', nodeBorder: '#6d5dfc',
    clusterBkg: '#161b22', clusterBorder: '#30363d',
    actorBkg: '#2d333b', actorBorder: '#6d5dfc', actorTextColor: '#e6edf3',
    signalColor: '#8b949e', signalTextColor: '#e6edf3',
    labelBoxBkgColor: '#2d333b', labelBoxBorderColor: '#6d5dfc',
    labelTextColor: '#e6edf3', noteBkgColor: '#2d333b', noteTextColor: '#e6edf3'
  }
};

export const hash = value => crypto.createHash('sha256').update(value).digest('hex');
export const slug = value => value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const relative = value => path.relative(root, value).replaceAll(path.sep, '/');
const escape = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
  .replaceAll('"', '&quot;');

export function frontmatter(markdown) {
  markdown = markdown.replace(/^\uFEFF/, '');
  const match = markdown.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!match) throw new Error('VitePress title/description frontmatter is required.');
  const get = name => {
    const line = match[1].match(new RegExp(`^${name}:\\s*"(.+)"\\s*$`, 'm'));
    if (!line) throw new Error(`Quoted ${name} frontmatter is required.`);
    return line[1];
  };
  return { title: get('title'), description: get('description'), body: markdown.slice(match[0].length) };
}

export function citations(markdown) {
  const pattern = /((?:src|tests|services|scripts|tools|docs|TODO|spikes)[\\/][A-Za-z0-9_.\\/-]+|(?:README|AGENTS|JOURNAL)\.md|global\.json|Directory\.Build\.props|PhantomFudge\.slnx|compose\.yaml):(\d+(?:-\d+)?(?:[ \t]*,[ \t]*\d+(?:-\d+)?)*)/g;
  return [...markdown.matchAll(pattern)].flatMap(match => match[2].split(',').map(range => {
    const [start, end = start] = range.trim().split('-').map(Number);
    return { file: match[1].replaceAll('\\', '/'), start, end };
  }));
}

export function validateCatalogue(catalogue) {
  if (!Array.isArray(catalogue.items) || catalogue.items[0]?.title !== 'onboarding' ||
      catalogue.items[0]?.collapsed !== false) throw new Error('Onboarding must be first and uncollapsed.');
  const names = new Set();
  const visit = (nodes, depth) => {
    if (depth > 4 || nodes.length > 8) throw new Error('Catalogue exceeds depth/child limits.');
    for (const node of nodes) {
      if (!node.title || !node.name || !node.prompt || !Array.isArray(node.children))
        throw new Error('Catalogue nodes need title/name/prompt/children.');
      if (names.has(node.title)) throw new Error(`Duplicate catalogue node: ${node.title}`);
      names.add(node.title);
      if (!citations(node.prompt).length) throw new Error(`Catalogue prompt lacks a source: ${node.title}`);
      if (node.children.length) visit(node.children, depth + 1);
    }
  };
  visit(catalogue.items, 1);
}

export function safeRepoPath(base, target) {
  const value = path.resolve(base, target.replaceAll('\\', path.sep));
  const rel = path.relative(root, value);
  if (rel.startsWith('..') || path.isAbsolute(rel) ||
      /(^|[\\/])(?:\.agent-context|\.local|\.git|\.env(?:\.[^\\/]*)?|node_modules)([\\/]|$)/i.test(rel))
    throw new Error(`Documentation reference leaves the public repository: ${target}`);
  return value;
}

async function publicRealPath(file) {
  const real = await fs.realpath(file);
  return safeRepoPath(root, real);
}

async function readJson(file) { return JSON.parse(await fs.readFile(file, 'utf8')); }
async function exists(file) { try { await fs.access(file); return true; } catch (error) {
  if (error.code === 'ENOENT') return false;
  throw error;
} }

async function browserPath() {
  const candidates = [
    process.env.PHANTOM_DOCS_BROWSER,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
  ].filter(Boolean);
  for (const candidate of candidates) if (await exists(candidate)) return candidate;
  throw new Error('Set PHANTOM_DOCS_BROWSER to an installed Chromium/Edge executable. No browser is downloaded.');
}

async function offlinePage(browser) {
  const page = await browser.newPage();
  await page.setRequestInterception(true);
  page.on('request', request => {
    if (/^(?:file:|data:|about:|blob:)/.test(request.url())) request.continue();
    else request.abort('blockedbyclient');
  });
  return page;
}

async function loadBook() {
  const config = await readJson(path.join(bookRoot, 'book.json'));
  const catalogue = await readJson(path.join(bookRoot, 'catalogue.json'));
  validateCatalogue(catalogue);
  const generatedTargets = new Set(['manifest.json', config.outputs.html, config.outputs.pdf]
    .map(file => safeRepoPath(bookRoot, file)));
  const files = [...config.chapters, ...(config.appendices ?? [])];
  const pages = [];
  const inputs = new Map();
  const record = async file => {
    await publicRealPath(file);
    const bytes = await fs.readFile(file);
    inputs.set(relative(file), hash(bytes));
    return bytes.toString('utf8');
  };
  for (const file of files) {
    const absolute = safeRepoPath(bookRoot, file);
    const markdown = await record(absolute);
    const page = { file: absolute, id: `ch-${slug(path.basename(file, '.md'))}`, markdown, ...frontmatter(markdown) };
    page.citations = citations(markdown);
    if (new Set(page.citations.map(item => item.file)).size < 5)
      throw new Error(`${file}: at least five distinct source files are required.`);
    for (const citation of page.citations) {
      const source = safeRepoPath(root, citation.file);
      const content = await record(source);
      const count = content.split(/\r?\n/).length;
      if (citation.start < 1 || citation.end < citation.start || citation.end > count)
        throw new Error(`${file}: invalid citation ${citation.file}:${citation.start}-${citation.end}, file has ${count} lines.`);
    }
    page.diagrams = [...page.body.matchAll(/^```mermaid[ \t]*\r?\n([\s\S]*?)^```[ \t]*$/gm)]
      .map(match => match[1].trim());
    if (page.diagrams.length < 2) throw new Error(`${file}: at least two Mermaid diagrams are required.`);
    for (const code of page.diagrams) {
      if (/<br\s*\/>/.test(code)) throw new Error(`${file}: Mermaid must not contain <br/>.`);
      if (!/\baccTitle:/.test(code) || !/\baccDescr:/.test(code))
        throw new Error(`${file}: diagrams need accessible titles and descriptions.`);
      if (/%%\{init:/.test(code)) throw new Error(`${file}: use Mermaid frontmatter, not deprecated init directives.`);
      if (/sequenceDiagram/.test(code) && !/\bautonumber\b/.test(code))
        throw new Error(`${file}: sequence diagrams need autonumber.`);
    }
    pages.push(page);
  }
  const markdown = new MarkdownIt({ html: false, linkify: false });
  for (const page of pages) {
    const tokens = markdown.parse(page.body, {});
    for (const token of tokens.flatMap(item => [item, ...(item.children ?? [])])) {
      const target = token.type === 'image' ? token.attrGet('src') :
        token.type === 'link_open' ? token.attrGet('href') : null;
      if (!target || target.startsWith('#')) continue;
      if (/^[a-z]+:/i.test(target)) {
        if (token.type === 'image') throw new Error('Book images must be local, not remote.');
        if (!/^https?:/i.test(target)) throw new Error(`Unsupported public documentation link: ${target}`);
        continue;
      }
      const file = safeRepoPath(path.dirname(page.file), decodeURIComponent(target.split('#')[0]));
      if (!await exists(file)) {
        if (token.type !== 'image' && generatedTargets.has(file)) continue;
        throw new Error(`${relative(page.file)} has a missing link/image: ${target}`);
      }
      await publicRealPath(file);
      if (token.type === 'image') await record(file);
    }
  }
  const screenshotsFile = path.join(bookRoot, 'assets', 'screenshots', 'manifest.json');
  const screenshots = JSON.parse(await record(screenshotsFile));
  if (!screenshots.documentationFixture || screenshots.recordingStarted)
    throw new Error('Screenshots must be documented synthetic, non-recording fixtures.');
  for (const [file, expected] of Object.entries(screenshots.sourceFiles)) {
    const actual = hash(Buffer.from(await record(safeRepoPath(root, file))));
    if (actual !== expected) throw new Error(`UI screenshots are stale for ${file}; run Export-HandbookScreenshots.ps1.`);
  }
  for (const image of screenshots.images) {
    const file = safeRepoPath(path.dirname(screenshotsFile), image.file);
    await publicRealPath(file);
    const bytes = await fs.readFile(file);
    if (hash(bytes) !== image.sha256) throw new Error(`Screenshot hash mismatch: ${image.file}`);
    inputs.set(relative(file), image.sha256);
  }
  const readmeImages = JSON.parse(await record(path.join(root, 'docs', 'assets', 'readme', 'manifest.json')));
  for (const image of readmeImages.images) {
    for (const [file, expected] of [[image.source, image.sourceSha256], [image.file, image.sha256]]) {
      const bytes = await fs.readFile(await publicRealPath(safeRepoPath(root, file)));
      if (hash(bytes) !== expected)
        throw new Error(`README image is stale: ${file}; run Export-ReadmeImages.ps1.`);
      inputs.set(file, expected);
    }
  }
  for (const file of ['book.json', 'catalogue.json', 'assets/cover.svg'])
    await record(path.join(bookRoot, file));
  for (const file of ['build.mjs', 'handbook.css', 'package.json', 'package-lock.json', 'test-docs.mjs', 'README.md'])
    await record(path.join(here, file));
  for (const file of ['AGENTS.md', 'README.md', 'JOURNAL.md', 'docs/README.md',
    'docs/handbook/README.md', 'docs/DOCUMENTATION-MAINTENANCE.md', 'docs/LOCAL-QUESTIONS.md', 'docs/OVERVIEW.md',
    'docs/ANSWER-PROVIDERS.md', 'docs/answer-provider-smoke.json',
    'scripts/Initialize-Answers.ps1', 'scripts/Initialize-Answers.txt',
    'scripts/Build-Handbook.ps1', 'scripts/Build-Handbook.txt',
    'scripts/Export-HandbookScreenshots.ps1', 'scripts/Export-HandbookScreenshots.txt',
    'scripts/Export-ReadmeImages.ps1', 'scripts/Export-ReadmeImages.txt',
    'TODO/38_LIVING_DOCUMENTATION.todo', 'TODO/WORKFLOWS/06_LIVING_DOCUMENTATION.todo'])
    await record(safeRepoPath(root, file));
  return { config, pages, inputs: Object.fromEntries([...inputs].sort(([a], [b]) => a.localeCompare(b))) };
}

async function renderDiagrams(book, browser, force) {
  const page = await offlinePage(browser);
  await page.setViewport({ width: 1600, height: 1200 });
  await page.setContent('<!doctype html><html><body style="margin:0;background:#161b22"></body></html>');
  const mermaidRoot = path.dirname(require.resolve('mermaid/package.json'));
  await page.addScriptTag({ path: path.join(mermaidRoot, 'dist', 'mermaid.min.js') });
  const version = (await readJson(path.join(mermaidRoot, 'package.json'))).version;
  await fs.mkdir(path.join(bookRoot, 'assets', 'diagrams'), { recursive: true });
  const artifacts = [];
  for (const chapter of book.pages) {
    chapter.diagramFiles = [];
    for (const [index, code] of chapter.diagrams.entries()) {
      const stem = `${path.basename(chapter.file, '.md').toLowerCase()}-${String(index + 1).padStart(2, '0')}`;
      const svgFile = path.join(bookRoot, 'assets', 'diagrams', `${stem}.svg`);
      const mmdFile = path.join(bookRoot, 'assets', 'diagrams', `${stem}.mmd`);
      const cacheFile = path.join(bookRoot, 'assets', 'diagrams', `${stem}.json`);
      const fingerprint = hash(JSON.stringify({ code, theme, version }));
      let cached = null;
      if (await exists(cacheFile)) cached = await readJson(cacheFile);
      if (force || cached?.inputSha256 !== fingerprint || !await exists(svgFile) ||
          hash(await fs.readFile(svgFile)) !== cached?.svgSha256) {
        const svg = await page.evaluate(async ({ code, theme, id }) => {
          window.mermaid.initialize({ ...theme, deterministicIDSeed: id });
          return (await window.mermaid.render(id, code)).svg;
        }, { code, theme, id: `diagram-${slug(stem)}` });
        if (!svg.includes('<svg') || /<script\b|https?:\/\/[^"']+\.(?:js|css)/i.test(svg))
          throw new Error(`Invalid/non-local SVG generated for ${stem}`);
        await fs.writeFile(svgFile, svg, 'utf8');
        await fs.writeFile(cacheFile, JSON.stringify({
          schemaVersion: 1, mermaidVersion: version, inputSha256: fingerprint, svgSha256: hash(svg)
        }, null, 2) + '\n');
      }
      await fs.writeFile(mmdFile, code + '\n', 'utf8');
      chapter.diagramFiles.push(`assets/diagrams/${stem}.svg`);
      for (const file of [svgFile, mmdFile, cacheFile])
        artifacts.push({ file: relative(file), sha256: hash(await fs.readFile(file)) });
    }
  }
  await page.close();
  return artifacts;
}

function renderChapter(chapter, allPages) {
  const parser = new MarkdownIt({ html: false, linkify: false, typographer: false });
  const ids = new Map(allPages.map(page => [page.file.toLowerCase(), page.id]));
  const headings = new Map();
  const previousHeading = parser.renderer.rules.heading_open;
  parser.renderer.rules.heading_open = (tokens, index, options, env, renderer) => {
    const text = tokens[index + 1]?.content ?? '';
    const base = `${chapter.id}-${slug(text)}`;
    const count = headings.get(base) ?? 0;
    headings.set(base, count + 1);
    tokens[index].attrSet('id', count ? `${base}-${count}` : base);
    return previousHeading ? previousHeading(tokens, index, options, env, renderer) :
      renderer.renderToken(tokens, index, options);
  };
  const rewrite = value => {
    if (!value || /^[a-z]+:/i.test(value)) return value;
    if (value.startsWith('#')) return `#${chapter.id}-${value.slice(1)}`;
    const [filePart, fragment] = value.split('#');
    const file = safeRepoPath(path.dirname(chapter.file), decodeURIComponent(filePart));
    const included = ids.get(file.toLowerCase());
    if (included) return `#${included}${fragment ? '-' + fragment : ''}`;
    return path.relative(bookRoot, file).replaceAll(path.sep, '/') + (fragment ? '#' + fragment : '');
  };
  parser.renderer.rules.link_open = (tokens, index, options, env, renderer) => {
    tokens[index].attrSet('href', rewrite(tokens[index].attrGet('href')));
    return renderer.renderToken(tokens, index, options);
  };
  const image = parser.renderer.rules.image;
  parser.renderer.rules.image = (tokens, index, options, env, renderer) => {
    tokens[index].attrSet('src', rewrite(tokens[index].attrGet('src')));
    return image(tokens, index, options, env, renderer);
  };
  const fence = parser.renderer.rules.fence;
  let diagram = 0;
  parser.renderer.rules.fence = (tokens, index, options, env, renderer) => {
    const token = tokens[index];
    if (token.info.trim() !== 'mermaid') return fence(tokens, index, options, env, renderer);
    const file = chapter.diagramFiles[diagram++];
    return `<figure class="diagram"><img src="${escape(file)}" alt="${escape(chapter.title)} diagram ${diagram}">` +
      `<figcaption>${escape(chapter.title)} - diagram ${diagram}. Source-linked; status follows the chapter.</figcaption></figure>` +
      `<details class="diagram-source"><summary>Editable Mermaid source</summary><pre><code>${escape(token.content)}</code></pre></details>`;
  };
  return `<section class="chapter" id="${chapter.id}"><div class="chapter-note">Source: ${escape(relative(chapter.file))}</div>` +
    parser.render(chapter.body) + '<p class="top-link"><a href="#contents">Back to contents</a></p></section>';
}

async function makeHtml(book) {
  const css = await fs.readFile(path.join(here, 'handbook.css'), 'utf8');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">` +
    `<title>${escape(book.config.title)}</title><style>${css}</style></head><body><main>` +
    `<header class="cover"><div class="edition">Engineering edition ${escape(book.config.edition)}</div>` +
    `<img src="assets/cover.svg" alt="Original diagrammatic handbook cover">` +
    `<h1>${escape(book.config.title)}</h1><p>${escape(book.config.subtitle)}</p>` +
    '<p>Local capture and explicit provider answers. Measured evidence is bounded. Direct Azure remains future work.</p></header>' +
    '<div class="status">This book distinguishes implemented behavior, measured observations, proposals and unknowns. ' +
    'No universal screen-capture or cursor invisibility is promised. Documentation images use synthetic content.</div>' +
    `<h2 id="contents">Contents</h2><nav class="toc">${book.pages.map(page =>
      `<a href="#${page.id}">${escape(page.title)}</a>`).join('')}</nav>` +
    book.pages.map(page => renderChapter(page, book.pages)).join('\n') +
    '</main></body></html>';
}

async function renderPdf(book, browser, htmlFile) {
  const page = await offlinePage(browser);
  await page.setViewport({ width: 674, height: 1000 });
  await page.goto(pathToFileURL(htmlFile).href, { waitUntil: 'networkidle0' });
  await page.evaluate(() => document.fonts.ready);
  const screenLayouts = [];
  for (const width of [1440, 768, 390]) {
    await page.setViewport({ width, height: 1000 });
    const layout = await page.evaluate(() => ({
      viewport: window.innerWidth, content: document.documentElement.scrollWidth
    }));
    if (layout.content > width + 2) throw new Error(`Reader layout overflow: ${JSON.stringify(layout)}`);
    screenLayouts.push(layout);
  }
  await page.setViewport({ width: 674, height: 1000 });
  await page.emulateMediaType('print');
  const inspection = await page.evaluate(() => ({
    missingImages: [...document.images].filter(image => !image.complete || image.naturalWidth === 0)
      .map(image => image.getAttribute('src')),
    overflowing: [...document.querySelectorAll('pre,table,figure,img')].filter(element =>
      element.getBoundingClientRect().right > document.documentElement.clientWidth + 2 ||
      element.getBoundingClientRect().left < -2).map(element => element.tagName + ':' + element.textContent.slice(0, 60)),
    documentOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
    missingAnchors: [...document.querySelectorAll('a[href^="#"]')].map(link => link.getAttribute('href'))
      .filter(href => !document.getElementById(decodeURIComponent(href.slice(1))))
  }));
  if (inspection.missingImages.length || inspection.overflowing.length || inspection.documentOverflow ||
      inspection.missingAnchors.length)
    throw new Error(`Printable layout failed: ${JSON.stringify(inspection)}`);
  const pdfFile = path.join(bookRoot, book.config.outputs.pdf);
  await page.pdf({
    path: pdfFile, format: 'A4', preferCSSPageSize: true, printBackground: true,
    tagged: true, outline: true, displayHeaderFooter: true, headerTemplate: '<div></div>',
    footerTemplate: '<div style="font:9px Arial;width:100%;text-align:center;color:#536b66">' +
      'Phantom Fudge Engineering Handbook &nbsp; | &nbsp; <span class="pageNumber"></span> / <span class="totalPages"></span></div>'
  });
  const bytes = await fs.readFile(pdfFile);
  const pages = [...bytes.toString('latin1').matchAll(/\/Type\s*\/Page\b/g)].length;
  if (!bytes.subarray(0, 5).equals(Buffer.from('%PDF-')) || pages < book.pages.length)
    throw new Error('PDF did not contain the expected document/page structure.');
  await page.close();
  return { file: relative(pdfFile), sha256: hash(bytes), bytes: bytes.length, pages, inspection, screenLayouts };
}

async function check(book) {
  const manifestFile = path.join(bookRoot, 'manifest.json');
  if (!await exists(manifestFile)) throw new Error('Build the handbook before checking freshness.');
  const manifest = await readJson(manifestFile);
  if (!manifest.pdf) throw new Error('This is an intermediate HTML-only edition; run a full build before checking completion.');
  for (const [file, expected] of Object.entries(manifest.inputs)) {
    if (book.inputs[file] !== expected) throw new Error(`Handbook source changed: ${file}. Review docs/journal, then rebuild.`);
  }
  for (const file of Object.keys(book.inputs))
    if (!(file in manifest.inputs)) throw new Error(`New handbook input not in manifest: ${file}`);
  for (const artifact of manifest.artifacts) {
    const file = safeRepoPath(root, artifact.file);
    if (!await exists(file) || hash(await fs.readFile(file)) !== artifact.sha256)
      throw new Error(`Missing/stale handbook artifact: ${artifact.file}`);
  }
  console.log(JSON.stringify({ status: 'current', chapters: manifest.chapters.length,
    diagrams: manifest.diagramCount, words: manifest.wordCount, pdfPages: manifest.pdf?.pages }, null, 2));
}

export async function main(args = process.argv.slice(2)) {
  const allowed = new Set(['--check', '--accept-reviewed-source', '--force-diagrams', '--no-pdf']);
  if (args.some(arg => !allowed.has(arg))) throw new Error('Unknown handbook build argument.');
  const book = await loadBook();
  if (args.includes('--check')) return check(book);
  const manifestFile = path.join(bookRoot, 'manifest.json');
  if (await exists(manifestFile) && !args.includes('--accept-reviewed-source')) {
    const previous = await readJson(manifestFile);
    const changedCode = Object.entries(book.inputs).filter(([file, value]) =>
      /^(?:src|services|scripts|tests|tools)\//.test(file) && previous.inputs[file] && previous.inputs[file] !== value);
    if (changedCode.length)
      throw new Error('Source changed. Review affected chapters/citations/screenshots and update JOURNAL, then use --accept-reviewed-source.');
  }
  const browser = await puppeteer.launch({
    executablePath: await browserPath(), headless: true, pipe: true,
    args: ['--disable-background-networking', '--disable-component-update', '--disable-sync',
      '--no-first-run', '--disable-default-apps', '--metrics-recording-only']
  });
  try {
    const artifacts = await renderDiagrams(book, browser, args.includes('--force-diagrams'));
    const html = await makeHtml(book);
    const htmlFile = path.join(bookRoot, book.config.outputs.html);
    await fs.writeFile(htmlFile, html, 'utf8');
    artifacts.push({ file: relative(htmlFile), sha256: hash(html) });
    const pdf = args.includes('--no-pdf') ? null : await renderPdf(book, browser, htmlFile);
    if (pdf) artifacts.push({ file: pdf.file, sha256: pdf.sha256 });
    const chapters = book.pages.map(page => ({
      file: relative(page.file), title: page.title, citations: page.citations,
      diagrams: page.diagramFiles.length, words: page.body.replace(/```[\s\S]*?```/g, '').split(/\s+/).filter(Boolean).length
    }));
    const manifest = {
      schemaVersion: 1, generatedAtUtc: new Date().toISOString(), edition: book.config.edition,
      inputs: book.inputs, chapters, diagramCount: chapters.reduce((sum, item) => sum + item.diagrams, 0),
      wordCount: chapters.reduce((sum, item) => sum + item.words, 0), pdf, artifacts
    };
    await fs.writeFile(manifestFile, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
    if (pdf) await check(book);
    else console.log(JSON.stringify({ status: 'intermediate-html-only', chapters: chapters.length,
      diagrams: manifest.diagramCount, words: manifest.wordCount }, null, 2));
  } finally {
    await browser.close();
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
