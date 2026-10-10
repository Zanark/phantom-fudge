/**
 * Original DeepSeaFoam website artwork. No external images or font downloads.
 * Requires the existing tools/Docs Puppeteer dependency and an installed browser.
 * Usage: node tools\Site\make-artwork.mjs [--browser "path-to-browser"]
 */
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs/promises';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const assets = path.join(root, 'site', 'assets');
const work = path.join(root, '.local', 'site-art');
const require = createRequire(path.join(root, 'tools', 'Docs', 'package.json'));
const puppeteer = require('puppeteer-core');
const browserIndex = process.argv.indexOf('--browser');
const executablePath = browserIndex >= 0 ? process.argv[browserIndex + 1]
  : process.env.SITE_BROWSER ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

const colors = {
  base: '#000F13', panel: '#001E26', accent: '#00A591', text: '#93A1A1',
  faint: '#839496', warm: '#EEE8D5', edge: '#FDF6E3', border: '#586E75',
};
const font = "'Segoe UI Variable Display', 'Segoe UI', Arial, sans-serif";
const defs = `
  <linearGradient id="body" x1=".2" y1="0" x2=".7" y2="1">
    <stop stop-color="${colors.edge}"/><stop offset=".67" stop-color="${colors.warm}"/>
    <stop offset="1" stop-color="${colors.accent}"/>
  </linearGradient>
  <radialGradient id="aura"><stop stop-color="${colors.accent}" stop-opacity=".27"/>
    <stop offset="1" stop-color="${colors.accent}" stop-opacity="0"/></radialGradient>
  <linearGradient id="mist" x1="0" x2="1"><stop stop-color="${colors.accent}" stop-opacity="0"/>
    <stop offset=".6" stop-color="${colors.accent}" stop-opacity=".19"/>
    <stop offset="1" stop-color="${colors.warm}" stop-opacity="0"/></linearGradient>
  <linearGradient id="shade" x1="0" x2="1" y1="0" y2="1">
    <stop stop-color="${colors.panel}"/><stop offset="1" stop-color="${colors.base}"/>
  </linearGradient>`;

// An asymmetric phantom with a folded-paper tail, a little notebook and a cursor.
const phantom = `
  <ellipse cx="328" cy="386" rx="293" ry="310" fill="url(#aura)"/>
  <path d="M80 522C39 414 76 179 278 97C427 36 551 126 538 274"
    fill="none" stroke="${colors.accent}" stroke-width="1.5" opacity=".35" stroke-dasharray="2 12"/>
  <path d="M165 348C166 250 147 175 212 118C261 73 351 80 406 134
    C472 199 447 291 470 357C490 418 544 455 556 500
    C527 496 508 481 494 468C505 533 555 555 593 551
    C566 600 509 614 459 589C442 580 431 568 417 551
    C413 589 393 616 365 636C342 614 326 588 317 559
    C291 600 257 624 217 617C228 589 230 566 222 544
    C191 572 151 581 108 565C169 525 160 436 165 348Z"
    fill="url(#body)"/>
  <path d="M181 347C185 270 166 198 217 145C248 111 296 102 335 114"
    fill="none" stroke="${colors.edge}" stroke-width="6" opacity=".67" stroke-linecap="round"/>
  <path d="M466 485C467 531 496 563 535 568M234 533C255 508 262 481 259 453"
    fill="none" stroke="${colors.base}" stroke-opacity=".1" stroke-width="7" stroke-linecap="round"/>
  <g fill="${colors.base}">
    <ellipse cx="264" cy="262" rx="18" ry="30" transform="rotate(-12 264 262)"/>
    <ellipse cx="350" cy="248" rx="18" ry="30" transform="rotate(-12 350 248)"/>
  </g>
  <g fill="${colors.edge}">
    <ellipse cx="269" cy="254" rx="4.5" ry="8"/>
    <ellipse cx="355" cy="240" rx="4.5" ry="8"/>
  </g>
  <path d="M297 309Q314 321 331 303" fill="none" stroke="${colors.base}"
    stroke-width="5" stroke-linecap="round"/>
  <g transform="translate(302 397) rotate(-11)">
    <rect x="-7" y="3" width="157" height="186" rx="15" fill="${colors.base}" opacity=".16"/>
    <rect width="148" height="180" rx="12" fill="${colors.panel}" stroke="${colors.accent}" stroke-width="3"/>
    <path d="M27 0V180" fill="none" stroke="${colors.border}" stroke-opacity=".7" stroke-width="2"/>
    <path d="M45 38H123M45 60H113M45 82H123M45 104H101" fill="none"
      stroke="${colors.warm}" stroke-width="4" stroke-linecap="round" opacity=".7"/>
    <path d="M45 141l13 11 28-30" fill="none" stroke="${colors.accent}" stroke-width="5"
      stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M109 0V28l8-5 8 5V0" fill="${colors.accent}"/>
  </g>
  <path d="M208 389C226 364 251 374 270 402L302 439C311 450 299 467 285 460
    L257 444" fill="url(#body)" stroke="${colors.edge}" stroke-width="2"/>
  <g transform="translate(487 158) rotate(10)">
    <path d="M0 0L7 64 22 45 44 52Z" fill="${colors.accent}" stroke="${colors.edge}"
      stroke-width="3" stroke-linejoin="round"/>
  </g>
  <path d="M131 218v26m-13-13h26M457 91v18m-9-9h18M568 362v22m-11-11h22"
    stroke="${colors.warm}" stroke-width="2" opacity=".55" stroke-linecap="round"/>
  <path d="M41 652C179 590 336 703 595 615" fill="none" stroke="url(#mist)" stroke-width="39"
    stroke-linecap="round"/>
  <path d="M68 666C214 619 421 712 577 669" fill="none" stroke="${colors.accent}"
    stroke-opacity=".16" stroke-width="1.5"/>`;

