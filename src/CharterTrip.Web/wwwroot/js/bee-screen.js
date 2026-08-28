// The wall's full screen, and the one awkward fact about it: the bee is started from the host's
// phone, and a browser will not hand a page the whole display because something happened on a
// different device.
//
// So this does two things. It asks properly when asked — which works if the wall was touched
// recently — and it arms itself, so the very next click or key anywhere on the wall goes full
// screen instead of doing nothing. In practice somebody is standing at that laptop, and the
// first thing they do is the gesture.

let dotnet = null;
let armed = false;

export function isFull() {
    return !!document.fullscreenElement;
}

export async function enter() {
    if (document.fullscreenElement) return true;

    try {
        await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    } catch {
        // Refused because nothing has been touched yet. The arming below is the answer.
        return false;
    }

    return isFull();
}

export function exit() {
    try { document.exitFullscreen?.(); } catch { /* not ours to exit */ }
}

/// Whether the next stray gesture should be spent on going full screen.
export function arm(value) {
    armed = !!value;
}

/// Tell Blazor when this changes, so the "back to full screen" button can appear the moment
/// somebody presses Escape and disappear the moment they are back.
export function watch(ref) {
    dotnet = ref;
    document.addEventListener('fullscreenchange', changed);
    document.addEventListener('pointerdown', gesture, true);
    document.addEventListener('keydown', gesture, true);
    return isFull();
}

export function unwatch() {
    dotnet = null;
    document.removeEventListener('fullscreenchange', changed);
    document.removeEventListener('pointerdown', gesture, true);
    document.removeEventListener('keydown', gesture, true);
}

function changed() {
    try { dotnet?.invokeMethodAsync('FullScreenChanged', isFull()); } catch { /* circuit gone */ }
}

function gesture(e) {
    if (!armed || document.fullscreenElement) return;

    // The testing panel is a set of embedded phones somebody is deliberately poking at. Throwing
    // the laptop into full screen every time they tap one would make it unusable.
    if (e.target instanceof Element && e.target.closest('.bee-testing')) return;

    armed = false;
    enter();
}
