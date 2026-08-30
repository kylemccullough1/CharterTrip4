// Braun Manor, synthesised.
//
// Same shape as jeopardy-audio.js and for the same reason: no files to download, nothing to go
// wrong on house wifi with no internet. One looping bed at a time, named by the phase, plus a
// handful of one-shots layered over it.
//
// The recordings are the exception. The theme, the rain, the thunderclaps, the scream, the
// sneaking piece under the rounds and the cell door under a conviction in wwwroot/audio are real,
// fetched the moment this module loads and played as buffers through the same master gain as
// everything else, so one mute button covers the lot. Buffers rather than <audio> elements
// because an element needs its own play permission on top of the context's. The scream is the
// one with a fallback — if the file is missing or will not decode, the synth stands in.
//
// The weather is also here, because the thunder has to land on the flash. Lightning used to be
// two CSS animations on their own clocks; now strike() decides when, puts a class on the storm
// for the stylesheet to flash to, and plays the next clap in the cycle.

let ctx = null;
let master = null;
let muted = false;

let bed = null;        // { name, stop }
let pending = null;    // the bed we want as soon as the browser lets us make noise
let disarm = null;     // removes the gesture listener waiting on that permission

const THEME = '/audio/MurderMysteryMain.mp3';
const RAIN = '/audio/rain-ambience.mp3';
const CLAPS = ['/audio/thunder-1.mp3', '/audio/thunder-2.mp3', '/audio/thunder-3.mp3'];
const STUDY = '/audio/thunder-3.mp3';    // looped under the study: thunder rolling through, every fourteen seconds
const SNEAK = '/audio/sneaking.mp3';     // looped under the investigation and the deliberations
const TRIAL = '/audio/trial.mp3';        // looped under the trials and the votes
const MURDER_CLAP = '/audio/murder-thunder.mp3';   // the one clap that goes with the white flash
const SCREAM = '/audio/scream.mp3';
const CELL_DOOR = '/audio/metal.mp3';    // a conviction turned face up

const files = new Map();   // url -> { bytes: Promise<ArrayBuffer|null>, buffer: AudioBuffer|null }

let rain = null;           // { stop } while the ambience is running
let rainWanted = false;    // asked for before the browser would let us make noise

let storm = null;          // { el, timer } while the lightning is being scheduled
let clapIndex = 0;         // which clap is next; 1, 2, 3, 1, …

const GESTURES = ['pointerdown', 'keydown', 'touchstart'];

function audio() {
    if (!ctx) {
        ctx = new (window.AudioContext || window.webkitAudioContext)();
        master = ctx.createGain();
        master.gain.value = muted ? 0 : 0.5;
        master.connect(ctx.destination);
    }
    return ctx;
}

export async function unlock() {
    const c = audio();
    if (c.state === 'suspended') await c.resume();
    return c.state;
}

export function setMuted(value) {
    muted = value;
    if (master) master.gain.setTargetAtTime(muted ? 0 : 0.5, audio().currentTime, 0.02);
}

// ---------------------------------------------------------------- building blocks

/// One shaped note.
function note({ freq = 220, start = 0, dur = 1, type = 'sine', peak = 0.2, glideTo = null }) {
    const c = audio();
    const t = c.currentTime + start;

    const osc = c.createOscillator();
    const gain = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (glideTo) osc.frequency.exponentialRampToValueAtTime(glideTo, t + dur);

    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    osc.connect(gain).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.02);
    return osc;
}

/// Filtered noise. This one function is most of the weather.
function noise({ start = 0, dur = 1, peak = 0.3, type = 'lowpass', from = 4000, to = 80, q = 0.7 }) {
    const c = audio();
    const t = c.currentTime + start;

    const frames = Math.max(1, Math.floor(c.sampleRate * dur));
    const buffer = c.createBuffer(1, frames, c.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < frames; i++) data[i] = Math.random() * 2 - 1;

    const src = c.createBufferSource();
    src.buffer = buffer;

    const filter = c.createBiquadFilter();
    filter.type = type;
    filter.Q.value = q;
    filter.frequency.setValueAtTime(from, t);
    filter.frequency.exponentialRampToValueAtTime(Math.max(20, to), t + dur);

    const gain = c.createGain();
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.01);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    src.connect(filter).connect(gain).connect(master);
    src.start(t);
    src.stop(t + dur + 0.02);
}

