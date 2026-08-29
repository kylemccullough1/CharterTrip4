// Blood, from the letters.
//
// Drips drawn over a word are stuck on: whatever colour they are, the eye sees a shape sitting on
// the text. The two things that make blood look like it came from the letter are that it starts
// inside the ink and that it starts where there is ink. So the drips live in a layer behind the
// title — the pool at the top of each is hidden by the glyph, and only the run shows below it —
// and they are placed by rendering each word onto a canvas and finding the columns where a
// letter's foot actually touches the baseline. A drip never hangs from the gap in an M.
//
// Deterministic: the same title at the same size gets the same drips, seeded from the text, so
// a re-render for somebody walking in does not redraw the blood.

let current = null;     // { wrap, key }
let selector = null;
let resizeTimer = null;

export function bleed(sel) {
    selector = sel;

    if (!document.fonts || document.fonts.status === 'loaded') draw();
    else document.fonts.ready.then(draw);

    if (!resizeTimer) {
        window.addEventListener('resize', onResize);
        resizeTimer = true;
    }
}

function onResize() {
    clearTimeout(current?.timer);
    if (current) current.timer = setTimeout(draw, 150);
    else draw();
}

function draw() {
    const wrap = selector && document.querySelector(selector);
    if (!wrap) { current = null; return; }

    const layer = wrap.querySelector('.ms-drips-layer');
    const words = [...wrap.querySelectorAll('.ms-word')];
    if (!layer || words.length === 0) return;

    const size = parseFloat(getComputedStyle(words[0]).fontSize);
    const key = words.map(w => w.textContent).join(' ') + '|' + size + '|' + Math.round(wrap.getBoundingClientRect().width);
    if (current?.wrap === wrap && current.key === key) return;
    current = { wrap, key };

    layer.replaceChildren();
    const wrapRect = wrap.getBoundingClientRect();
    const rand = mulberry32(hash(key));

    words.forEach(word => {
        const rect = word.getBoundingClientRect();
        const baselineY = baselineOf(word) - wrapRect.top;
        const segments = feet(word, size);

        for (const seg of pick(segments, size, rand)) {
            const x = rect.left - wrapRect.left + seg.x;
            const drip = document.createElement('i');
            drip.style.left = `${x.toFixed(1)}px`;
            // Just inside the letter: the pool is behind the glyph, the run comes out under it.
            drip.style.top = `${(baselineY - size * 0.08).toFixed(1)}px`;
            drip.style.setProperty('--w', `${(size * (0.055 + rand() * 0.05)).toFixed(1)}px`);
            drip.style.setProperty('--l', `${(size * (0.22 + rand() * 0.5)).toFixed(0)}px`);
            const period = 5 + rand() * 6;
            drip.style.setProperty('--t', `${period.toFixed(1)}s`);
            drip.style.setProperty('--d', `${(-rand() * period).toFixed(1)}s`);
            layer.appendChild(drip);
        }
    });
}

/// Where the text sits. An empty inline-block has its baseline at its bottom edge.
function baselineOf(word) {
    const probe = document.createElement('span');
    probe.style.cssText = 'display:inline-block;width:0;height:0;vertical-align:baseline;';
    word.appendChild(probe);
    const y = probe.getBoundingClientRect().bottom;
    probe.remove();
    return y;
}

/**
 * The columns of a word where ink meets the baseline, as segments: one per serif foot, stem or
 * bowl that touches down. Rendered at the word's real font and size so the x values map onto
 * the element one for one.
 */
function feet(word, size) {
    const cs = getComputedStyle(word);
    const font = `${cs.fontStyle} ${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
    const text = word.textContent;

    const canvas = document.createElement('canvas');
    const c = canvas.getContext('2d', { willReadFrequently: true });
    c.font = font;
    const m = c.measureText(text);

    const pad = 2;
    const asc = Math.ceil(m.actualBoundingBoxAscent);
    const desc = Math.ceil(m.actualBoundingBoxDescent);
    canvas.width = Math.ceil(m.width) + pad * 2;
    canvas.height = asc + desc + pad * 2;

    c.font = font;
    c.fillStyle = '#fff';
    c.textBaseline = 'alphabetic';
    c.fillText(text, pad, asc + pad);

    const baseline = asc + pad;
    const { width: w, height: h } = canvas;
    const data = c.getImageData(0, 0, w, h).data;
    const tolerance = size * 0.06;

    const segments = [];
    let open = null;

    for (let x = 0; x < w; x++) {
        let lowest = -1;
        for (let y = h - 1; y >= 0; y--) {
            if (data[(y * w + x) * 4 + 3] > 96) { lowest = y; break; }
        }

        const foot = lowest >= 0 && Math.abs(lowest - baseline) <= tolerance;
        if (foot) {
            if (open) open.to = x; else open = { from: x, to: x };
        } else if (open) {
            segments.push(open);
            open = null;
        }
    }
    if (open) segments.push(open);

    // Back to the element's own coordinates.
    return segments.map(s => ({ from: s.from - pad, to: s.to - pad, x: 0 }));
}

/**
 * Which feet bleed. About half of them, never two closer than a third of an em, and always at
 * least one per word. A drip lands somewhere along its foot rather than dead centre.
 */
function pick(segments, size, rand) {
    const chosen = [];
    const gap = size * 0.34;
    let lastX = -Infinity;

    for (const seg of segments) {
        const width = seg.to - seg.from;
        if (width < size * 0.03) continue;
        if (rand() > 0.45) continue;

        const x = seg.from + width * (0.25 + rand() * 0.5);
        if (x - lastX < gap) continue;

        chosen.push({ ...seg, x });
        lastX = x;
    }

    if (chosen.length === 0 && segments.length > 0) {
        const widest = segments.reduce((a, b) => (b.to - b.from) > (a.to - a.from) ? b : a);
        chosen.push({ ...widest, x: (widest.from + widest.to) / 2 });
    }

    return chosen;
}

function hash(text) {
    let h = 2166136261;
    for (let i = 0; i < text.length; i++) { h ^= text.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h >>> 0;
}

function mulberry32(seed) {
    let a = seed;
    return () => {
        a |= 0; a = a + 0x6D2B79F5 | 0;
        let t = Math.imul(a ^ a >>> 15, 1 | a);
        t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
        return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
}

export function dispose() {
    window.removeEventListener('resize', onResize);
    resizeTimer = null;
    current = null;
    selector = null;
}
