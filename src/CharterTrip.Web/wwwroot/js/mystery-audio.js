// Braun Manor, synthesised.
//
// Same shape as jeopardy-audio.js and for the same reason: no files to download, nothing to go
// wrong on house wifi with no internet. One looping bed at a time, named by the phase, plus a
// handful of one-shots layered over it.
//
// The one exception is the scream. Synthesised screams sound like a theremin, and the murder is
// the single biggest moment of the night — so if wwwroot/audio/scream.mp3 exists it is used, and
// if it does not the synth stands in. The build never depends on the recording existing, which
// means somebody can record it on a phone the afternoon of the party and drop it in.

let ctx = null;
let master = null;
let muted = false;

let bed = null;        // { name, stop }
let pending = null;    // the bed we want as soon as the browser lets us make noise
let disarm = null;     // removes the gesture listener waiting on that permission

let screamBuffer = null;
let screamChecked = false;

const GESTURES = ['pointerdown', 'keydown', 'touchstart'];

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

/// Three layers, because one is a hiss and two is a door slamming.
export function thunder() {
    // The crack: bright, brief, and the thing that makes people jump.
    noise({ dur: 0.07, peak: 0.5, type: 'highpass', from: 3000, to: 6000, q: 0.5 });

    // The body: a long sweep down into the floor.
    noise({ start: 0.03, dur: 2.6, peak: 0.42, type: 'lowpass', from: 4000, to: 80 });

    // The floor itself. On a phone speaker this is felt more than heard, which is the point.
    note({ freq: 42, start: 0.02, dur: 3.0, type: 'sine', peak: 0.34 });
}

/// A door, a chair going over, something heavy.
export function crash() {
    noise({ dur: 0.5, peak: 0.4, type: 'lowpass', from: 1800, to: 120 });
    note({ freq: 70, dur: 0.4, type: 'triangle', peak: 0.2, glideTo: 38 });
}

/**
 * The scream.
 *
 * Tries the recording first. Falls back to two detuned saws through a bandpass with a ragged
 * decay — which is not a person, but is unmistakably a person's worst moment, and it is better
 * than silence at the one instant everybody is listening.
 */
export async function scream() {
    const buffer = await loadScream();

    if (buffer) {
        const c = audio();
        const src = c.createBufferSource();
        src.buffer = buffer;
        src.connect(master);
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

async function loadScream() {
    if (screamChecked) return screamBuffer;
    screamChecked = true;

    try {
        const response = await fetch('/audio/scream.mp3', { cache: 'force-cache' });
        if (!response.ok) return null;

        const bytes = await response.arrayBuffer();
        screamBuffer = await audio().decodeAudioData(bytes);
    } catch {
        // No recording, or it will not decode. The synth covers it.
        screamBuffer = null;
    }

    return screamBuffer;
}

/// The whole murder, in order, so the page fires one thing rather than three on timers.
export async function murder() {
    thunder();
    setTimeout(() => crash(), 260);
    setTimeout(() => scream(), 520);
    setTimeout(() => thunder(), 1900);
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

    /// A slow heartbeat under the investigation. Two thuds, a long gap, again.
    heart() {
        const timer = setInterval(() => {
            note({ freq: 54, dur: 0.22, type: 'sine', peak: 0.22 });
            setTimeout(() => note({ freq: 48, dur: 0.26, type: 'sine', peak: 0.16 }), 260);
        }, 1400);

        return () => clearInterval(timer);
    },

    /// The trial: a tick, slower than a clock, so it reads as patience rather than pressure.
    trial() {
        const timer = setInterval(() => {
            noise({ dur: 0.05, peak: 0.12, type: 'highpass', from: 2200, to: 3000 });
        }, 1000);

        return () => clearInterval(timer);
    }
};

function stopBed() {
    if (bed?.stop) {
        try { bed.stop(); } catch { /* nothing to stop */ }
    }
    bed = null;
}

function arm() {
    if (disarm) return;

    const go = async () => {
        disarmBed();
        if (pending) {
            const name = pending;
            pending = null;
            await unlock();
            await setBed(name);
        }
    };

    GESTURES.forEach(g => window.addEventListener(g, go, { once: true, passive: true }));
    disarm = () => GESTURES.forEach(g => window.removeEventListener(g, go));
}

export function disarmBed() {
    if (disarm) { disarm(); disarm = null; }
    pending = null;
}

export function dispose() {
    stopBed();
    disarmBed();
    if (ctx) { try { ctx.close(); } catch { /* already closed */ } }
    ctx = null;
    master = null;
}
