export function formatTime(value) {
  const seconds = Math.max(0, Math.floor(Number.isFinite(value) ? value : 0));
  return `${seconds >= 3600 ? `${Math.floor(seconds / 3600)}:` : ''}${String(Math.floor(seconds / 60) % 60).padStart(seconds >= 3600 ? 2 : 1, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

export function clampSeek(time, duration) {
  if (!Number.isFinite(time) || !Number.isFinite(duration) || duration <= 0) throw new RangeError('Invalid media time');
  return Math.min(Math.max(0, duration - 0.05), Math.max(0, time));
}

export function activeIndex(entries, time) {
  // Browser media seeks can round a few microseconds below an authored boundary.
  const adjusted = time + 0.0001;
  let low = 0;
  let high = entries.length - 1;
  while (low <= high) {
    const mid = (low + high) >> 1;
    if (adjusted < entries[mid].start) high = mid - 1;
    else if (adjusted >= entries[mid].end) low = mid + 1;
    else return mid;
  }
  return -1;
}

export function searchCues(cues, query) {
  const needle = query.trim().toLocaleLowerCase('en');
  return cues.filter(cue => cue.text.toLocaleLowerCase('en').includes(needle));
}

export function pageSlice(items, page, size) {
  return items.slice(page * size, (page + 1) * size);
}
