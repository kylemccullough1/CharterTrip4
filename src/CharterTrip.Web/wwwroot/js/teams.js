// Dragging a person from one team column to another.
//
// Same shape as planner.js: pointer events so touch and mouse behave identically, and the
// module reports WHAT was dropped WHERE rather than deciding anything. It also never writes to
// a style property Blazor renders — only `transform`, which Blazor never sets — because
// anything JS clears there is invisible to Blazor's diff and never comes back.

const DRAG_THRESHOLD_PX = 4;

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
    state = null;
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
        person,
        personId: person.dataset.personId,
        startX: event.clientX,
        startY: event.clientY,
        moved: false
    };
}

function onPointerMove(event) {
    const drag = state?.drag;
    if (!drag) return;

    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;

    if (!drag.moved) {
        if (Math.abs(dx) < DRAG_THRESHOLD_PX && Math.abs(dy) < DRAG_THRESHOLD_PX) return;
        drag.moved = true;
        drag.person.classList.add('is-dragging');
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

    if (!drag.moved) return;               // a plain tap touched nothing, so clear nothing

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
