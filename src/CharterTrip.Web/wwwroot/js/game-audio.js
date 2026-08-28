// Cues for the games played on their feet.
//
// Synthesised rather than sampled, for the same reason the Jeopardy soundtrack is: no files to
// ship, nothing to download, and it works on house wifi with no internet. This is deliberately
// its own small module rather than an import of jeopardy-audio.js — that one owns a looping
// soundtrack and a mute switch tied to a board, and none of that belongs on a page whose only
// job is to go "ding" when a team gets one right.
//
// Browsers refuse to make noise until the page has been touched. By the time anybody scores a
// round the host has tapped a great deal, so there is no gesture-waiting dance here.

let ctx = null;
let master = null;

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

/// One shaped note. A short fade at each end, or it clicks.
function note({ freq = 440, start = 0, dur = 0.2, type = 'sine', peak = 0.3 }) {
    const c = audio();
    const t = c.currentTime + start;

    const osc = c.createOscillator();
    const gain = c.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);

    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(peak, t + 0.012);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);

    osc.connect(gain).connect(master);
    osc.start(t);
    osc.stop(t + dur + 0.02);
}

/// A team got the character. A bright arpeggio with a sparkle scattered over the top of it —
/// the stars on screen, in sound.
export function sparkle() {
    // The chime underneath: a rising major triad, quick enough to feel like a reward and not a
    // fanfare. This one plays five or six times a game, so it stays short.
    [659.25, 987.77, 1318.51].forEach((f, i) =>
        note({ freq: f, start: i * 0.07, dur: 0.30, type: 'triangle', peak: 0.26 }));

    // The glitter: high, quiet, slightly irregular, so it twinkles rather than arpeggiates.
    const twinkles = [2093.0, 2637.0, 3136.0, 2349.3, 3520.0, 2793.8];
    twinkles.forEach((f, i) =>
        note({ freq: f, start: 0.12 + i * 0.055, dur: 0.16, type: 'sine', peak: 0.085 }));
}

/// A new round opens. One soft, low knock — enough to look up at, nothing like the scoring cue.
export function roundStart() {
    note({ freq: 196.00, dur: 0.20, type: 'triangle', peak: 0.16 });
    note({ freq: 293.66, start: 0.10, dur: 0.26, type: 'triangle', peak: 0.14 });
}

export function dispose() {
    if (ctx) { ctx.close(); ctx = null; master = null; }
}
