// Drag and resize for the itinerary planner.
//
// This module is deliberately dumb: it tracks a pointer and reports PIXELS back to C#.
// It never works out what time a card landed on, because collapsed hour bands make that
// mapping non-linear and DayTimeline (which is unit tested) is the only thing that knows
// the band layout. Keeping the arithmetic on one side avoids two implementations drifting.
//
// Pointer events rather than HTML5 drag-and-drop, so touch works the same as mouse.

const DRAG_THRESHOLD_PX = 4;
const MIN_HEIGHT_PX = 18;

let state = null;

export function init(dotNetRef) {
    dispose();

    state = { ref: dotNetRef, drag: null, suppressClick: false };

    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('pointermove', onPointerMove, true);
    document.addEventListener('pointerup', onPointerUp, true);
    document.addEventListener('pointercancel', onPointerUp, true);
    document.addEventListener('click', onClick, true);
}

export function dispose() {
    document.removeEventListener('pointerdown', onPointerDown, true);
    document.removeEventListener('pointermove', onPointerMove, true);
    document.removeEventListener('pointerup', onPointerUp, true);
    document.removeEventListener('pointercancel', onPointerUp, true);
    document.removeEventListener('click', onClick, true);
    state = null;
}

function onPointerDown(event) {
    if (!state || state.drag || event.button !== 0) return;

    const handle = event.target.closest?.('[data-resize]');
    const card = event.target.closest?.('.planner-card');
    if (!card) return;

    // Let the editor's own controls behave normally.
    if (!handle && event.target.closest?.('button, input, select, textarea, a')) return;

    const lanes = card.closest('.planner-lanes');
    if (!lanes) return;

    const rect = card.getBoundingClientRect();

    state.drag = {
        mode: handle ? 'resize' : 'move',
        card,
        lanes,
        itemId: card.dataset.itemId,
        originHeight: card.offsetHeight,
        // Offsets from the pointer to the card's edges. Everything at drop time is derived from
        // the live pointer position plus these, never from a pixel value captured at pointerdown —
        // Blazor may re-render mid-drag and move the element out from under us.
        grabOffsetTop: event.clientY - rect.top,
        grabOffsetBottom: rect.bottom - event.clientY,
        startX: event.clientX,
        startY: event.clientY,
        moved: false
    };
}

function onPointerMove(event) {
    const drag = state?.drag;
    if (!drag) return;

    const dy = event.clientY - drag.startY;
    const dx = event.clientX - drag.startX;

    if (!drag.moved) {
        // A short press is a tap, not a drag — let it through so the editor still opens.
        if (Math.abs(dy) < DRAG_THRESHOLD_PX && Math.abs(dx) < DRAG_THRESHOLD_PX) return;
        drag.moved = true;
        drag.card.classList.add(drag.mode === 'resize' ? 'is-resizing' : 'is-dragging');
    }

    event.preventDefault();

    if (drag.mode === 'resize') {
        // Write a custom property that CSS applies only while .is-resizing, rather than
        // overwriting style.height. Blazor renders height in the style attribute and diffs
        // against its own last output, so anything JS clears there is gone for good.
        drag.card.style.setProperty('--drag-h', Math.max(MIN_HEIGHT_PX, drag.originHeight + dy) + 'px');
    } else {
        // Blazor never renders transform, so this one is ours to set and clear freely.
        drag.card.style.transform = `translate(${dx}px, ${dy}px)`;
    }
}

async function onPointerUp(event) {
    const drag = state?.drag;
    if (!drag) return;

    state.drag = null;

    // A plain click never set anything, so it must not clear anything either. This ran before
    // the check once, and every click on a card silently stripped its height.
    if (!drag.moved) return;

    drag.card.classList.remove('is-dragging', 'is-resizing');
    drag.card.style.transform = '';
    drag.card.style.removeProperty('--drag-h');

    // Swallow the click this drag is about to generate, or the editor pops open on every drop.
    state.suppressClick = true;
    setTimeout(() => { if (state) state.suppressClick = false; }, 0);

    try {
        if (drag.mode === 'resize') {
            // Where the card's bottom edge now sits, measured inside its day column.
            const laneTop = drag.lanes.getBoundingClientRect().top;
            const bottom = event.clientY + drag.grabOffsetBottom - laneTop;
            await state.ref.invokeMethodAsync('ItemResized', drag.itemId, bottom);
            return;
        }

        const target = laneUnder(event.clientX, event.clientY) ?? drag.card.closest('.planner-lanes');
        if (!target) return;

        // Dropped straight onto another card? Then the intent is "these two trade places",
        // not "stack them both at this minute". C# decides what swapping means.
        const onto = cardUnder(event.clientX, event.clientY, drag.card);

        const top = event.clientY - drag.grabOffsetTop - target.getBoundingClientRect().top;
        await state.ref.invokeMethodAsync(
            'ItemDropped', drag.itemId, target.dataset.dayId, top, onto?.dataset.itemId ?? null);
    } catch {
        // The circuit went away mid-drop; the next render will show the truth.
    }
}

/// The topmost card under the pointer, skipping the one being dragged.
function cardUnder(x, y, exclude) {
    for (const el of document.elementsFromPoint(x, y)) {
        const card = el.closest?.('.planner-card');
        if (card && card !== exclude) return card;
    }
    return null;
}

/// Which day column is under the pointer, ignoring the card being dragged.
function laneUnder(x, y) {
    const under = document.elementsFromPoint(x, y);
    for (const el of under) {
        const lane = el.closest?.('.planner-lanes');
        if (lane) return lane;
    }
    return null;
}

function onClick(event) {
    if (state?.suppressClick && event.target.closest?.('.planner-card')) {
        event.stopPropagation();
        event.preventDefault();
    }
}
