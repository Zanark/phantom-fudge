import assert from 'node:assert/strict';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { citations, frontmatter, safeRepoPath, slug, validateCatalogue } from './build.mjs';
import { hash } from './build.mjs';
import { physicalLines, sourceKind, validateFileAnnotation, validateAtlasManifest, escapeHtml, discoverSources, atlasChapterLink } from './source-atlas.mjs';

test('frontmatter is required and stripped', () => {
  const page = frontmatter('---\ntitle: "Title"\ndescription: "Description"\n---\n# Body');
  assert.equal(page.title, 'Title');
  assert.equal(page.body, '# Body');
  assert.throws(() => frontmatter('# missing'));
});
test('source citations support Windows paths and line ranges', () => {
  assert.deepEqual(citations('(`src\\App\\Main.cs:12-19`), README.md:4'), [
    { file: 'src/App/Main.cs', start: 12, end: 19 },
    { file: 'README.md', start: 4, end: 4 }
  ]);
});
test('URLs and numeric examples are not source citations', () => {
  assert.deepEqual(citations('http://127.0.0.1:8765 and latency:50'), []);
});
test('every comma-separated citation range is validated', () => {
  assert.deepEqual(citations('src/App.cs:1-4, 10,20-30'), [
    { file: 'src/App.cs', start: 1, end: 4 },
    { file: 'src/App.cs', start: 10, end: 10 },
    { file: 'src/App.cs', start: 20, end: 30 }
  ]);
});
test('root solution and spike citations are not omitted from provenance', () => {
  assert.deepEqual(citations('PhantomFudge.slnx:1-13; spikes/CaptureProof/CaptureProof.csproj:9-12'), [
    { file: 'PhantomFudge.slnx', start: 1, end: 13 },
    { file: 'spikes/CaptureProof/CaptureProof.csproj', start: 9, end: 12 }
  ]);
});
test('catalogue requires onboarding and real source prompts', () => {
  const good = { items: [{ title: 'onboarding', name: 'Onboarding', prompt: 'Read README.md:1.',
    collapsed: false, children: [] }] };
  validateCatalogue(good);
  assert.throws(() => validateCatalogue({ items: [{ ...good.items[0], prompt: 'Guess the architecture' }] }));
  assert.throws(() => validateCatalogue({ items: [{ ...good.items[0], collapsed: true }] }));
});
test('catalogue rejects excessive children', () => {
  const children = Array.from({ length: 9 }, (_, index) => ({
    title: `child-${index}`, name: 'Child', prompt: 'README.md:1', children: []
  }));
  assert.throws(() => validateCatalogue({ items: [{
    title: 'onboarding', name: 'Onboarding', prompt: 'README.md:1', collapsed: false, children
  }] }));
});
test('stable slugs avoid punctuation collisions across page prefixes', () => {
  assert.equal(slug('01. A C# + Python System'), '01-a-c-python-system');
});
test('BOM and CRLF frontmatter preserve the exact body', () => {
  const page = frontmatter('\uFEFF---\r\ntitle: "Title"\r\ndescription: "Description"\r\n---\r\n# Body');
  assert.equal(page.body, '# Body');
});
test('public paths reject private stores, secrets and traversal', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
  for (const target of ['../outside.md', '.git/config', '.local/token', '.env',
    '.env.production', '.agent-context/STATE.md', 'tools/Docs/node_modules/private.md'])
    assert.throws(() => safeRepoPath(root, target));
  assert.equal(safeRepoPath(root, 'docs/README.md'), path.join(root, 'docs', 'README.md'));
});

const annotatedFile = () => {
  const bytes = Buffer.from('using System;\r\n\r\nreturn 1;\r\n');
  return { bytes, file: { path: 'src/Example.cs', sourceSha256: hash(bytes),
    role: 'A small deterministic fixture for annotation validation.', entryPoints: ['Example'],
    blocks: [{ start: 1, end: 3, title: 'Return a fixed value',
      explanation: 'The fixture imports System, preserves a blank line and returns one; every physical line must have an explanation owner.',
      focus: [{ start: 3, end: 3 }] }] } };
};