// ---------------------------------------------------------------- one-shots

/// Somebody came through the door. The bee's chime, because it is the same moment on the same
/// kind of wall: small, because twenty-five of these go off and two may land together. Skipped
/// rather than queued while the context is still locked — a note scheduled on a suspended
/// context plays the moment somebody finally touches the screen, which is a chime for nobody.
export function joined() {
    if (audio().state !== 'running') return;
    note({ freq: 880, dur: 0.06, type: 'triangle', peak: 0.16 });
    note({ freq: 1318.5, start: 0.05, dur: 0.10, type: 'triangle', peak: 0.13 });
}

/// The next clap in the cycle, at a volume. The index moves whether or not anything is heard,
/// so a wall nobody has touched yet still comes in on the right one.
///
/// Only a clap already decoded is played — the file is fetched at load and decoded on the first
/// strike, so at worst the first flash of the night is the synth and every one after is the
/// recording. A decode awaited here would land the sound seconds after the flash.
function clap(peak) {
    const url = CLAPS[clapIndex++ % CLAPS.length];
    const c = audio();

    const cached = files.get(url)?.buffer;
    if (!cached) {
        loadFile(url);
        return false;
    }

    if (c.state !== 'running') return true;

    const gain = c.createGain();
    gain.gain.value = peak;
    gain.connect(master);

    const src = c.createBufferSource();
    src.buffer = cached;
    src.connect(gain);
    src.start();
    return true;
}

/// The recording if there is one; three layers of synth if there is not yet — because one layer
/// is a hiss and two is a door slamming.
export function thunder(peak = 1.8) {
    if (clap(peak)) return;

    // The crack: bright, brief, and the thing that makes people jump.
    noise({ dur: 0.07, peak: 0.5, type: 'highpass', from: 3000, to: 6000, q: 0.5 });

    // The body: a long sweep down into the floor.
    noise({ start: 0.03, dur: 2.6, peak: 0.42, type: 'lowpass', from: 4000, to: 80 });

    // The floor itself. On a phone speaker this is felt more than heard, which is the point.
    note({ freq: 42, start: 0.02, dur: 3.0, type: 'sine', peak: 0.34 });
}

/// A body hitting the floor: no crack, all weight. Low noise and a sine dropping through the
/// floor, short, so it reads as one thing landing rather than a room coming apart.
export function thud() {
    noise({ dur: 0.32, peak: 0.55, type: 'lowpass', from: 420, to: 60, q: 0.9 });
    note({ freq: 58, dur: 0.45, type: 'sine', peak: 0.5, glideTo: 30 });
}

/// One recording, once, at a level. Nothing if it has not decoded yet — a one-shot that lands
/// seconds late is worse than one that does not land.
function play(url, peak = 1) {
    const c = audio();
    const cached = files.get(url)?.buffer;
    if (!cached) { loadFile(url); return false; }
    if (c.state !== 'running') return true;

    const gain = c.createGain();
    gain.gain.value = peak;
    gain.connect(master);

    const src = c.createBufferSource();
    src.buffer = cached;
    src.connect(gain);
    src.start();
    return true;
}

/// The cell door, when a conviction is turned face up.
export function cellDoor() {
    play(CELL_DOOR, 1.4);
}

/**
 * The scream.
 *
 * Tries the recording first. Falls back to two detuned saws through a bandpass with a ragged
 * decay — which is not a person, but is unmistakably a person's worst moment, and it is better
 * than silence at the one instant everybody is listening.
 */
export async function scream() {
    const buffer = await loadFile(SCREAM);

    if (buffer) {
        const c = audio();
        const gain = c.createGain();
        gain.gain.value = 1.6;
        gain.connect(master);

        const src = c.createBufferSource();
        src.buffer = buffer;
        src.connect(gain);
        src.start(c.currentTime);
        return;
    }

    const c = audio();
    const t = c.currentTime;

    const band = c.createBiquadFilter();
    band.type = 'bandpass';
    band.frequency.setValueAtTime(1200, t);
    band.Q.value = 2.4;

    const gain = c.createGain();
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(0.34, t + 0.04);
    gain.gain.setValueAtTime(0.34, t + 0.5);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + 1.5);

    band.connect(gain).connect(master);

    // A little vibrato, because a held tone reads as a siren rather than a voice.
    const vibrato = c.createOscillator();
    const depth = c.createGain();
    vibrato.frequency.value = 6.2;
    depth.gain.value = 34;
    vibrato.connect(depth);

    for (const detune of [0, 11]) {
        const osc = c.createOscillator();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(760 + detune, t);
        osc.frequency.exponentialRampToValueAtTime(1080 + detune, t + 0.35);
        osc.frequency.exponentialRampToValueAtTime(520 + detune, t + 1.5);
        depth.connect(osc.frequency);
        osc.connect(band);
        osc.start(t);
        osc.stop(t + 1.55);
    }

    vibrato.start(t);
    vibrato.stop(t + 1.55);

    // Breath under it.
    noise({ dur: 1.4, peak: 0.1, type: 'bandpass', from: 900, to: 500, q: 1.2 });
}

