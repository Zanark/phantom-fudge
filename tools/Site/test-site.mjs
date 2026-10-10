import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, readdir, rm, stat, truncate, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { activeIndex, clampSeek, formatTime, pageSlice, searchCues } from '../../site/player-core.mjs';
import { tutorial } from '../../site/assets/tutorial-data.mjs';
import { buildSite, MAX_FILE_BYTES } from './build.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
test('time formatting and seek bounds', () => {
  assert.equal(formatTime(2297.767), '38:17');
  assert.equal(formatTime(3601), '1:00:01');
  assert.equal(formatTime(-1), '0:00');
  assert.equal(clampSeek(-10, 2297.767), 0);
  assert.ok(Math.abs(clampSeek(9000, 2297.767) - 2297.717) < 1e-9);
  assert.throws(() => clampSeek(NaN, 20), RangeError);
  assert.throws(() => clampSeek(20, 0), RangeError);
});
test('all fifteen exact chapter seeks tolerate measured media rounding', () => {
  assert.equal(tutorial.chapters.length, 15);
  tutorial.chapters.forEach((chapter, index) => {
    assert.equal(activeIndex(tutorial.chapters, chapter.start), index);
    assert.equal(activeIndex(tutorial.chapters, chapter.start - .000002), index);
    if (index) assert.equal(activeIndex(tutorial.chapters, chapter.start - .01), index - 1);
    if (index) assert.equal(chapter.start, tutorial.chapters[index - 1].end);
  });
});
test('all 525 captions retain content and ordered nonoverlapping timing', () => {
  assert.equal(tutorial.cues.length, 525);
  tutorial.cues.forEach((cue, index) => {
    assert.ok(cue.text.trim());
    assert.ok(cue.end > cue.start);
    assert.ok(cue.end <= tutorial.duration);
    assert.equal(activeIndex(tutorial.cues, (cue.start + cue.end) / 2), index);
    if (index) assert.ok(cue.start >= tutorial.cues[index - 1].end);
  });
  assert.equal(activeIndex(tutorial.cues, 0), -1);
});
test('search is literal, case insensitive and resets to all cues when cleared', () => {
  assert.deepEqual(searchCues(tutorial.cues, ' PREVIOUS ANSWER '), searchCues(tutorial.cues, 'previous answer'));
  assert.ok(searchCues(tutorial.cues, 'previous answer').length > 0);
  assert.equal(searchCues(tutorial.cues, '[<>.*] no match').length, 0);
  assert.equal(searchCues(tutorial.cues, '   ').length, 525);
  const pages = [0, 1, 2].flatMap(page => pageSlice(tutorial.chapters, page, 5));
  assert.deepEqual(pages, tutorial.chapters);
  assert.equal(pageSlice(tutorial.cues, 87, 6).length, 3);
});
test('site contains required controls, graceful native video and no external scripts', async () => {
  const html = await readFile(path.join(root, 'site', 'index.html'), 'utf8');
  for (const id of ['video', 'play', 'back', 'forward', 'time', 'speed', 'captions', 'search', 'chapters', 'transcript', 'motion']) {
    assert.ok(html.includes(`id="${id}"`), id);
  }
  assert.match(html, /<video[^>]+controls[^>]+preload="metadata"/);
  assert.doesNotMatch(html, /\bautoplay\b|<script[^>]+src="https?:/);
  assert.match(html, /Previous-answer context is not in the published/);
  assert.match(html, /not universal invisibility/);
});
test('public website inputs are public-safe and every file fits regular Git storage', async () => {
  async function inspect(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name);
      assert.ok(!entry.isSymbolicLink(), `Symlink not allowed: ${file}`);
      if (entry.isDirectory()) await inspect(file);
      else if (/\.(?:mjs|html|css|json|txt|svg|vtt|srt)$/.test(entry.name)) {
        const text = await readFile(file, 'utf8');
        assert.doesNotMatch(text, /C:\\+(?:Users|devdesk)|\.agent-context|sourceRoot|api[_-]?key\s*[:=]/i, file);
      }
      if (entry.isFile()) assert.ok((await stat(file)).size < MAX_FILE_BYTES, file);
    }
  }
  await inspect(path.join(root, 'site'));
});

