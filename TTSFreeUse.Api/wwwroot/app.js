// TTSFreeUse web page. Kept in its own file (not inline in index.html) so the
// page's Content-Security-Policy can refuse to run any inline script at all.
"use strict";

const $ = (id) => document.getElementById(id);
let mode = 'text';
let ready = false;
let preparing = false;   // true while "Listen now" is splitting the text, before playback starts
let audioUrl = null;
let jobsTimer = null;
const knownDone = new Set();

// ---------- helpers ----------
function setStatus(message, isError = false) {
  $('status').textContent = message;
  $('status').classList.toggle('error', isError);
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children.filter((c) => c != null));
  return node;
}

function formatDuration(seconds) {
  if (seconds == null) return '';
  const s = Math.round(seconds);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  return h ? `${h}h ${m}m` : m ? `${m}m ${sec}s` : `${sec}s`;
}

function formatSize(bytes) {
  return bytes >= 1e9 ? (bytes / 1e9).toFixed(1) + ' GB'
       : bytes >= 1e6 ? (bytes / 1e6).toFixed(1) + ' MB'
       : Math.max(1, Math.round(bytes / 1e3)) + ' KB';
}

async function errorText(res) {
  const text = await res.text();
  try { const j = JSON.parse(text); return j.title || j.detail || text; } catch { return text; }
}

function options() {
  return { voice: $('voice').value, speed: Number($('speed').value) };
}

function updateButtons() {
  const hasInput = mode === 'text' ? $('text').value.trim().length > 0 : $('file').files.length > 0;
  $('listen').disabled = !ready || preparing || !hasInput;
  $('save').disabled = !ready || !hasInput;
  $('hint').textContent = mode === 'text'
    ? '“Listen now” starts reading in a few seconds. “Save audio file” makes a file you can keep or copy to your phone.'
    : '“Listen now” reads the document here. “Save audio file” converts it in the background into a file you can keep.';
}

function updateCount() {
  const words = $('text').value.trim().split(/\s+/).filter(Boolean).length;
  // ~150 spoken words per minute at normal speed
  const minutes = words / 150 / Number($('speed').value);
  $('count').textContent = words
    ? `${words.toLocaleString()} words · ${minutes < 1 ? 'under a minute' : 'about ' + Math.round(minutes) + ' min'} of audio`
    : '';
}

// ---------- engine status & voices ----------
async function waitForEngine() {
  try {
    const s = await (await fetch('/api/speech/status')).json();
    ready = s.ready;
    if (ready) {
      if (!session && !preparing) setStatus('Ready.');
      await loadVoices();
    } else {
      setStatus(s.status);
      setTimeout(waitForEngine, 1500);
    }
  } catch {
    setStatus('Can’t reach the TTSFreeUse server. Is it running?', true);
    setTimeout(waitForEngine, 3000);
  }
  updateButtons();
}

async function loadVoices() {
  const data = await (await fetch('/api/speech/voices')).json();
  const groups = {};
  for (const v of data.voices) {
    const key = `${v.language === 'en-GB' ? 'British' : 'American'} ${v.gender}`;
    (groups[key] ??= []).push(v);
  }
  const select = $('voice');
  select.innerHTML = '';
  for (const [name, voices] of Object.entries(groups)) {
    const group = el('optgroup', { label: name });
    for (const v of voices) group.append(new Option(v.name, v.id, false, v.id === data.defaultVoice));
    select.append(group);
  }
}

// ---------- listen now (live, passage by passage) ----------
// The server splits the text into short passages. We fetch audio for one
// passage at a time and queue it in the Web Audio API, so speech starts as
// soon as the first passage is ready and plays back-to-back from then on.
const PASSAGE_GAP = 0.25;   // seconds of silence between passages
const BUFFER_AHEAD = 30;    // don't generate more than this many seconds ahead
let session = null;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const wordCount = (s) => s.split(/\s+/).filter(Boolean).length;