/// The whole murder, in order, so the page fires one thing rather than three on timers: the clap
/// that goes with the white flash, then a man screaming, then the weight of him hitting the
/// floor. The stylesheet's flash is timed to the same numbers (see .ms-flash).
export async function murder() {
    if (!play(MURDER_CLAP, 2.0)) thunder(2.0);
    setTimeout(() => scream(), 900);
    setTimeout(() => thud(), 2300);
}

// ---------------------------------------------------------------- beds

/**
 * One looping bed at a time, named by the phase.
 *
 * Named rather than started and stopped per transition, because the alternative was a stop call
 * at every phase change and one forgotten `stop` means two pieces of music playing at once for
 * the rest of the night.
 */
export async function setBed(name) {
    if (bed?.name === name) return;

    stopBed();
    if (!name) return;

    const c = audio();
    if (c.state === 'suspended') {
        // Not allowed to make noise yet. Remember what we wanted and wait for any gesture.
        pending = name;
        arm();
        return;
    }

    bed = { name, stop: START[name] ? START[name]() : () => {} };
}

const START = {
    /// The theme, looped. Starts the moment the bytes are decoded, which on a wall that has been
    /// open since the doors opened is immediately: the fetch began when the module loaded.
    main() { return loop(THEME, 0.6); },

    /// The house itself: two low sines beating slowly against each other.
    manor() {
        const c = audio();
        const gain = c.createGain();
        gain.gain.value = 0.09;
        gain.connect(master);

        const oscs = [55, 82.6].map(freq => {
            const osc = c.createOscillator();
            osc.type = 'sine';
            osc.frequency.value = freq;
            osc.connect(gain);
            osc.start();
            return osc;
        });

        return () => oscs.forEach(o => { try { o.stop(); } catch { /* already gone */ } });
    },

    /// The study: the third clap on a loop, so thunder rolls through the house every fourteen
    /// seconds while the room reads the scene.
    study() { return loop(STUDY, 1.2); },

    /// The investigation and the deliberations: the sneaking piece, quietly, under the work.
    sneak() { return loop(SNEAK, 0.55); },

    /// The trials, and the votes inside them: their own piece.
    trial() { return loop(TRIAL, 0.6); }
};

// ---------------------------------------------------------------- the recordings

/// The bytes now, the decode later. Fetching needs nothing; decoding needs a context, and making
/// one before anybody has touched the page earns a console warning per tab. A decoded buffer is
/// plain samples and outlives the context that made it, so dispose() keeps them.
function fetchFile(url) {
    let entry = files.get(url);
    if (!entry) {
        entry = {
            bytes: fetch(url, { cache: 'force-cache' })
                .then(r => r.ok ? r.arrayBuffer() : null)
                .catch(() => null),
            buffer: null
        };
        files.set(url, entry);
    }
    return entry;
}

async function loadFile(url) {
    const entry = fetchFile(url);
    if (entry.buffer) return entry.buffer;

    const bytes = await entry.bytes;
    if (!bytes) return null;

    try {
        // A copy, because decodeAudioData detaches the buffer it is given and a second context
        // (after a dispose) would otherwise find nothing to decode.
        entry.buffer = await audio().decodeAudioData(bytes.slice(0));
    } catch {
        entry.buffer = null;
    }
    return entry.buffer;
}

const loadTheme = () => loadFile(THEME);

[THEME, RAIN, TRIAL, SNEAK, MURDER_CLAP, SCREAM, CELL_DOOR, ...CLAPS].forEach(fetchFile);