test('atlas physical lines agree with trailing-newline and BOM handling', () => {
  assert.deepEqual(physicalLines(''), []);
  assert.deepEqual(physicalLines('\uFEFFa\r\n\r\nb\r\n'), ['a', '', 'b']);
  assert.deepEqual(physicalLines('a\rb\n'), ['a', 'b']);
  assert.deepEqual(physicalLines('a\n\n'), ['a', '']);
});
test('atlas scope distinguishes annotated code, mirrors and generated data', () => {
  assert.equal(sourceKind('src/App/Main.cs'), 'annotated');
  assert.equal(sourceKind('tools/Docs/source-atlas.mjs'), 'annotated');
  assert.equal(sourceKind('tools/Installer/PhantomFudge.iss'), 'annotated');
  assert.equal(sourceKind('src/PhantomFudge.Launcher/app.manifest'), 'annotated');
  assert.equal(sourceKind('services/transcription/requirements.txt'), 'annotated');
  assert.equal(sourceKind('scripts/Run.txt'), 'possible-mirror');
  assert.equal(sourceKind('tools/Probe/results/run.json'), 'evidence');
  assert.equal(sourceKind('src/App/packages.lock.json'), 'lockfile');
  for (const file of ['.local/token.json', 'tools/Docs/node_modules/index.js',
    'src/App/bin/generated.cs', 'src/App/obj/AssemblyInfo.cs', 'docs/source-atlas/manifest.json',
    'tools/.agent-context/STATE.md'])
    assert.equal(sourceKind(file), null);
});
test('atlas includes authored website files without treating the assets directory as generated', () => {
  for (const file of ['site/index.html', 'site/styles.css', 'site/app.mjs', 'site/player-core.mjs',
    'site/media.json', 'site/404.html', 'site/assets/new-widget.mjs', '.github/workflows/pages.yml'])
    assert.equal(sourceKind(file), 'annotated');
  for (const file of ['site/.local/private.json', 'site/node_modules/package/index.js',
    'site/.agent-context/STATE.md', 'site/media/tutorial.mp4', 'site/assets/demo.gif'])
    assert.equal(sourceKind(file), null);
});
test('atlas records named tutorial exports as generated, not authored source or script mirrors', () => {
  for (const file of ['site/assets/tutorial-data.mjs', 'site/assets/transcript.json',
    'site/assets/chapters.json', 'site/assets/media-manifest.json', 'site/assets/asset-provenance.json',
    'site/assets/captions.srt', 'site/assets/captions.vtt', 'site/assets/chapters.txt'])
    assert.equal(sourceKind(file), 'generated');
  assert.equal(sourceKind('site/assets/other-config.json'), 'annotated');
});
test('atlas accepts a complete authored line partition with an exact source hash', () => {
  const { bytes, file } = annotatedFile();
  const result = validateFileAnnotation(file, bytes);
  assert.equal(result.lines.length, 3);
  assert.equal(result.nonblankLines, 2);
  const empty = Buffer.alloc(0);
  assert.equal(validateFileAnnotation({ ...file, sourceSha256: hash(empty), blocks: [] }, empty).lines.length, 0);
});
test('atlas rejects stale source, missing tails, gaps and overlaps', () => {
  const { bytes, file } = annotatedFile();
  assert.throws(() => validateFileAnnotation(file, Buffer.from('changed')), /stale/);
  for (const [start, end] of [[2, 3], [1, 2], [1, 4], [0, 3], [1, 0]])
    assert.throws(() => validateFileAnnotation({ ...file, blocks: [{ ...file.blocks[0], start, end }] }, bytes));
  assert.throws(() => validateFileAnnotation({ ...file, blocks: [file.blocks[0], file.blocks[0]] }, bytes));
});
test('atlas rejects unbounded or invalid focus ranges and placeholder explanations', () => {
  const { bytes, file } = annotatedFile();
  const block = file.blocks[0];
  for (const focus of [[{ start: 0, end: 1 }], [{ start: 2, end: 4 }], [{ start: 2, end: 1 }]])
    assert.throws(() => validateFileAnnotation({ ...file, blocks: [{ ...block, focus }] }, bytes));
  assert.throws(() => validateFileAnnotation({ ...file, blocks: [{ ...block, explanation: 'Does stuff.' }] }, bytes));
  assert.throws(() => validateFileAnnotation({ ...file, blocks: [{ ...block, unexplained: true }] }, bytes));
});
test('atlas rejects traversal and escapes source as text, not executable HTML', () => {
  const { bytes, file } = annotatedFile();
  assert.throws(() => validateFileAnnotation({ ...file, path: '../outside.cs' }, bytes));
  assert.equal(escapeHtml('<script>"&\'</script>'), '&lt;script&gt;&quot;&amp;&#39;&lt;/script&gt;');
});
test('atlas discovery includes its own code and verifies canonical script mirrors', async () => {
  const inventory = await discoverSources();
  assert.ok(inventory.some(file => file.path === 'tools/Docs/source-atlas.mjs' && file.kind === 'annotated'));
  assert.ok(inventory.some(file => file.path === 'scripts/Build-Handbook.txt' &&
    file.kind === 'mirror' && file.canonical === 'scripts/Build-Handbook.ps1'));
  for (const file of ['index.html', 'styles.css', 'app.mjs', 'player-core.mjs', 'media.json', '404.html'])
    assert.ok(inventory.some(entry => entry.path === `site/${file}` && entry.kind === 'annotated'));
  for (const file of ['tutorial-data.mjs', 'transcript.json', 'chapters.json', 'media-manifest.json',
    'asset-provenance.json', 'captions.srt', 'captions.vtt', 'chapters.txt'])
    assert.ok(inventory.some(entry => entry.path === `site/assets/${file}` && entry.kind === 'generated'));
  assert.ok(inventory.every(file => !file.path.includes('node_modules/')));
});
test('atlas completion requires all real outputs and matching coverage counts', () => {
  const counts = { files: 2, lines: 3, nonblankLines: 2, blocks: 2, groups: 1 };
  const artifacts = ['index.html', 'Phantom-Fudge-Source-Atlas.pdf', 'coverage.json', 'README.md']
    .map(name => ({ file: `docs/source-atlas/${name}`, sha256: 'a'.repeat(64) }));
  const manifest = { schemaVersion: 1, complete: true, counts, artifacts,
    pdf: { ...artifacts[1], pages: 3 } };
  validateAtlasManifest(manifest, counts);
  assert.throws(() => validateAtlasManifest({ ...manifest, complete: false }, counts));
  assert.throws(() => validateAtlasManifest({ ...manifest, artifacts: [] }, counts));
  assert.throws(() => validateAtlasManifest({ ...manifest, pdf: { ...manifest.pdf, pages: 1 } }, counts));
  assert.throws(() => validateAtlasManifest({ ...manifest, counts: { ...counts, files: 99 } }, counts));
});

test('atlas links root documentation chapters without inventing a handbook path', () => {
  assert.equal(atlasChapterLink('docs/INSTALLATION.md'), '../INSTALLATION.md');
  assert.equal(atlasChapterLink('docs/handbook/16-distribution-and-troubleshooting.md'), '../handbook/16-distribution-and-troubleshooting.md');
  for (const chapter of [null, '../outside.md', 'docs/../README.md', 'docs/.local/secret.md',
    'docs/handbook/../../README.md', 'https://example.com/page.md', 'docs/page.html'])
    assert.throws(() => atlasChapterLink(chapter));
});