async function listenNow() {
  stopListening();
  $('audio').pause();
  // Created during the click so the browser allows it to play sound.
  const s = { abort: new AbortController(), ctx: new AudioContext(), passages: [], words: [],
              scheduled: [], nextTime: 0, allLoaded: false, shown: -1, title: '' };
  session = s;
  preparing = true;
  updateButtons();
  setStatus(mode === 'text' ? 'Getting ready…' : 'Reading the document…');
  const signal = s.abort.signal;
  const json = { 'Content-Type': 'application/json' };

  try {
    let res;
    if (mode === 'text') {
      res = await fetch('/api/speech/prepare',
        { method: 'POST', headers: json, body: JSON.stringify({ text: $('text').value }), signal });
    } else {
      const form = new FormData();
      form.append('file', $('file').files[0]);
      res = await fetch('/api/speech/prepare-file', { method: 'POST', body: form, signal });
    }
    if (!res.ok) return failListening(s, await errorText(res) || `Something went wrong (${res.status}).`);
    const data = await res.json();
    if (session !== s) return;

    s.passages = data.passages;
    s.words = s.passages.map(wordCount);
    s.title = mode === 'file' ? data.title : '';
    preparing = false;
    updateButtons();
    $('reader').hidden = false;
    $('pause').textContent = 'Pause';
    $('now-reading').textContent = '';
    s.timer = setInterval(() => updateReader(s), 200);
    updateReader(s);

    for (let i = 0; i < s.passages.length; i++) {
      while (session === s && s.nextTime - s.ctx.currentTime > BUFFER_AHEAD) await sleep(300);
      if (session !== s) return;

      // Voice and speed are read each time, so changes apply from the next passage.
      res = await fetch('/api/speech/text',
        { method: 'POST', headers: json, body: JSON.stringify({ text: s.passages[i], ...options() }), signal });
      if (!res.ok) return failListening(s, await errorText(res) || `Something went wrong (${res.status}).`);
      const buffer = await s.ctx.decodeAudioData(await res.arrayBuffer());
      if (session !== s) return;

      const start = Math.max(s.nextTime, s.ctx.currentTime + 0.1);
      const source = s.ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(s.ctx.destination);
      source.start(start);
      s.scheduled.push({ index: i, start, end: start + buffer.duration });
      s.nextTime = start + buffer.duration + PASSAGE_GAP;
    }
    s.allLoaded = true;
  } catch (e) {
    if (e.name !== 'AbortError' && session === s) failListening(s, 'Request failed: ' + e.message);
  } finally {
    if (session === s || session === null) { preparing = false; updateButtons(); }
  }
}

function updateReader(s) {
  if (session !== s) return;
  const t = s.ctx.currentTime;
  const total = s.passages.length;
  const current = s.scheduled.find((p) => t >= p.start && t < p.end + PASSAGE_GAP);
  const last = s.scheduled[s.scheduled.length - 1];

  if (s.allLoaded && last && t >= last.end) {
    stopListening();
    setStatus(`Finished reading${s.title ? ' ' + s.title : ''}.`);
    return;
  }
  if (current) {
    if (s.shown !== current.index) {
      s.shown = current.index;
      $('now-reading').textContent = s.passages[current.index];
    }
    const wordsLeft = s.words.slice(current.index + 1).reduce((a, b) => a + b, 0);
    const minutesLeft = wordsLeft / 150 / Number($('speed').value);
    $('reader-progress').textContent = `Part ${current.index + 1} of ${total}` +
      (wordsLeft ? ` · about ${minutesLeft < 1 ? 'a minute' : Math.round(minutesLeft) + ' min'} left` : '');
    setStatus((s.ctx.state === 'suspended' ? 'Paused' : 'Reading') + (s.title ? ': ' + s.title : ''));
  } else {
    $('reader-progress').textContent = total ? `Part ${s.scheduled.length + 1} of ${total}` : '';
    setStatus(s.scheduled.length ? 'Loading the next part…' : 'Starting…');
  }
  const done = current ? current.index : s.scheduled.length;
  $('reader-bar').style.width = `${Math.round((done / Math.max(total, 1)) * 100)}%`;
}

