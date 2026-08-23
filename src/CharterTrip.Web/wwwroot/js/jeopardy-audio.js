// The Jeopardy soundtrack, synthesised.
//
// The real "Think!" theme is Merv Griffin's and cannot ship in this repo, so every cue here is
// generated with the Web Audio API instead — no files, nothing to download, works on house wifi
// with no internet. It is not the show's music, but it is the right shape: a ticking countdown,
// a buzz, a rising sting for right and a falling one for wrong.
//
// Browsers refuse to make noise until the user has interacted with the page, so the join
// screen's lobby vamp waits for the first gesture anywhere rather than for one particular click.

let ctx = null;
let master = null;
let think = null;      // the looping countdown, while a clue is up
let drone = null;      // the low bed under Final Jeopardy
let lobby = null;      // the vamp on the join screen, while phones are connecting
let disarm = null;     // removes the gesture listener waiting to start the lobby vamp
let muted = false;

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

/// The join screen, while people are scanning codes. A four-bar lounge vamp: quiet enough to
/// talk over, repetitive enough that nobody listens to it. Not the show's music — see the note
/// at the top of this file.
export function startLobby() {
    if (lobby) return;

    // Am7 - Dm7 - G7 - Cmaj7. Bass on the downbeat, the chord rolled in over the bar.
    const bars = [
        { bass: 110.00, chord: [261.63, 329.63, 392.00] },
        { bass: 146.83, chord: [293.66, 349.23, 440.00] },
        { bass: 196.00, chord: [293.66, 349.23, 493.88] },
        { bass: 130.81, chord: [261.63, 329.63, 493.88] },
    ];
    const beat = 0.42;
    let bar = 0;

    const play = () => {
        const { bass, chord } = bars[bar++ % bars.length];
        note({ freq: bass, dur: beat * 1.7, type: 'triangle', peak: 0.09 });
        chord.forEach((f, i) =>
            note({ freq: f, start: beat * 0.5 * (i + 1), dur: beat * 0.9, type: 'sine', peak: 0.055 }));
    };

    play();
    lobby = setInterval(play, beat * 2 * 1000);
}

export function stopLobby() {
    clearInterval(lobby);
    lobby = null;
}

/// Start the lobby vamp as soon as the browser will allow it.
///
/// No sound is permitted until someone has touched the page, and the join screen has nothing
/// anyone needs to click — so rather than putting up a "play music" button nobody asked for,
/// this waits for the first gesture anywhere on the page, whatever it happens to be.
/// Returns true if the music is already playing.
export async function armLobby() {
    const c = audio();

    if (c.state === 'suspended') await c.resume();
    if (c.state === 'running') { startLobby(); return true; }

    if (disarm) return false;

    const go = async () => {
        await c.resume();
        if (c.state !== 'running') return;
        disarmLobby();
        startLobby();
    };
    for (const e of GESTURES) window.addEventListener(e, go);
    disarm = () => { for (const e of GESTURES) window.removeEventListener(e, go); };

    return false;
}

/// Stop waiting for a gesture. Leaving the listener attached would outlive the page, since a
/// Blazor navigation keeps this module loaded.
export function disarmLobby() {
    disarm?.();
    disarm = null;
}

export function dispose() {
    disarmLobby();
    stopThink();
    stopDrone();
    stopLobby();
    if (ctx) { ctx.close(); ctx = null; master = null; }
}
