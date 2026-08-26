// Drag and resize for the itinerary planner.
//
// This module is deliberately dumb: it tracks a pointer and reports PIXELS back to C#.
// It never works out what time a card landed on, because collapsed hour bands make that
// mapping non-linear and DayTimeline (which is unit tested) is the only thing that knows
// the band layout. Keeping the arithmetic on one side avoids two implementations drifting.
//
// Pointer events rather than HTML5 drag-and-drop, so touch works the same as mouse. When a
// press becomes a drag is drag-gesture.js's decision, not this module's — on touch that means
// after a short hold, so a finger that just wants to scroll the day still can.

import * as gesture from './drag-gesture.js';

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
    abandonDrag();
    state = null;
}

/// Put a half-finished drag back the way it was found. Navigating away mid-drag otherwise
/// strands three things: the pending hold timer, the document-level scroll lock (which would
/// block touch scrolling everywhere until a reload), and the card's own is-dragging class —
/// and that class carries touch-action: none, so the card itself would never scroll again.
function abandonDrag() {
    const drag = state?.drag;
    if (!drag) return;

    gesture.end(drag.gesture);
    drag.card.classList.remove('is-dragging', 'is-resizing');
    drag.card.style.transform = '';
    drag.card.style.removeProperty('--drag-h');
    state.drag = null;
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
        // Set once the press has earned the right to move the card; until then this is still
        // a tap or a scroll, and the card must not have moved or been marked.
        gesture: gesture.begin(event, () => markDragging(state?.drag)),
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

/// Show the card as picked up. Called on the hold for touch, on the first real movement for
/// mouse and pen — the two paths differ only in when, so they share the marking.
function markDragging(drag) {
    if (!drag || drag.marked) return;
    drag.marked = true;
    drag.card.classList.add(drag.mode === 'resize' ? 'is-resizing' : 'is-dragging');
}

function onPointerMove(event) {
    const drag = state?.drag;
    if (!drag) return;

    const verdict = gesture.classify(drag.gesture, event);

    // A finger that moved before the hold was scrolling all along. Drop the press entirely and
    // leave the page to the browser — a card that was never marked has nothing to undo.
    if (verdict === 'abandon') {
        gesture.end(drag.gesture);
        state.drag = null;
        return;
    }

    // A short press is a tap, not a drag — let it through so the editor still opens.
    if (verdict === 'wait') return;

    const dy = event.clientY - drag.startY;
    const dx = event.clientX - drag.startX;

    if (!drag.moved) {
        drag.moved = true;
        gesture.lockScroll(drag.gesture);
        markDragging(drag);
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
    gesture.end(drag.gesture);

    // A plain click never set anything, so it must not clear anything either. This ran before
    // the check once, and every click on a card silently stripped its height. A touch that held
    // long enough to be marked but never moved lands here too, so clear the mark before leaving.
    if (!drag.moved) {
        if (drag.marked) drag.card.classList.remove('is-dragging', 'is-resizing');
        return;
    }

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