function togglePause() {
  const s = session;
  if (!s) return;
  if (s.ctx.state === 'running') { s.ctx.suspend(); $('pause').textContent = 'Resume'; }
  else { s.ctx.resume(); $('pause').textContent = 'Pause'; }
  updateReader(s);
}

function stopListening() {
  const s = session;
  session = null;
  if (!s) return;
  s.abort.abort();   // also stops the server generating the passage in progress
  clearInterval(s.timer);
  s.ctx.close().catch(() => {});
  $('reader').hidden = true;
}

function failListening(s, message) {
  if (session !== s) return;
  stopListening();
  setStatus(message, true);
}

function playUrl(url, label = null) {
  stopListening();
  if (audioUrl && audioUrl.startsWith('blob:')) URL.revokeObjectURL(audioUrl);
  audioUrl = url;
  $('audio').src = url;
  $('player').hidden = false;
  if (label) setStatus('Playing: ' + label);
  $('audio').play().catch(() => {});
}

// ---------- save audio file (background job) ----------
async function saveFile() {
  $('save').disabled = true;
  const format = $('format').value;
  let res;
  try {
    if (mode === 'text') {
      res = await fetch('/api/jobs/text', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: $('text').value, format, ...options() }),
      });
    } else {
      const form = new FormData();
      form.append('file', $('file').files[0]);
      const o = options();
      form.append('voice', o.voice);
      form.append('speed', o.speed);
      form.append('format', format);
      res = await fetch('/api/jobs/file', { method: 'POST', body: form });
    }
    if (!res.ok) { setStatus(await errorText(res) || `Something went wrong (${res.status}).`, true); return; }
    setStatus('Added to the conversion list below.');
    if (mode === 'file') $('file').value = '';
    refreshJobs();
  } catch (e) {
    setStatus('Request failed: ' + e.message, true);
  } finally {
    updateButtons();
  }
}

async function refreshJobs() {
  clearTimeout(jobsTimer);
  let jobs = [];
  try { jobs = await (await fetch('/api/jobs')).json(); } catch { /* server restarting */ }

  const list = $('jobs');
  list.innerHTML = '';
  $('jobs-panel').hidden = jobs.length === 0;
  let finishedNow = false;

  for (const j of jobs) {
    const active = ['Queued', 'Narrating', 'Encoding'].includes(j.state);
    if (j.state === 'Completed' && !knownDone.has(j.id)) { knownDone.add(j.id); finishedNow = true; }

    let detail;
    if (j.state === 'Queued') detail = 'Waiting…';
    else if (j.state === 'Narrating') detail = `${Math.round(j.progress * 100)}%` +
      (j.secondsRemaining != null ? ` · about ${formatDuration(j.secondsRemaining)} left` : '');
    else if (j.state === 'Encoding') detail = j.statusText || 'Converting…';
    else if (j.state === 'Completed') detail = `Done · ${formatDuration(j.audioSeconds)} of audio · saved as ${j.fileName}`;
    else if (j.state === 'Cancelled') detail = 'Cancelled';
    else detail = 'Failed: ' + (j.error || 'unknown error');

    const actions = active
      ? el('button', { className: 'link danger', textContent: 'Cancel',
          onclick: () => fetch(`/api/jobs/${j.id}/cancel`, { method: 'POST' }).then(refreshJobs) })
      : el('button', { className: 'link', textContent: 'Remove from list',
          onclick: () => fetch(`/api/jobs/${j.id}`, { method: 'DELETE' }).then(refreshJobs) });

    list.append(el('li', {},
      el('div', { className: 'row' },
        el('span', { className: 'name', textContent: `${j.title} (${j.format.toUpperCase()})` }),
        actions),
      el('div', { className: 'meta' + (j.state === 'Failed' ? ' error' : ''), textContent: detail }),
      active ? el('div', { className: 'bar' }, el('div', { style: `width:${Math.round(j.progress * 100)}%` })) : null));
  }

  if (finishedNow) refreshLibrary();
  if (jobs.some((j) => ['Queued', 'Narrating', 'Encoding'].includes(j.state)))
    jobsTimer = setTimeout(refreshJobs, 1000);
}