/// A recording on a loop at a level, started as soon as it is decoded and stoppable before then.
function loop(url, level) {
    let stopped = false;
    let src = null;

    (async () => {
        const buffer = await loadFile(url);
        if (stopped || !buffer) return;

        const c = audio();
        const gain = c.createGain();
        gain.gain.value = level;
        gain.connect(master);

        src = c.createBufferSource();
        src.buffer = buffer;
        src.loop = true;
        src.connect(gain);
        src.start();
    })();

    return () => {
        stopped = true;
        if (src) { try { src.stop(); } catch { /* already gone */ } }
    };
}

// ---------------------------------------------------------------- the weather

/**
 * Rain, under everything, for as long as the page is open. Not a bed: the beds change with the
 * phase and this does not, and a phase that wanted silence from the music still has weather.
 */
export function setAmbience(on) {
    rainWanted = on;

    if (!on) { stopRain(); return; }
    if (rain) return;

    if (audio().state === 'suspended') { arm(); return; }
    startRain();
}

function startRain() {
    if (rain) return;
    rain = { stop: loop(RAIN, 0.7) };
}

function stopRain() {
    if (rain) { try { rain.stop(); } catch { /* nothing running */ } }
    rain = null;
}

const STRIKE_GAP_MIN = 6000;
const STRIKE_GAP_MAX = 16000;

/**
 * Lightning, sporadically, from here rather than from a CSS clock — so the thunder can go with
 * it. Each strike puts a class on the storm for a couple of seconds; the stylesheet does the
 * flashing. Near strikes take the bolt and a loud clap almost at once; far ones light the sheet
 * past the hill, and the clap comes later and quieter, the way it does.
 */
export function startStorm() {
    if (storm) return;

    const el = document.querySelector('.ms-storm');
    if (!el) return;

    storm = { el, timer: null };

    // Decoded now, not on first use: clap() only plays what is already decoded, so a clap met
    // for the first time at its strike would be the synth. Decoding works on a context that is
    // not yet allowed to make noise; only the playing waits on the gesture. The murder's clap,
    // the scream and the cell door are the same shape of problem.
    [...CLAPS, MURDER_CLAP, SCREAM, CELL_DOOR].forEach(loadFile);

    scheduleStrike();
}

function scheduleStrike() {
    if (!storm) return;
    const gap = STRIKE_GAP_MIN + Math.random() * (STRIKE_GAP_MAX - STRIKE_GAP_MIN);
    storm.timer = setTimeout(strike, gap);
}

function strike() {
    if (!storm) return;

    const near = Math.random() < 0.6;
    const cls = near ? 'is-strike' : 'is-sheet';
    const el = storm.el;

    // Off, reflow, on: the only way to restart a CSS animation that may still be running.
    el.classList.remove('is-strike', 'is-sheet');
    void el.offsetWidth;
    el.classList.add(cls);
    setTimeout(() => el.classList.remove(cls), 2400);

    const delay = near ? 150 + Math.random() * 300 : 900 + Math.random() * 1100;
    // Well above the music: a clap that has to be listened for is not a clap. The master gain is
    // half, so these land at roughly 0.9 and 0.5 of full scale over a bed at 0.3.
    setTimeout(() => { if (storm) thunder(near ? 1.8 : 1.0); }, delay);

    scheduleStrike();
}

function stopStorm() {
    if (!storm) return;
    clearTimeout(storm.timer);
    storm.el.classList.remove('is-strike', 'is-sheet');
    storm = null;
}

function stopBed() {
    if (bed?.stop) {
        try { bed.stop(); } catch { /* nothing to stop */ }
    }
    bed = null;
}

function arm() {
    if (disarm) return;

    const go = async () => {
        // Read before disarming: disarmBed() clears `pending` too, and reading it afterwards is
        // how the bed queued behind the gesture used to be dropped on the floor by the gesture.
        const name = pending;
        disarmBed();
        await unlock();
        if (rainWanted) startRain();
        if (name) await setBed(name);
    };

    GESTURES.forEach(g => window.addEventListener(g, go, { once: true, passive: true }));
    disarm = () => GESTURES.forEach(g => window.removeEventListener(g, go));
}

export function disarmBed() {
    if (disarm) { disarm(); disarm = null; }
    pending = null;
}

export function dispose() {
    stopStorm();
    stopRain();
    rainWanted = false;
    stopBed();
    disarmBed();
    if (ctx) { try { ctx.close(); } catch { /* already closed */ } }
    ctx = null;
    master = null;
}
