// Dragging a meal from one menu slot to another.
//
// Same shape as teams.js: pointer events so touch and mouse behave identically, and the module
// reports WHAT was dropped WHERE rather than deciding anything — C# owns what a move means
// (including the swap when the target slot is occupied). It also never writes to a style
// property Blazor renders — only `transform`, which Blazor never sets.

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

    const meal = event.target.closest?.('.menu-meal');
    if (!meal) return;

    // Clicking into the inline editor and dragging the card are different intentions.
    if (event.target.closest?.('input, textarea, select, option, button, a')) return;

    state.drag = {
        meal,
        dayId: meal.dataset.dayId,
        slot: meal.dataset.slot,
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
        drag.meal.classList.add('is-dragging');
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
    if (!drag.moved) return;

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