// ---------- library ----------
async function refreshLibrary() {
  let data;
  try { data = await (await fetch('/api/library')).json(); } catch { return; }
  const list = $('library');
  list.innerHTML = '';
  if (data.items.length === 0)
    list.append(el('li', {}, el('p', { className: 'empty', textContent: 'Saved audio files will appear here.' })));

  for (const item of data.items) {
    const url = '/api/library/' + encodeURIComponent(item.fileName);
    const confirmBox = el('span', { hidden: true },
      el('span', { className: 'meta', textContent: 'Delete this file? ' }),
      el('button', { className: 'link danger', textContent: 'Yes, delete',
        onclick: async () => {
          if (audioUrl === url) { $('audio').pause(); $('player').hidden = true; }
          const res = await fetch(url, { method: 'DELETE' });
          // e.g. the file is open in a music player, so Windows won't delete it
          if (!res.ok && res.status !== 404) setStatus(await errorText(res), true);
          refreshLibrary();
        } }),
      ' ',
      el('button', { className: 'link', textContent: 'Keep', onclick: () => { confirmBox.hidden = true; } }));

    list.append(el('li', {},
      el('div', { className: 'row' },
        el('span', { className: 'name', textContent: item.fileName }),
        el('button', { className: 'link', textContent: 'Play', onclick: () => playUrl(url, item.fileName) }),
        el('a', { className: 'link', href: url + '?download=true', textContent: 'Download' }),
        el('button', { className: 'link danger', textContent: 'Delete', onclick: () => { confirmBox.hidden = false; } })),
      el('div', { className: 'meta' },
        `${formatSize(item.sizeBytes)} · ${new Date(item.created).toLocaleString()} `, confirmBox)));
  }
  $('library-hint').textContent = `Files are saved in ${data.directory}. ` +
    'To listen on your phone, copy them over with a USB cable or put them in your cloud storage (OneDrive, Google Drive, iCloud).';
}

// ---------- pronunciations ----------
let pronEntries = [];

async function loadPronunciations() {
  let data;
  try { data = await (await fetch('/api/pronunciations')).json(); } catch { return; }
  pronEntries = data.entries.map((e) => ({ ...e }));
  renderPronunciations();
  $('pron-save').disabled = true;
  $('pron-status').textContent = data.error || `Saved on this computer in ${data.file}`;
  $('pron-status').classList.toggle('error', !!data.error);
}

function markPronunciationsChanged() {
  $('pron-save').disabled = false;
  $('pron-status').textContent = 'Unsaved changes.';
  $('pron-status').classList.remove('error');
}

function renderPronunciations() {
  const rows = $('pron-rows');
  rows.innerHTML = '';
  if (pronEntries.length === 0)
    rows.append(el('p', { className: 'empty', textContent: 'No words added yet.' }));

  pronEntries.forEach((entry, i) => {
    const term = el('input', { type: 'text', value: entry.term, placeholder: 'e.g. Romex' });
    term.setAttribute('aria-label', 'Written');
    term.addEventListener('input', () => { entry.term = term.value; markPronunciationsChanged(); });

    const sayAs = el('input', { type: 'text', value: entry.sayAs, placeholder: 'e.g. roh mex' });
    sayAs.setAttribute('aria-label', 'Say it as');
    sayAs.addEventListener('input', () => { entry.sayAs = sayAs.value; markPronunciationsChanged(); });

    const matchCase = el('input', { type: 'checkbox', checked: entry.matchCase });
    matchCase.addEventListener('change', () => { entry.matchCase = matchCase.checked; markPronunciationsChanged(); });

    rows.append(el('div', { className: 'pron-row' },
      term, sayAs,
      el('label', { className: 'check', title: 'Only when capitalized exactly like this' }, matchCase, 'Match case'),
      el('button', { className: 'link', textContent: 'Hear',
        onclick: () => speakOnce(entry.sayAs || entry.term, entry.term) }),
      el('button', { className: 'link danger', textContent: 'Remove',
        onclick: () => { pronEntries.splice(i, 1); renderPronunciations(); markPronunciationsChanged(); } })));
  });
}