const svg = (width, height, title, description, content) => `<svg xmlns="http://www.w3.org/2000/svg"
  width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="title desc">
  <title id="title">${title}</title><desc id="desc">${description}</desc>
  <defs>${defs}</defs>${content}</svg>`;

const background = (width, height) => `
  <rect width="${width}" height="${height}" fill="${colors.base}"/>
  <ellipse cx="${width * .76}" cy="${height * .49}" rx="${width * .48}" ry="${height * .72}"
    fill="url(#aura)"/>
  <path d="M-100 ${height * .91}C${width * .23} ${height * .62} ${width * .67} ${height * 1.1}
    ${width + 80} ${height * .59}L${width + 80} ${height + 80}H-100Z" fill="url(#mist)" opacity=".5"/>
  <path d="M-100 ${height * .94}C${width * .3} ${height * .7} ${width * .69} ${height * 1.08}
    ${width + 60} ${height * .7}" fill="none" stroke="${colors.accent}" stroke-opacity=".16" stroke-width="2"/>
  <path d="M${width * .04} ${height * .83}C${width * .39} ${height * .57} ${width * .63} ${height * 1.08}
    ${width * 1.01} ${height * .87}" fill="none" stroke="${colors.warm}" stroke-opacity=".08" stroke-width="1"/>`;

const poster = svg(1920, 1080, 'Phantom Fudge — the complete walkthrough',
  'Original ivory phantom holding a notebook in deep teal seafoam mist. 38 minutes, 15 chapters.',
  `${background(1920, 1080)}
  <g font-family="${font}">
    <path d="M113 131H172" stroke="${colors.accent}" stroke-width="4"/>
    <text x="196" y="142" fill="${colors.warm}" font-size="28" letter-spacing="4.7" font-weight="600">
      THE COMPLETE WALKTHROUGH</text>
    <text x="105" y="356" fill="${colors.warm}" font-size="157" letter-spacing="-7" font-weight="700">Phantom</text>
    <text x="105" y="519" fill="${colors.warm}" font-size="157" letter-spacing="-7" font-weight="700">Fudge</text>
    <text x="115" y="612" fill="${colors.text}" font-size="35">Select a thought. Ask with intent.</text>
    <path d="M116 704H259" stroke="${colors.accent}" stroke-width="3"/>
    <text x="115" y="774" fill="${colors.warm}" font-size="35" letter-spacing="2.4" font-weight="600">
      38 MIN · 15 CHAPTERS</text>
    <text x="116" y="980" fill="${colors.faint}" font-size="21" letter-spacing="2.1">
      LOCAL TRANSCRIPTION / SPEAKER NOTES / EXPLICIT ASK</text>
  </g>
  <g transform="translate(1057 45) scale(1.24)">${phantom}</g>
  <g transform="translate(977 783)">
    <circle r="61" fill="${colors.accent}" fill-opacity=".1" stroke="${colors.accent}" stroke-width="2"/>
    <path d="M-12-22L23 0-12 22Z" fill="${colors.warm}" stroke-linejoin="round"/>
  </g>`);

