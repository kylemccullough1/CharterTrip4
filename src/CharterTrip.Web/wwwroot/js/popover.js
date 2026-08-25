// Keeps a small floating panel on screen.
//
// The carpool palette used to be absolutely positioned inside its row. That works right up until
// the row is near the bottom of a scrolling panel or the right edge of a narrow card, at which
// point the palette is clipped by the very container it lives in and half the colours become
// unreachable. CSS cannot see that coming — `overflow` clips unconditionally — so the palette is
// `position: fixed` and this puts it somewhere it fits.
//
// Measured after every render rather than only on open, because the page underneath can scroll
// or reflow while the palette is up and a stale position is worse than no position.

const MARGIN = 8;

/// Place the open palette against the dot that opened it, flipping when it would overflow.
export function placeCarPalette() {
    const pop = document.querySelector('[data-car-pop]');
    if (!pop) return;

    const anchor = document.querySelector(`[data-car-anchor="${CSS.escape(pop.dataset.carPop)}"]`);
    if (!anchor) return;

    // Clear any previous placement first: measuring while the old one still applies gives a size
    // for a box that has already been squeezed against an edge.
    pop.style.left = '0px';
    pop.style.top = '0px';

    const a = anchor.getBoundingClientRect();
    const p = pop.getBoundingClientRect();
    const vw = document.documentElement.clientWidth;
    const vh = document.documentElement.clientHeight;

    // Below the dot by default; above it when there is no room below but there is above.
    const below = a.bottom + MARGIN;
    const above = a.top - p.height - MARGIN;
    const top = (below + p.height <= vh - MARGIN || above < MARGIN) ? below : above;

    // Left-aligned to the dot, pulled back inside the viewport if that would overhang.
    const left = Math.max(MARGIN, Math.min(a.left, vw - p.width - MARGIN));

    pop.style.left = `${Math.round(left)}px`;
    pop.style.top = `${Math.round(Math.max(MARGIN, top))}px`;
}
