// The Jeopardy soundtrack, synthesised.
//
// The real "Think!" theme is Merv Griffin's and cannot ship in this repo, so every cue here is
// generated with the Web Audio API instead — no files, nothing to download, works on house wifi
// with no internet. It is not the show's music, but it is the right shape: a ticking countdown,
// a buzz, a rising sting for right and a falling one for wrong.
//
// Browsers refuse to make noise until the user has interacted with the page, which is exactly
// what the start screen's button is for.

let ctx = null;
let master = null;
let think = null;      // the looping countdown, while a clue is up
let drone = null;      // the low bed under Final Jeopardy
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

/// One shaped note. Everything else is built out of these.
function note({ freq = 440, start = 0, dur = 0.2, type = 'sine', peak = 0.3, glideTo = null }) {
    const c = audio();
    const t = c.currentTime + start;

    const osc = c.createOscillator();
    const gain = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (glideTo) osc.frequency.exponentialRampToValueAtTime(glideTo, t + dur);

    // A short fade at each end; square waves click horribly without one.
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.012);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    osc.connect(gain).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.02);
}

// ---------------------------------------------------------------- cues

/// Buzz-in: a short, unmistakable blat.
export function buzz() {
    note({ freq: 300, dur: 0.20, type: 'square', peak: 0.28 });
    note({ freq: 200, dur: 0.22, type: 'square', peak: 0.20, start: 0.01 });
}

/// Right answer: a bright rising third.
export function correct() {
    [523.25, 659.25, 783.99, 1046.5].forEach((f, i) =>
        note({ freq: f, start: i * 0.09, dur: 0.26, type: 'triangle', peak: 0.26 }));
}

/// Wrong answer: the classic descending pair.
export function wrong() {
    note({ freq: 233.08, dur: 0.30, type: 'sawtooth', peak: 0.22 });
    note({ freq: 174.61, start: 0.16, dur: 0.42, type: 'sawtooth', peak: 0.22 });
}

/// A clue goes up on the board.
export function reveal() {
    note({ freq: 392, dur: 0.16, type: 'triangle', peak: 0.20 });
    note({ freq: 587.33, start: 0.08, dur: 0.22, type: 'triangle', peak: 0.20 });
}

/// The board is done.
export function fanfare() {
    [523.25, 523.25, 523.25, 698.46, 880].forEach((f, i) =>
        note({ freq: f, start: i * 0.14, dur: i === 4 ? 0.7 : 0.13, type: 'triangle', peak: 0.3 }));
}

/// Ticking under a live clue: an alternating two-note pulse that sits behind conversation.
export function startThink() {
    if (think) return;
    let step = 0;
    const tick = () => {
        note({ freq: step % 2 ? 330 : 392, dur: 0.11, type: 'sine', peak: 0.10 });
        step++;
    };
    tick();
    think = setInterval(tick, 620);
}

export function stopThink() {
    clearInterval(think);
    think = null;
}

/// A low bed for Final Jeopardy while everyone is writing.
export function startDrone() {
    if (drone) return;
    const c = audio();
    const osc = c.createOscillator();
    const gain = c.createGain();
    const lfo = c.createOscillator();
    const lfoGain = c.createGain();

    osc.type = 'sine';
    osc.frequency.value = 110;
    gain.gain.setValueAtTime(0.0001, c.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.14, c.currentTime + 1.2);

    lfo.frequency.value = 0.25;          // a slow swell, so it breathes
    lfoGain.gain.value = 0.05;
    lfo.connect(lfoGain).connect(gain.gain);

    osc.connect(gain).connect(master);
    osc.start();
    lfo.start();
    drone = { osc, lfo, gain };
}

export function stopDrone() {
    if (!drone) return;
    const c = audio();
    drone.gain.gain.exponentialRampToValueAtTime(0.0001, c.currentTime + 0.6);
    drone.osc.stop(c.currentTime + 0.7);
    drone.lfo.stop(c.currentTime + 0.7);
    drone = null;
}

export function dispose() {
    stopThink();
    stopDrone();
    if (ctx) { ctx.close(); ctx = null; master = null; }
}
