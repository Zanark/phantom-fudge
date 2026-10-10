import { tutorial } from './assets/tutorial-data.mjs';
import { formatTime, clampSeek, activeIndex, searchCues, pageSlice } from './player-core.mjs';

const $ = id => document.getElementById(id);
const video = $('video');
let pendingSeek = null;
let extraCaptions = false;
let chapterPage = 0;
let transcriptPage = 0;
let lastChapter = -1;
let matches = tutorial.cues;
const chapterSize = 5;
const transcriptSize = 6;

function status(message) {
  $('status').textContent = message;
  $('status').hidden = !message;
}

function seek(time) {
  const target = clampSeek(time, tutorial.duration);
  if (video.readyState >= 1) video.currentTime = target;
  else {
    pendingSeek = target;
    video.load();
  }
}

function renderChapterPage() {
  [...$('chapters').children].forEach((button, index) => {
    button.hidden = Math.floor(index / chapterSize) !== chapterPage;
  });
  $('chapter-prev').disabled = chapterPage === 0;
  $('chapter-next').disabled = (chapterPage + 1) * chapterSize >= tutorial.chapters.length;
  $('chapter-page').textContent = `${chapterPage * chapterSize + 1}-${Math.min((chapterPage + 1) * chapterSize, tutorial.chapters.length)} of ${tutorial.chapters.length}`;
}

for (const [index, chapter] of tutorial.chapters.entries()) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'chapter';
  button.dataset.start = chapter.start;
  button.dataset.chapter = chapter.id;
  button.setAttribute('aria-label', `${formatTime(chapter.start)} - ${chapter.title}`);
  const time = document.createElement('span');
  time.className = 'chapter-time';
  time.textContent = formatTime(chapter.start);
  const name = document.createElement('span');
  name.className = 'chapter-name';
  const number = document.createElement('small');
  number.textContent = `CHAPTER ${String(index + 1).padStart(2, '0')}`;
  name.append(number, document.createTextNode(chapter.title));
  button.append(time, name);
  button.addEventListener('click', () => seek(chapter.start));
  $('chapters').append(button);
}
$('chapter-prev').addEventListener('click', () => { chapterPage--; renderChapterPage(); });
$('chapter-next').addEventListener('click', () => { chapterPage++; renderChapterPage(); });
renderChapterPage();
$('chapter-pager').hidden = false;

function renderTranscript() {
  const fragment = document.createDocumentFragment();
  for (const cue of pageSlice(matches, transcriptPage, transcriptSize)) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'cue';
    button.dataset.start = cue.start;
    const time = document.createElement('span');
    time.className = 'cue-time';
    time.textContent = formatTime(cue.start);
    const text = document.createElement('span');
    text.textContent = cue.text;
    button.append(time, text);
    button.addEventListener('click', () => seek(cue.start));
    fragment.append(button);
  }
  if (!matches.length) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = 'No matching narration. Try a different word or clear the search.';
    fragment.append(empty);
  }
  $('transcript').replaceChildren(fragment);
  $('transcript-page').textContent = matches.length ? `${transcriptPage * transcriptSize + 1}-${Math.min((transcriptPage + 1) * transcriptSize, matches.length)} of ${matches.length} results` : '0 results';
  $('transcript-prev').disabled = transcriptPage === 0;
  $('transcript-next').disabled = (transcriptPage + 1) * transcriptSize >= matches.length;
}
$('search').disabled = false;
$('search').addEventListener('input', () => {
  matches = searchCues(tutorial.cues, $('search').value);
  transcriptPage = 0;
  renderTranscript();
});
$('transcript-prev').addEventListener('click', () => { transcriptPage--; renderTranscript(); });
$('transcript-next').addEventListener('click', () => { transcriptPage++; renderTranscript(); });
renderTranscript();
$('transcript-pager').hidden = false;

async function togglePlayback() {
  if (!video.paused) { video.pause(); return; }
  try {
    await video.play();
    status('');
  } catch (error) {
    status(`Playback could not start (${error.name}). Try the native video controls or download the MP4.`);
  }
}

