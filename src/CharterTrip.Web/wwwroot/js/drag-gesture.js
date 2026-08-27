// Telling "I want to drag this card" apart from "I want to scroll past it", on a touch screen.
//
// The three boards below (schedule, menu, teams) are all mostly cards, so on a phone a finger
// landing on a card is the common case, not the exception — and both gestures start out
// identical: one finger, pressed down, about to move vertically.
//
// The old answer was `touch-action: none` on the card, which told the browser to keep its hands
// off every gesture starting there. That does stop the browser stealing a drag, but it also
// means the page cannot be scrolled from anywhere a card is, and on a full schedule day that is
// nearly everywhere. The only place left to scroll from was the hour gutter down the side.
//
// It has to be `touch-action`, too: preventDefault() on a *pointer* event does not stop
// scrolling. A pointer event's default action is governed by touch-action alone, so the
// preventDefault() calls in the three drag modules were never what was holding the page still.
//
// So instead we split by intent, the way phone home screens have always done it: a finger that
// moves straight away is scrolling, and a finger that stays put is grabbing. Mouse and pen have
// a hover state, a visible cursor and no scroll conflict, so they keep the immediate threshold.

/// Movement past this, once the gesture is a drag, means the card is actually moving.
const DRAG_THRESHOLD_PX = 4;

/// How long a finger must stay put before it is a grab rather than a scroll.
const HOLD_MS = 400;

/// A finger drifting more than this during the hold was always going to be a scroll.
const HOLD_SLOP_PX = 10;

// One shared touchmove listener, mounted only while a touch drag is live — see lockScroll.
let scrollLocks = 0;
function blockScroll(event) { event.preventDefault(); }

/// Start tracking a press. `onHold` fires when a touch has stayed still long enough to be a grab.
export function begin(event, onHold) {
    const touch = event.pointerType === 'touch';

    const gesture = {
        touch,
        startX: event.clientX,
        startY: event.clientY,
        moved: false,
        // Mouse and pen are a drag from the first pixel; touch has to earn it.
        ready: !touch,
        hold: 0,
        locked: false
    };

    if (touch) {
        gesture.hold = setTimeout(() => {
            gesture.hold = 0;
            gesture.ready = true;
            // The finger has not moved, so the browser has not committed to a scroll yet and
            // this listener still gets to cancel one. Arriving any later than the hold — on the
            // first move, say — would be too late.
            lockScroll(gesture);
            onHold?.();
        }, HOLD_MS);
    }

    return gesture;
}

/// What a pointermove means: keep waiting, start/continue the drag, or let the browser scroll.
export function classify(gesture, event) {
    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    const travelled = Math.max(Math.abs(dx), Math.abs(dy));

    if (!gesture.ready) {
        // Moved before the hold finished. That is a scroll, and the browser is already doing it.
        return travelled >= HOLD_SLOP_PX ? 'abandon' : 'wait';
    }

    if (!gesture.moved && travelled < DRAG_THRESHOLD_PX) return 'wait';

    // Latch it: the threshold decides whether a drag has begun, not whether it is still going.
    // Without this, dragging a card back to within a few pixels of where it started would read
    // as 'wait' again and the card would stall under the finger.
    gesture.moved = true;
    return 'drag';
}

/// Stop the browser scrolling underneath a touch drag. No-op for mouse and pen.
export function lockScroll(gesture) {
    if (!gesture.touch || gesture.locked) return;
    gesture.locked = true;
    if (scrollLocks++ === 0) {
        document.addEventListener('touchmove', blockScroll, { passive: false });
    }
}

/// Release everything a gesture is holding. Safe to call more than once, on a gesture that never
/// became a drag, and on nothing at all — every exit path runs through here, including dispose().
export function end(gesture) {
    if (!gesture) return;

    if (gesture.hold) {
        clearTimeout(gesture.hold);
        gesture.hold = 0;
    }

    if (gesture.locked) {
        gesture.locked = false;
        if (--scrollLocks === 0) {
            document.removeEventListener('touchmove', blockScroll, { passive: false });
        }
    }
}
