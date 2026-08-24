// The Jeopardy soundtrack, synthesised.
//
// The real "Think!" theme is Merv Griffin's and cannot ship in this repo, so every cue here is
// generated with the Web Audio API instead — no files, nothing to download, works on house wifi
// with no internet. It is not the show's music, but it is the right shape: a ticking countdown,
// a buzz, a rising sting for right and a falling one for wrong.
//
// Browsers refuse to make noise until the user has interacted with the page, so the join
// screen's vamp waits for the first gesture anywhere rather than for one particular click.

let ctx = null;
let master = null;
let muted = false;

// Exactly one looping bed plays at a time, and this is it. Every phase of the game names the
// bed it wants and this switches to it — which is the only way the layering stays honest, since
// the alternative was a start/stop call per bed at every transition and one forgotten `stop`
// meant two pieces of music playing over each other.
let bed = null;        // { name, timer }
let pending = null;    // the bed we want as soon as the browser lets us make noise
let disarm = null;     // removes the gesture listener waiting on that permission

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

/// Choosing a clue off the board. A hard, short tick — the sound of a tile being taken, played
/// on whichever device did the choosing so the tap has something to answer it.
export function pick() {
    note({ freq: 880, dur: 0.045, type: 'square', peak: 0.20 });
    note({ freq: 1320, start: 0.03, dur: 0.06, type: 'triangle', peak: 0.14 });
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

/// Somebody won the whole thing. Longer and brassier than the fanfare, because this one plays
/// once all night and the room is looking at the screen when it does.
export function winJingle() {
    // Rising run into a held major chord, with the octave stacked on top of the last hit.
    const run = [392.00, 523.25, 659.25, 783.99, 1046.50];
    run.forEach((f, i) =>
        note({ freq: f, start: i * 0.11, dur: 0.22, type: 'triangle', peak: 0.30 }));

    const hit = run.length * 0.11;
    [523.25, 659.25, 783.99, 1046.50].forEach(f =>
        note({ freq: f, start: hit, dur: 1.5, type: 'triangle', peak: 0.20 }));

    // A low root underneath so the chord lands rather than chimes.
    note({ freq: 130.81, start: hit, dur: 1.6, type: 'sine', peak: 0.22 });

    // Two grace notes on the way out, the way a game show stinger tags itself.
    note({ freq: 1318.51, start: hit + 0.62, dur: 0.16, type: 'triangle', peak: 0.16 });
    note({ freq: 1567.98, start: hit + 0.78, dur: 0.40, type: 'triangle', peak: 0.18 });
}

// ---------------------------------------------------------------- beds

// Each bed is a function that plays its first bar and returns the interval keeping it going.
// They are deliberately different in register and pace, because the whole point is that you can
// tell where the game is with your back to the screen.

/// The join screen, while people scan codes. A lounge vamp — quiet enough to talk over,
/// repetitive enough that nobody listens to it. Not the show's music; see the note up top.
function vampBed() {
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
    return setInterval(play, beat * 2 * 1000);
}

/// The board is up and somebody is choosing. Warmer and slower than the clue bed — this is the
/// part of the game where the room is arguing about what to pick, so it stays out of the way.
function boardBed() {
    // Cmaj7 - Am7 - Fmaj7 - G. A walking root with a soft two-note answer over the top.
    const bars = [
        { bass: 130.81, reply: [329.63, 392.00] },
        { bass: 110.00, reply: [329.63, 440.00] },
        { bass: 174.61, reply: [349.23, 440.00] },
        { bass: 196.00, reply: [392.00, 493.88] },
    ];
    const beat = 0.5;
    let bar = 0;

    const play = () => {
        const { bass, reply } = bars[bar++ % bars.length];
        note({ freq: bass, dur: beat * 1.5, type: 'triangle', peak: 0.085 });
        note({ freq: bass * 2, start: beat * 0.5, dur: beat * 0.8, type: 'sine', peak: 0.04 });
        reply.forEach((f, i) =>
            note({ freq: f, start: beat * (1 + i * 0.5), dur: beat * 0.7, type: 'sine', peak: 0.05 }));
    };

    play();
    return setInterval(play, beat * 2 * 1000);
}

/// A clue is on the wall and the buzzers are live.
///
/// Built around the original countdown tick rather than replacing it — the alternating two-note
/// pulse is the bit that reads as "answer now", so it stays exactly as it was and the music is
/// assembled underneath it: a low pulse every other tick, and a minor chord that turns over the
/// bar so the pressure keeps climbing instead of sitting still.
function questionBed() {
    const tick = 0.62;                       // the countdown, unchanged
    const chords = [
        { root: 110.00, tones: [261.63, 329.63] },   // Am
        { root: 110.00, tones: [261.63, 329.63] },
        { root: 146.83, tones: [293.66, 349.23] },   // Dm
        { root: 164.81, tones: [329.63, 415.30] },   // E — leaves it unresolved
    ];
    let step = 0;

    const play = () => {
        // The tick itself.
        note({ freq: step % 2 ? 330 : 392, dur: 0.11, type: 'sine', peak: 0.10 });

        const { root, tones } = chords[Math.floor(step / 2) % chords.length];

        // A low pulse every other tick, so the bar has a floor.
        if (step % 2 === 0)
            note({ freq: root, dur: tick * 0.9, type: 'triangle', peak: 0.11 });

        // The chord turns over every four ticks.
        if (step % 4 === 0)
            tones.forEach((f, i) =>
                note({ freq: f, start: 0.05 + i * 0.05, dur: tick * 1.4, type: 'sine', peak: 0.045 }));

        step++;
    };

    play();
    return setInterval(play, tick * 1000);
}

/// Final Jeopardy.
///
/// This used to be a 110 Hz sine under a slow LFO — technically "tense", actually just ominous,
/// and it made the last clue of a party game feel like a horror film. It is now the fastest thing
/// in the set: a driving eighth-note bass under a climbing arpeggio, minor enough to feel like
/// the finale and quick enough to feel like a race.
function finalBed() {
    // Am - F - G - Am, two beats each. The arpeggio climbs across the bar so it never settles.
    const bars = [
        { bass: 110.00, arp: [220.00, 261.63, 329.63, 440.00] },
        { bass: 87.31, arp: [174.61, 220.00, 261.63, 349.23] },
        { bass: 98.00, arp: [196.00, 246.94, 293.66, 392.00] },
        { bass: 110.00, arp: [220.00, 261.63, 329.63, 523.25] },
    ];
    const beat = 0.30;                   // ~200 bpm eighths: quick, but not comical
    let bar = 0;

    const play = () => {
        const { bass, arp } = bars[bar++ % bars.length];

        // Bass on every eighth — this is what makes it drive rather than float.
        for (let i = 0; i < 4; i++)
            note({ freq: bass, start: beat * i, dur: beat * 0.55, type: 'triangle', peak: 0.16 });

        arp.forEach((f, i) =>
            note({ freq: f, start: beat * i + beat * 0.5, dur: beat * 0.7, type: 'square', peak: 0.055 }));
    };

    play();
    return setInterval(play, beat * 4 * 1000);
}

const BEDS = { vamp: vampBed, board: boardBed, question: questionBed, final: finalBed };

function stopBed() {
    if (!bed) return;
    clearInterval(bed.timer);
    bed = null;
}

function playBed(name) {
    // Already running: leave it alone rather than restarting it mid-bar. A phase that comes back
    // to the same bed — a wrong answer reopening a clue, say — should not stutter the music.
    if (bed?.name === name) return;

    stopBed();
    if (name && BEDS[name]) bed = { name, timer: BEDS[name]() };
}

/// Switch to the bed a phase wants, or pass null for silence.
///
/// Browsers refuse to make noise until the page has been touched, and the join screen has
/// nothing anyone needs to click — so rather than putting up a "play music" button nobody asked
/// for, an unplayable request is remembered and started on the first gesture, whatever it is.
export async function setBed(name) {
    const c = audio();
    if (c.state === 'suspended') await c.resume();

    if (c.state === 'running') {
        disarmBed();
        playBed(name);
        return true;
    }

    pending = name;
    arm();
    return false;
}

function arm() {
    if (disarm) return;

    const go = async () => {
        const c = audio();
        await c.resume();
        if (c.state !== 'running') return;
        disarmBed();
        playBed(pending);
    };

    for (const e of GESTURES) window.addEventListener(e, go);
    disarm = () => { for (const e of GESTURES) window.removeEventListener(e, go); };
}

/// Stop waiting for a gesture. Leaving the listener attached would outlive the page, since a
/// Blazor navigation keeps this module loaded.
export function disarmBed() {
    disarm?.();
    disarm = null;
    pending = null;
}

export function dispose() {
    disarmBed();
    stopBed();
    if (ctx) { ctx.close(); ctx = null; master = null; }
}
