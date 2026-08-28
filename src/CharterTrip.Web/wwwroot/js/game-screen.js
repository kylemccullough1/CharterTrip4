// What a screen does when it stops being a website.
//
// Two jobs, and keeping them apart is the whole design. `setMode` puts a class on <html>, which is
// what actually hides the chrome — it always works, everywhere, including on an iPhone. `enter`
// asks for real full screen, which is a nicety the browser may refuse and may take back the moment
// somebody's thumb finds Escape. Nothing that matters is allowed to depend on the second one.
//
// Was bee-screen.js, which knew about the spelling bee. It does not any more: the one selector it
// still names, `.game-testing`, is the class every game's testing panel now carries.

let dotnet = null;
let armed = false;

export function isFull() {
    return !!document.fullscreenElement;
}

/// The class the CSS keys off. This is Game Mode; everything below is decoration.
export function setMode(on) {
    document.documentElement.classList.toggle('game-mode', !!on);
    return true;
}

export async function enter() {
    if (document.fullscreenElement) return true;

    try {
        await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    } catch {
        // Refused because nothing has been touched yet, or because this is Safari on a phone and
        // the API is not there at all. The class is already doing the real work.
        return false;
    }

    return isFull();
}

export function exit() {
    try { if (document.fullscreenElement) document.exitFullscreen?.(); } catch { /* not ours */ }
}

/// Whether the next stray gesture should be spent on going full screen.
///
/// A game is started from the host's phone, and a browser will not hand the display to a page
/// nobody has touched. So the wall waits: the next click or key anywhere on it finishes the job.
export function arm(value) {
    armed = !!value;
}

export function watch(ref) {
    dotnet = ref;
    document.addEventListener('fullscreenchange', changed);
    document.addEventListener('pointerdown', gesture, true);
    document.addEventListener('keydown', key, true);
    return isFull();
}

export function unwatch() {
    dotnet = null;
    document.removeEventListener('fullscreenchange', changed);
    document.removeEventListener('pointerdown', gesture, true);
    document.removeEventListener('keydown', key, true);
}

function changed() {
    try { dotnet?.invokeMethodAsync('ScreenChanged', isFull()); } catch { /* circuit gone */ }
}

function gesture(e) {
    if (!armed || document.fullscreenElement) return;

    // The testing panel is a strip of embedded phones somebody is deliberately poking at. Throwing
    // the laptop into full screen every time they tap one would make it unusable.
    if (e.target instanceof Element && e.target.closest('.game-testing')) return;

    armed = false;
    enter();
}

/// Escape is the one key everybody already knows means "give me my screen back".
///
/// The browser eats it while we are full screen — that press only leaves full screen and never
/// reaches here — so this fires on the *second* press, which is exactly the person who has decided
/// they want the navigation bar back rather than the one who fumbled a key.
function key(e) {
    if (e.key === 'Escape' && !document.fullscreenElement) {
        try { dotnet?.invokeMethodAsync('EscapePressed'); } catch { /* circuit gone */ }
        return;
    }

    gesture(e);
}