function update() {
  const time = video.currentTime;
  $('time').textContent = `${formatTime(time)} / ${formatTime(tutorial.duration)}`;
  $('play').textContent = video.paused ? 'Play' : 'Pause';
  const chapter = activeIndex(tutorial.chapters, time);
  [...$('chapters').children].forEach((button, index) => button.setAttribute('aria-current', String(index === chapter)));
  if (chapter >= 0 && chapter !== lastChapter) {
    chapterPage = Math.floor(chapter / chapterSize);
    renderChapterPage();
    lastChapter = chapter;
  }
  const cue = extraCaptions ? activeIndex(tutorial.cues, time) : -1;
  $('caption').hidden = cue < 0;
  $('caption').textContent = cue < 0 ? '' : tutorial.cues[cue].text;
}

video.addEventListener('loadedmetadata', () => {
  if (pendingSeek !== null) { video.currentTime = pendingSeek; pendingSeek = null; }
  update();
});
for (const event of ['timeupdate', 'play', 'pause', 'seeked', 'ended']) video.addEventListener(event, update);
video.addEventListener('ratechange', () => { $('speed').value = String(video.playbackRate); });
function mediaFailure() {
  status('The tutorial could not be loaded. Reload the page or try the downloadable MP4. For a local preview, run node tools/Site/build.mjs to assemble the bundled website.');
}
video.addEventListener('error', mediaFailure);
video.querySelector('source').addEventListener('error', mediaFailure);
if (video.error || video.networkState === video.NETWORK_NO_SOURCE) mediaFailure();
$('play').addEventListener('click', togglePlayback);
$('back').addEventListener('click', () => seek(video.currentTime - 10));
$('forward').addEventListener('click', () => seek(video.currentTime + 10));
$('speed').addEventListener('change', () => { video.playbackRate = Number($('speed').value); });
$('captions').addEventListener('click', () => {
  extraCaptions = !extraCaptions;
  $('captions').textContent = `Extra captions: ${extraCaptions ? 'on' : 'off'}`;
  $('captions').setAttribute('aria-pressed', String(extraCaptions));
  update();
});
$('controls').hidden = false;

for (const link of document.querySelectorAll('[data-jump]')) {
  link.addEventListener('click', () => {
    const chapter = tutorial.chapters.find(item => item.id === link.dataset.jump);
    seek(chapter.start);
  });
}

const tabs = [...document.querySelectorAll('[role="tab"]')];
function showDemo(index) {
  tabs.forEach((tab, position) => {
    const active = position === index;
    tab.setAttribute('aria-selected', String(active));
    tab.tabIndex = active ? 0 : -1;
    const panel = $(tab.getAttribute('aria-controls'));
    panel.setAttribute('role', 'tabpanel');
    panel.setAttribute('aria-labelledby', tab.id);
    panel.hidden = !active;
  });
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => showDemo(index));
  tab.addEventListener('keydown', event => {
    const next = { ArrowRight: (index + 1) % tabs.length, ArrowLeft: (index + tabs.length - 1) % tabs.length, Home: 0, End: tabs.length - 1 }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    showDemo(next);
    tabs[next].focus();
  });
});
showDemo(0);
$('demo-tabs').hidden = false;

const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let motionPaused = reducedMotion.matches;
function setMotion(paused) {
  motionPaused = paused;
  document.body.classList.toggle('motion-paused', paused);
  document.body.classList.toggle('motion-enabled', !paused);
  $('motion').textContent = paused ? 'Resume motion' : 'Pause motion';
  $('motion').setAttribute('aria-pressed', String(paused));
  for (const image of document.querySelectorAll('[data-motion]')) {
    image.src = paused ? image.dataset.still : image.dataset.motion;
  }
}
$('motion').hidden = false;
$('motion').addEventListener('click', () => setMotion(!motionPaused));
reducedMotion.addEventListener('change', event => setMotion(event.matches));
setMotion(motionPaused);
update();
