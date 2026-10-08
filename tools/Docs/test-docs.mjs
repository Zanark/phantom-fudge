import assert from 'node:assert/strict';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { citations, frontmatter, safeRepoPath, slug, validateCatalogue } from './build.mjs';

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