async function savePronunciations() {
  $('pron-save').disabled = true;
  try {
    const res = await fetch('/api/pronunciations', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(pronEntries),
    });
    if (!res.ok) {
      $('pron-status').textContent = await errorText(res);
      $('pron-status').classList.add('error');
      $('pron-save').disabled = false;
      return;
    }
    await loadPronunciations();
    $('pron-status').textContent = 'Saved. ' + $('pron-status').textContent;
  } catch (e) {
    $('pron-status').textContent = 'Couldn’t save: ' + e.message;
    $('pron-status').classList.add('error');
    $('pron-save').disabled = false;
  }
}

async function previewPronunciation() {
  const res = await fetch('/api/pronunciations/preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text: $('pron-test').value }),
  });
  $('pron-spoken').hidden = false;
  $('pron-spoken').textContent = res.ok ? (await res.json()).spoken : await errorText(res);
}

// Plays a short piece of text once, through the same rules as everything else.
async function speakOnce(text, label) {
  if (!ready || !text.trim()) return;
  setStatus('Generating speech…');
  try {
    const res = await fetch('/api/speech/text', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, ...options() }),
    });
    if (!res.ok) { setStatus(await errorText(res) || `Something went wrong (${res.status}).`, true); return; }
    playUrl(URL.createObjectURL(await res.blob()), label);
  } catch (e) {
    setStatus('Request failed: ' + e.message, true);
  }
}

$('pron-add').addEventListener('click', () => {
  pronEntries.push({ term: '', sayAs: '', matchCase: false });
  renderPronunciations();
  markPronunciationsChanged();
  $('pron-rows').querySelector('.pron-row:last-child input')?.focus();
});
$('pron-save').addEventListener('click', savePronunciations);
$('pron-preview').addEventListener('click', previewPronunciation);
$('pron-hear').addEventListener('click', () => {
  if (!$('pron-save').disabled) $('pron-status').textContent = 'Save the list first to hear your changes.';
  previewPronunciation();
  speakOnce($('pron-test').value, 'pronunciation test');
});

// ---------- wiring ----------
document.querySelectorAll('.tabs button').forEach((tab) => {
  tab.addEventListener('click', () => {
    mode = tab.dataset.tab;
    document.querySelectorAll('.tabs button').forEach((t) => t.setAttribute('aria-selected', t === tab));
    $('tab-text').hidden = mode !== 'text';
    $('tab-file').hidden = mode !== 'file';
    updateButtons();
  });
});
$('text').addEventListener('input', () => { updateCount(); updateButtons(); });
$('file').addEventListener('change', updateButtons);
$('speed').addEventListener('input', () => {
  $('speed-value').textContent = Number($('speed').value).toFixed(1) + '×';
  updateCount();
});
$('listen').addEventListener('click', listenNow);
$('pause').addEventListener('click', togglePause);
$('stop').addEventListener('click', () => { stopListening(); setStatus('Stopped.'); });
$('save').addEventListener('click', saveFile);
$('text').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && !$('listen').disabled) listenNow();
});

updateCount();
updateButtons();
waitForEngine();
refreshJobs();
refreshLibrary();
loadPronunciations();
if (location.hash === '#pronunciations') $('pron-panel').open = true;