const social = svg(1200, 630, 'Phantom Fudge', 'An original ivory notebook-carrying ghost on deep teal.',
  `${background(1200, 630)}
  <g font-family="${font}">
    <path d="M62 83H96" stroke="${colors.accent}" stroke-width="3"/>
    <text x="112" y="90" fill="${colors.warm}" font-size="17" letter-spacing="3">PHANTOM FUDGE</text>
    <text x="55" y="242" fill="${colors.warm}" font-size="96" letter-spacing="-4" font-weight="700">Phantom</text>
    <text x="55" y="345" fill="${colors.warm}" font-size="96" letter-spacing="-4" font-weight="700">Fudge</text>
    <text x="62" y="414" fill="${colors.text}" font-size="23">Your words. Your context. Your call.</text>
    <text x="62" y="554" fill="${colors.warm}" font-size="18" letter-spacing="1.4">THE COMPLETE WALKTHROUGH</text>
    <text x="62" y="585" fill="${colors.accent}" font-size="17" letter-spacing="1.5">38 MIN · 15 CHAPTERS</text>
  </g>
  <g transform="translate(670 -8) scale(.87)">${phantom}</g>`);

const ghost = svg(640, 720, 'Phantom Fudge ghost',
  'Friendly ivory phantom carrying a teal notebook, looking toward a seafoam cursor.', phantom);

const favicon = svg(64, 64, 'Phantom Fudge', 'Ivory ghost with two eyes on deep teal.',
  `<rect width="64" height="64" rx="16" fill="${colors.base}"/>
  <path d="M15 37V26C15 7 46 7 47 26L49 39Q50 47 56 49Q46 54 40 46L33 54 27 47 16 52Q18 44 15 37Z"
    fill="${colors.warm}"/>
  <ellipse cx="26" cy="28" rx="3" ry="5" fill="${colors.base}"/>
  <ellipse cx="37" cy="27" rx="3" ry="5" fill="${colors.base}"/>
  <path d="M50 10v8m-4-4h8" stroke="${colors.accent}" stroke-width="2" stroke-linecap="round"/>`);

await fs.mkdir(assets, { recursive: true });
await fs.mkdir(work, { recursive: true });
await Promise.all([
  fs.writeFile(path.join(assets, 'ghost.svg'), ghost),
  fs.writeFile(path.join(assets, 'favicon.svg'), favicon),
  fs.writeFile(path.join(assets, 'tutorial-poster.svg'), poster),
  fs.writeFile(path.join(work, 'social-card.svg'), social),
]);

const browser = await puppeteer.launch({
  executablePath, headless: true, userDataDir: path.join(work, 'browser-profile'),
  args: ['--disable-background-networking', '--disable-component-update', '--no-first-run'],
});
try {
  const page = await browser.newPage();
  await page.setRequestInterception(true);
  page.on('request', request => request.url().startsWith('data:') ? request.continue() : request.abort());
  for (const [name, width, height, content] of [
    ['tutorial-poster', 1920, 1080, poster], ['social-card', 1200, 630, social],
    ['ghost-review', 640, 720, ghost], ['favicon-review', 64, 64, favicon],
  ]) {
    await page.setViewport({ width, height, deviceScaleFactor: 1 });
    await page.setContent(`<style>html,body{margin:0;background:${colors.base}}svg{display:block}</style>${content}`);
    await page.evaluate(() => document.fonts.ready);
    const output = name.endsWith('review') ? work : assets;
    await page.screenshot({ path: path.join(output, `${name}.jpg`), type: 'jpeg', quality: 94 });
  }
  await page.setViewport({ width: 480, height: 270, deviceScaleFactor: 1 });
  await page.setContent(`<style>html,body{margin:0}svg{display:block;width:480px;height:270px}</style>${poster}`);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: path.join(work, 'tutorial-poster-small.jpg'), type: 'jpeg', quality: 94 });
} finally {
  await browser.close();
}
console.log('Rendered original ghost, favicon, walkthrough poster and social card.');
