// The spelling bee's noises, synthesised.
//
// Same approach as jeopardy-audio.js and for the same reason — no files, nothing to download,
// works on house wifi with no internet — but its own module rather than more exports on that
// one. The two games are on screen at different times and share nothing but the technique, and
// a single module would mean the bee's page loading Jeopardy's four looping beds to use none
// of them.
//
// The one cue worth reading is strike(): a bowling ball is mostly noise, not pitch, so it is
// built out of filtered white noise rather than oscillators.

let ctx = null;
let master = null;
let muted = false;

function audio() {
    if (!ctx) {
        ctx = new (window.AudioContext || window.webkitAudioContext)();
        master = ctx.createGain();
        master.gain.value = 0.5;
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

/// One shaped note. A short fade at each end; square waves click horribly without one.
function note({ freq = 440, start = 0, dur = 0.2, type = 'sine', peak = 0.3, glideTo = null }) {
    const c = audio();
    const t = c.currentTime + start;

    const osc = c.createOscillator();
    const gain = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (glideTo) osc.frequency.exponentialRampToValueAtTime(glideTo, t + dur);

    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.012);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    osc.connect(gain).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.02);
}

/// A burst of filtered white noise. The buffer is built once and replayed, because generating
/// two seconds of random samples on every pin crash is real work on a laptop also running a
/// Blazor circuit.
let noiseBuffer = null;

function noise({ start = 0, dur = 0.4, peak = 0.3, from = 400, to = 400, q = 1, type = 'bandpass' }) {
    const c = audio();
    const t = c.currentTime + start;

    if (!noiseBuffer) {
        noiseBuffer = c.createBuffer(1, c.sampleRate * 2, c.sampleRate);
        const data = noiseBuffer.getChannelData(0);
        for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    }

    const src = c.createBufferSource();
    src.buffer = noiseBuffer;
    src.loop = true;

    const filter = c.createBiquadFilter();
    filter.type = type;
    filter.Q.value = q;
    filter.frequency.setValueAtTime(from, t);
    if (to !== from) filter.frequency.exponentialRampToValueAtTime(to, t + dur);

    const gain = c.createGain();
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    src.connect(filter).connect(gain).connect(master);
    src.start(t);
    src.stop(t + dur + 0.05);
}

// ---------------------------------------------------------------- cues

/// Somebody readied up on their phone. Deliberately small — twenty-five of these go off during
/// the lobby and any two of them may land together.
export function joined() {
    note({ freq: 880, dur: 0.06, type: 'triangle', peak: 0.16 });
    note({ freq: 1318.5, start: 0.05, dur: 0.10, type: 'triangle', peak: 0.13 });
}

/// The next speller is called. A two-note "you're up".
export function up() {
    note({ freq: 587.33, dur: 0.12, type: 'triangle', peak: 0.20 });
    note({ freq: 880, start: 0.10, dur: 0.18, type: 'triangle', peak: 0.20 });
}

/// Spelled it. A bright rising run.
export function correct() {
    [523.25, 659.25, 783.99, 1046.5].forEach((f, i) =>
        note({ freq: f, start: i * 0.08, dur: 0.24, type: 'triangle', peak: 0.26 }));
}

/// The bowling alley. Roll, crash, and the rumble after.
///
/// Timed against the CSS: the ball takes about 900ms to cross the screen, so the rumble runs
/// under it and the crash lands where the pins actually scatter.
export function strike() {
    // The ball down the lane — a low rolling rumble that gets closer.
    noise({ dur: 0.9, peak: 0.16, from: 90, to: 220, q: 0.7, type: 'lowpass' });
    note({ freq: 55, dur: 0.9, type: 'sine', peak: 0.14, glideTo: 90 });

    // Impact.
    noise({ start: 0.88, dur: 0.55, peak: 0.42, from: 2600, to: 500, q: 0.6, type: 'highpass' });
    note({ freq: 160, start: 0.88, dur: 0.35, type: 'square', peak: 0.20, glideTo: 60 });

    // Pins clattering off each other afterwards, thinning out.
    for (let i = 0; i < 7; i++) {
        noise({
            start: 0.95 + Math.random() * 0.5,
            dur: 0.09,
            peak: 0.16 - i * 0.015,
            from: 1400 + Math.random() * 2200,
            q: 4
        });
    }
}

/// The field comes back. Deliberately warm and open rather than triumphant — nobody won
/// anything, the game just refused to end — and nothing like the crash, because the whole point
/// of this cue is that the room can hear it is not an elimination.
export function revival() {
    // A rising open fifth under a soft bloom, four voices arriving one after another the way the
    // faces do on the wall.
    [261.63, 392.00, 523.25, 784.00].forEach((f, i) =>
        note({ freq: f, start: i * 0.13, dur: 0.9 - i * 0.1, type: 'sine', peak: 0.20 }));

    note({ freq: 130.81, dur: 1.1, type: 'triangle', peak: 0.14 });
}

/// The bee is over. Longer and brassier than anything else here, because this plays once.
export function fanfare() {
    const run = [392.00, 523.25, 659.25, 783.99, 1046.50];
    run.forEach((f, i) =>
        note({ freq: f, start: i * 0.11, dur: 0.22, type: 'triangle', peak: 0.30 }));

    const hit = run.length * 0.11;
    [523.25, 659.25, 783.99, 1046.50].forEach(f =>
        note({ freq: f, start: hit, dur: 1.5, type: 'triangle', peak: 0.20 }));

    note({ freq: 130.81, start: hit, dur: 1.6, type: 'sine', peak: 0.22 });
}

// ------------------------------------------------------- saying the word

/// Read something out, in the host's own phone's voice.
///
/// The bee's real problem is not that the room cannot hear the word — it is that the host cannot
/// pronounce half of the Expert tier on sight. This is the browser's own speech synthesiser, so
/// there is no service to call and no key to hold, and it keeps working at a venue with no
/// internet: the voices are installed on the phone.
///
/// Deliberately slower than default and pitched flat. A pronouncer says the word twice, slowly,
/// and this is standing in for one.
export function say(text, rate = 0.85) {
    if (!text || !('speechSynthesis' in window)) return false;

    // Whatever is still being read is the previous word, and nobody wants to hear the end of it.
    window.speechSynthesis.cancel();

    const utterance = new SpeechSynthesisUtterance(String(text));
    utterance.rate = rate;
    utterance.pitch = 1;
    utterance.lang = 'en-US';
    window.speechSynthesis.speak(utterance);
    return true;
}

/// Whether this device can say anything at all, so a button that would do nothing is not drawn.
export function canSpeak() {
    return 'speechSynthesis' in window;
}

export function hush() {
    if ('speechSynthesis' in window) window.speechSynthesis.cancel();
}

export function dispose() {
    if (ctx) { ctx.close(); ctx = null; master = null; noiseBuffer = null; }
}
