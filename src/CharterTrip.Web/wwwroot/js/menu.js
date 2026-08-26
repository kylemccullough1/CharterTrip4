// Dragging a meal from one menu slot to another.
//
// Same shape as teams.js: pointer events so touch and mouse behave identically, and the module
// reports WHAT was dropped WHERE rather than deciding anything — C# owns what a move means
// (including the swap when the target slot is occupied). It also never writes to a style
// property Blazor renders — only `transform`, which Blazor never sets.

// When a press becomes a drag is drag-gesture.js's decision: on touch that means after a
// short hold, so a finger that only wants to scroll the page still can.
import * as gesture from './drag-gesture.js';

let state = null;

export function init(dotNetRef) {
    dispose();
    state = { ref: dotNetRef, drag: null, suppressClick: false, hovered: undefined };

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
    drag.meal.classList.remove('is-dragging');
    drag.meal.style.transform = '';
    state.drag = null;
}

function onPointerDown(event) {
    if (!state || state.drag || event.button !== 0) return;

    const meal = event.target.closest?.('.menu-meal');
    if (!meal) return;

    // Clicking into the inline editor and dragging the card are different intentions.
    if (event.target.closest?.('input, textarea, select, option, button, a')) return;

    state.drag = {
        // Set once the press has earned the right to move the card; until then this is
        // still a tap or a scroll, and the card must not have moved or been marked.
        gesture: gesture.begin(event, () => markDragging(state?.drag)),
        meal,
        dayId: meal.dataset.dayId,
        slot: meal.dataset.slot,
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
    drag.meal.classList.add('is-dragging');
}

function onPointerMove(event) {
    const drag = state?.drag;
    if (!drag) return;

    const verdict = gesture.classify(drag.gesture, event);

    // A finger that moved before the hold was scrolling all along. Drop the press and leave the
    // page to the browser — a card that was never marked has nothing to undo.
    if (verdict === 'abandon') {
        gesture.end(drag.gesture);
        state.drag = null;
        return;
    }

    if (verdict === 'wait') return;

    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;

    if (!drag.moved) {
        drag.moved = true;
        gesture.lockScroll(drag.gesture);
        markDragging(drag);
    }

    event.preventDefault();
    drag.meal.style.transform = `translate(${dx}px, ${dy}px)`;

    const target = slotUnder(event.clientX, event.clientY);
    const key = target ? `${target.dayId}|${target.slot}` : null;
    if (key !== state.hovered) {
        state.hovered = key;
        state.ref.invokeMethodAsync('MealHover', target?.dayId ?? null, target?.slot ?? null).catch(() => {});
    }
}

async function onPointerUp(event) {
    const drag = state?.drag;
    if (!drag) return;

    state.drag = null;
    gesture.end(drag.gesture);

    // A touch that held long enough to be marked but never moved lands here too, so clear
    // the mark before leaving.
    if (!drag.moved) {
        if (drag.marked) drag.meal.classList.remove('is-dragging');
        return;
    }

    drag.meal.classList.remove('is-dragging');
    drag.meal.style.transform = '';

    const target = slotUnder(event.clientX, event.clientY);
    state.hovered = undefined;
    state.ref.invokeMethodAsync('MealHover', null, null).catch(() => {});

    state.suppressClick = true;
    setTimeout(() => { if (state) state.suppressClick = false; }, 0);

    if (!target) return;
    if (target.dayId === drag.dayId && target.slot === drag.slot) return;

    try {
        await state.ref.invokeMethodAsync('MealDropped', drag.dayId, drag.slot, target.dayId, target.slot);
    } catch {
        // circuit went away mid-drop; the next render shows the truth
    }
}

/// The slot under the pointer, or null.
function slotUnder(x, y) {
    for (const el of document.elementsFromPoint(x, y)) {
        const zone = el.closest?.('[data-menu-slot]');
        if (zone) return { dayId: zone.dataset.menuDayId, slot: zone.dataset.menuSlot };
    }
    return null;
}

function onClick(event) {
    if (state?.suppressClick && event.target.closest?.('.menu-meal')) {
        event.stopPropagation();
        event.preventDefault();
    }
}
