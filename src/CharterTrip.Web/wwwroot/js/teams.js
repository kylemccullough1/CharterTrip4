// Dragging a person from one team column to another.
//
// Same shape as planner.js: pointer events so touch and mouse behave identically, and the
// module reports WHAT was dropped WHERE rather than deciding anything. It also never writes to
// a style property Blazor renders — only `transform`, which Blazor never sets — because
// anything JS clears there is invisible to Blazor's diff and never comes back.

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
    drag.person.classList.remove('is-dragging');
    drag.person.style.transform = '';
    state.drag = null;
}

function onPointerDown(event) {
    if (!state || state.drag || event.button !== 0) return;

    const person = event.target.closest?.('.person');
    if (!person) return;

    // Team leads are fixed to their team; C# refuses the move anyway, but there is no point
    // letting a card be dragged around only to snap back.
    if (person.dataset.locked === 'true') return;

    // Never start a drag from a control or from the inline name editor — dragging and
    // selecting text inside an input are different intentions.
    if (event.target.closest?.('input, textarea, select, option, button, a')) return;

    state.drag = {
        // Set once the press has earned the right to move the card; until then this is
        // still a tap or a scroll, and the card must not have moved or been marked.
        gesture: gesture.begin(event, () => markDragging(state?.drag)),
        person,
        personId: person.dataset.personId,
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
    drag.person.classList.add('is-dragging');
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
    drag.person.style.transform = `translate(${dx}px, ${dy}px)`;

    const target = dropTargetUnder(event.clientX, event.clientY);
    if (target !== state.hovered) {
        state.hovered = target;
        state.ref.invokeMethodAsync('DropTargetChanged', target ?? null).catch(() => {});
    }
}

async function onPointerUp(event) {
    const drag = state?.drag;
    if (!drag) return;

    state.drag = null;
    gesture.end(drag.gesture);

    // A plain tap touched nothing, so it clears nothing. A touch that held long enough to be
    // marked but never moved lands here too, so clear the mark before leaving.
    if (!drag.moved) {
        if (drag.marked) drag.person.classList.remove('is-dragging');
        return;
    }

    drag.person.classList.remove('is-dragging');
    drag.person.style.transform = '';

    const target = state.hovered;
    state.hovered = undefined;
    state.ref.invokeMethodAsync('DropTargetChanged', null).catch(() => {});

    state.suppressClick = true;
    setTimeout(() => { if (state) state.suppressClick = false; }, 0);

    // undefined means the pointer was over nothing droppable — leave the person where they were.
    if (target === undefined) return;

    try {
        await state.ref.invokeMethodAsync('PersonDropped', drag.personId, target || null);
    } catch {
        // circuit went away mid-drop; the next render shows the truth
    }
}

/// The team id of the column under the pointer. "" is the unassigned tray; undefined is nothing.
function dropTargetUnder(x, y) {
    for (const el of document.elementsFromPoint(x, y)) {
        const zone = el.closest?.('[data-team-id]');
        if (zone) return zone.dataset.teamId;
    }
    return undefined;
}

function onClick(event) {
    if (state?.suppressClick && event.target.closest?.('.person')) {
        event.stopPropagation();
        event.preventDefault();
    }
}