test('all eight demos have local motion, still and video assets and README links', async () => {
  const html = await readFile(path.join(root, 'site', 'index.html'), 'utf8');
  const readme = await readFile(path.join(root, 'README.md'), 'utf8');
  const demos = [...html.matchAll(/data-motion="(assets\/demo-([a-z-]+)\.gif)"/g)];
  assert.equal(demos.length, 8);
  for (const [, file, name] of demos) {
    assert.ok(readme.includes(`site/${file}`), `Missing README GIF: ${name}`);
    assert.ok(html.includes(`id="tab-${name}" role="tab" aria-controls="demo-${name}"`));
    assert.ok(html.includes(`id="demo-${name}"`));
    for (const extension of ['gif', 'jpg', 'mp4']) {
      assert.ok((await stat(path.join(root, 'site', 'assets', `demo-${name}.${extension}`))).size > 0);
    }
  }
  for (const [, id] of html.matchAll(/data-jump="([^"]+)"/g)) {
    assert.ok(tutorial.chapters.some(chapter => chapter.id === id), `Unknown chapter: ${id}`);
  }
});

test('Pages workflow builds bundled media without a release download, LFS or custom secrets', async () => {
  const workflow = await readFile(path.join(root, '.github', 'workflows', 'pages.yml'), 'utf8');
  const media = JSON.parse(await readFile(path.join(root, 'site', 'media.json'), 'utf8'));
  const provenance = JSON.parse(await readFile(path.join(root, 'site', 'assets', 'media-manifest.json'), 'utf8'));
  assert.match(workflow, /run: node tools\/Site\/build\.mjs\s*\n/);
  assert.doesNotMatch(workflow, /gh release|--video|GH_TOKEN|secrets\.|lfs:\s*true/);
  assert.equal(media.release, undefined);
  assert.equal((await stat(path.join(root, 'site', 'media', media.file))).size, media.bytes);
  assert.ok(media.bytes < MAX_FILE_BYTES);
  assert.equal(media.original.bytes, 159267662);
  assert.equal(media.original.sha256, '388ee914e1ffdc0ac3911d2a2e8dc6c8f33d8aad047f1aeff02fbe41138e1fde');
  assert.ok(Math.abs(media.durationSeconds - tutorial.duration) < .05);
  assert.deepEqual(provenance.video, media);
});

test('self-contained assembly needs no private files and rejects damaged input before replacing output', async t => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'phantom-site-build-'));
  const source = path.join(directory, 'site');
  const output = path.join(directory, 'output');
  const video = Buffer.from('synthetic local media fixture');
  const identity = { file: 'tutorial.mp4', bytes: video.length, sha256: createHash('sha256').update(video).digest('hex') };
  const metadata = path.join(source, 'media.json');
  const mediaPath = path.join(source, 'media', identity.file);
  try {
    await mkdir(path.dirname(mediaPath), { recursive: true });
    await writeFile(mediaPath, video);
    await writeFile(metadata, JSON.stringify(identity));
    await writeFile(path.join(source, 'index.html'), '<h1>Local fixture</h1>');
    await writeFile(path.join(directory, 'private.txt'), 'must never be copied');
    await buildSite(source, output);
    assert.deepEqual((await readdir(output)).sort(), ['index.html', 'media', 'media.json']);
    assert.deepEqual(await readFile(path.join(output, 'media', identity.file)), video);
    await writeFile(path.join(output, 'preserve.txt'), 'previous preview');
    await t.test('missing media fails explicitly', async () => {
      await rm(mediaPath);
      await assert.rejects(buildSite(source, output), { code: 'ENOENT' });
      await writeFile(mediaPath, video);
    });
    await t.test('wrong byte count fails', async () => {
      await writeFile(metadata, JSON.stringify({ ...identity, bytes: identity.bytes + 1 }));
      await assert.rejects(buildSite(source, output), /byte count/);
    });
    await t.test('same-size corruption fails hash validation', async () => {
      await writeFile(metadata, JSON.stringify(identity));
      await writeFile(mediaPath, Buffer.alloc(video.length));
      await assert.rejects(buildSite(source, output), /SHA256/);
      await writeFile(mediaPath, video);
    });
    await t.test('media path traversal fails', async () => {
      await writeFile(metadata, JSON.stringify({ ...identity, file: '../private.mp4' }));
      await assert.rejects(buildSite(source, output), /Invalid bundled tutorial identity/);
      await writeFile(metadata, JSON.stringify(identity));
    });
    await t.test('oversized files fail before copying', async () => {
      await truncate(mediaPath, MAX_FILE_BYTES);
      await assert.rejects(buildSite(source, output), /below 100 MiB/);
    });
    assert.equal(await readFile(path.join(output, 'preserve.txt'), 'utf8'), 'previous preview');
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
