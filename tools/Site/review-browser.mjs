import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { tutorial } from '../../site/assets/tutorial-data.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const media = JSON.parse(await readFile(path.join(root, 'site', 'media.json'), 'utf8'));
const require = createRequire(path.join(root, 'tools', 'Docs', 'package.json'));
const { default: puppeteer } = await import(pathToFileURL(require.resolve('puppeteer-core')).href);
const url = process.argv[2] || 'http://127.0.0.1:4173/phantom-fudge/';
const output = path.join(root, '.local', 'site-review');
const profile = await mkdtemp(path.join(os.tmpdir(), 'phantom-site-review-'));
const errors = [];
const checks = [];
const failed = [];
const external = [];
let browser;
await mkdir(output, { recursive: true });
try {
  browser = await puppeteer.launch({
    executablePath: process.env.EDGE_PATH || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    headless: true, userDataDir: profile, args: ['--mute-audio', '--no-first-run']
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 1000, deviceScaleFactor: 1 });
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('response', response => { if (response.status() >= 400) failed.push(`${response.status()} ${response.url()}`); });
  page.on('request', request => {
    const requested = new URL(request.url());
    if (['http:', 'https:'].includes(requested.protocol) && requested.origin !== new URL(url).origin) external.push(request.url());
  });
  await page.goto(url, { waitUntil: 'networkidle2', timeout: 60000 });
  await page.waitForSelector('.chapter');
  await page.waitForFunction(() => [...document.querySelectorAll('.hero img')].every(image => image.complete && image.naturalWidth > 0));
  assert.equal(await page.$eval('#video', video => video.paused), true);
  const visibleWords = await page.$eval('body', body => body.innerText.trim().split(/\s+/).length);
  assert.ok(visibleWords <= 350, `Default page has too much visible copy: ${visibleWords} words`);
  await page.screenshot({ path: path.join(output, 'desktop.png') });
  await page.click('#motion');
  assert.equal(await page.$eval('#motion', button => button.getAttribute('aria-pressed')), 'true');
  assert.ok(await page.$$eval('[data-motion]', images => images.every(image => image.src.endsWith('.jpg'))));
  const pausedTransform = await page.$eval('.hero-ghost', image => getComputedStyle(image).transform);
  await new Promise(resolve => setTimeout(resolve, 250));
  assert.equal(await page.$eval('.hero-ghost', image => getComputedStyle(image).transform), pausedTransform);
  checks.push('Ghost motion and all GIFs pause together');

  await page.waitForFunction(() => document.querySelector('#video').readyState >= 1);
  const metadata = await page.$eval('#video', video => ({ duration: video.duration, width: video.videoWidth, height: video.videoHeight }));
  assert.ok(Math.abs(metadata.duration - tutorial.duration) < .1);
  assert.equal(metadata.width, media.width);
  assert.equal(metadata.height, media.height);
  await page.$eval('#video', video => { video.muted = true; });
  await page.click('#play');
  await page.waitForFunction(() => document.querySelector('#video').currentTime > .2);
  await page.click('#play');
  assert.equal(await page.$eval('#video', video => video.paused), true);
  await page.select('#speed', '1.5');
  assert.equal(await page.$eval('#video', video => video.playbackRate), 1.5);
  await page.click('#forward');
  await page.waitForFunction(() => !document.querySelector('#video').seeking);
  const forward = await page.$eval('#video', video => video.currentTime);
  assert.ok(forward > 10 && forward < 12);
  await page.click('#back');
  await page.waitForFunction(() => !document.querySelector('#video').seeking);
  assert.ok(await page.$eval('#video', video => video.currentTime < 2));
  checks.push('Native metadata, no autoplay, muted play/pause, speed and +/-10s');

  for (const [index, chapter] of tutorial.chapters.entries()) {
    while (await page.$eval(`.chapter:nth-child(${index + 1})`, button => button.hidden)) await page.click('#chapter-next');
    await page.click(`.chapter:nth-child(${index + 1})`);
    await page.waitForFunction(({ index, start }) => {
      const video = document.querySelector('#video');
      return !video.seeking && video.readyState >= 2 && Math.abs(video.currentTime - start) < .08 &&
        document.querySelectorAll('.chapter')[index].getAttribute('aria-current') === 'true';
    }, {}, { index, start: chapter.start });
  }
  checks.push('All 15 chapter seeks and active highlights through paginated navigation');
  assert.equal(await page.$eval('.transcript-panel details', details => details.open), false);
  assert.equal(await page.$eval('.scope-panel details', details => details.open), false);
  await page.click('#transcript-title');
  await page.type('#search', 'previous answer');
  assert.ok(await page.$$eval('.cue', cues => cues.length > 0 && cues.every(cue => cue.textContent.toLowerCase().includes('previous answer'))));
  const cue = await page.$eval('.cue', button => ({ time: Number(button.dataset.start), text: button.lastChild.textContent }));
  await page.click('.cue');
  await page.waitForFunction(time => !document.querySelector('#video').seeking && Math.abs(document.querySelector('#video').currentTime - time) < .08, {}, cue.time);
  await page.click('#captions');
  assert.equal(await page.$eval('#captions', button => button.getAttribute('aria-pressed')), 'true');
  await page.waitForFunction(() => !document.querySelector('#caption').hidden);
  assert.equal(await page.$eval('#caption', caption => caption.textContent), cue.text);
  await page.$eval('#search', input => { input.value = 'no-matching-phantom-xyz'; input.dispatchEvent(new Event('input')); });
  assert.equal(await page.$eval('#transcript-page', node => node.textContent), '0 results');
  assert.equal(await page.$eval('#transcript-next', button => button.disabled), true);
  await page.$eval('#search', input => { input.value = ''; input.dispatchEvent(new Event('input')); });
  assert.equal(await page.$eval('#transcript-page', node => node.textContent), '1-6 of 525 results');
  await page.click('#transcript-next');
  assert.equal(await page.$eval('#transcript-page', node => node.textContent), '7-12 of 525 results');
  await page.click('#transcript-prev');
  checks.push('Search, empty state, pagination, transcript seek and visible extra captions');

  await page.click('#captions');
  await page.click('#transcript-title');
  await page.$eval('#tutorial', node => node.scrollIntoView({ behavior: 'instant' }));
  await page.screenshot({ path: path.join(output, 'player.png') });
  await page.$eval('#showcase', node => node.scrollIntoView({ behavior: 'instant' }));
  const demoNames = await page.$$eval('[role="tab"]', tabs => tabs.map(tab => tab.id.slice(4)));
  assert.equal(demoNames.length, 8);
  for (const name of demoNames) {
    await page.click(`#tab-${name}`);
    await page.waitForFunction(name => {
      const image = document.querySelector(`#demo-${name} img`);
      return image.complete && image.naturalWidth > 0;
    }, {}, name);
    assert.equal(await page.$eval(`#demo-${name}`, panel => panel.hidden), false);
  }
  await page.focus(`#tab-${demoNames.at(-1)}`);
  await page.keyboard.press('Home');
  assert.equal(await page.$eval('#tab-selection', tab => tab.getAttribute('aria-selected')), 'true');
  await page.keyboard.press('ArrowLeft');
  assert.equal(await page.$eval(`#tab-${demoNames.at(-1)}`, tab => tab.getAttribute('aria-selected')), 'true');
  await page.keyboard.press('ArrowRight');
  assert.equal(await page.$eval('#tab-selection', tab => tab.getAttribute('aria-selected')), 'true');
  await page.keyboard.press('End');
  assert.equal(await page.$eval(`#tab-${demoNames.at(-1)}`, tab => tab.getAttribute('aria-selected')), 'true');
  await page.click('#motion');
  for (const name of demoNames) {
    await page.click(`#tab-${name}`);
    await page.waitForFunction(name => {
      const image = document.querySelector(`#demo-${name} img`);
      return image.complete && image.naturalWidth === 1000 && image.src.endsWith('.gif');
    }, {}, name);
    const chapterId = await page.$eval(`#demo-${name} a`, link => link.dataset.jump);
    const start = tutorial.chapters.find(chapter => chapter.id === chapterId).start;
    await page.click(`#demo-${name} a`);
    await page.waitForFunction(start => {
      const video = document.querySelector('#video');
      return !video.seeking && Math.abs(video.currentTime - start) < .08;
    }, {}, start);
    assert.equal(await page.$eval('#video', video => video.paused), true);
  }
  await page.click('#motion');
  await page.click('#tab-selection');
  await page.$eval('#showcase', node => node.scrollIntoView({ behavior: 'instant' }));
  checks.push('All eight still/GIF demos, chapter links, Home/End and arrow-key wrapping without autoplay');
  await page.screenshot({ path: path.join(output, 'showcase.png') });
  for (const width of [1440, 1024, 768, 390, 320]) {
    await page.setViewport({ width, height: 960, deviceScaleFactor: 1 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow at ${width}`);
    assert.ok(await page.$$eval('.chapters,.transcript', nodes => nodes.filter(node => node.checkVisibility()).every(node => node.scrollHeight <= node.clientHeight + 1)), `Nested scroll at ${width}`);
  }
  await page.setViewport({ width: 390, height: 1000, deviceScaleFactor: 1 });
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
  await page.screenshot({ path: path.join(output, 'mobile.png') });
  await page.$eval('#tutorial', node => node.scrollIntoView({ behavior: 'instant' }));
  await page.screenshot({ path: path.join(output, 'mobile-player.png') });
  await page.$eval('#showcase', node => node.scrollIntoView({ behavior: 'instant' }));
  await page.screenshot({ path: path.join(output, 'mobile-showcase.png') });
  checks.push('320-1440px layouts without width overflow or inner transcript/chapter scrolling');

  await page.emulateMediaFeatures([{ name: 'prefers-reduced-motion', value: 'reduce' }]);
  await page.reload({ waitUntil: 'networkidle2' });
  await page.waitForSelector('.chapter');
  assert.ok(await page.$eval('body', body => body.classList.contains('motion-paused')));
  assert.equal(await page.$eval('.hero-ghost', image => getComputedStyle(image).animationName), 'none');
  assert.ok(await page.$$eval('[data-motion]', images => images.every(image => image.src.endsWith('.jpg'))));
  await page.click('#motion');
  assert.ok(await page.$$eval('[data-motion]', images => images.every(image => image.src.endsWith('.gif'))));
  assert.notEqual(await page.$eval('.hero-ghost', image => getComputedStyle(image).animationName), 'none');
  checks.push('Reduced motion defaults to static art; explicit resume re-enables motion');
  await page.setJavaScriptEnabled(false);
  await page.reload({ waitUntil: 'networkidle2' });
  assert.equal(await page.$eval('#video', video => video.controls), true);
  assert.ok(await page.$eval('noscript', node => node.textContent.includes('Chapter')));
  checks.push('No-JavaScript native video and chapter-list fallback');
  const missingMedia = await browser.newPage();
  const mediaErrors = [];
  missingMedia.on('pageerror', error => mediaErrors.push(error.message));
  await missingMedia.setRequestInterception(true);
  missingMedia.on('request', request => {
    if (new URL(request.url()).pathname.endsWith(`/${media.file}`)) request.abort('failed');
    else request.continue();
  });
  await missingMedia.goto(url, { waitUntil: 'networkidle2', timeout: 60000 });
  await missingMedia.$eval('#video', video => video.load());
  await missingMedia.waitForFunction(() => !document.querySelector('#status').hidden);
  assert.match(await missingMedia.$eval('#status', node => node.textContent), /could not be loaded/);
  assert.equal(await missingMedia.$eval('#search', node => node.disabled), false);
  assert.deepEqual(mediaErrors, []);
  await missingMedia.close();
  checks.push('Missing-video failure is visible while transcript navigation remains available');
  assert.deepEqual(errors, []);
  assert.deepEqual(failed, []);
  assert.deepEqual(external, []);
  await writeFile(path.join(output, 'browser.json'), JSON.stringify({ url, checks, errors, failed, external, metadata, visibleWords, muted: true }, null, 2));
  console.log(JSON.stringify({ checks, errors, failed, external }, null, 2));
} finally {
  if (browser) await browser.close();
  await rm(profile, { recursive: true, force: true });
}
