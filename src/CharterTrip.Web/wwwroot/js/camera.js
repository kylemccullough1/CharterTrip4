// The camera, for the photograph everybody takes at the door.
//
// <input type="file" capture> asks the operating system for a picture and hands back a file, which
// on a phone means leaving the page for the camera app and coming back. That is three taps and a
// context switch at the exact moment twenty-five people are queueing. getUserMedia asks the browser
// for the lens instead: one permission prompt, a live preview, and a shutter.
//
// It can fail for reasons that are nobody's fault — permission refused, no camera, an insecure
// origin, an iframe that was never granted the camera, another app already holding the lens — so
// every call reports rather than throws. It reports the *specific* reason too: the page used to say
// "no camera answered" to all six, which is a sentence nobody can act on.

let stream = null;

export async function start(videoId) {
    stop();

    // Browsers hide mediaDevices entirely outside a secure context, so the missing API and the
    // insecure origin look identical from here unless this is checked first. They are completely
    // different problems: one needs a different browser, the other needs https or localhost.
    if (!window.isSecureContext) {
        return { status: 'insecure', detail: location.origin };
    }

    if (!navigator.mediaDevices?.getUserMedia) {
        return { status: 'unsupported', detail: null };
    }

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            // The front camera, and a portrait-ish frame, because this becomes a round portrait on
            // a board seen from across a room.
            video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 1280 } },
            audio: false
        });
    } catch (error) {
        return { status: reason(error), detail: error?.message ?? String(error) };
    }

    const video = document.getElementById(videoId);
    if (!video) { stop(); return { status: 'unavailable', detail: 'no preview element' }; }

    video.srcObject = stream;
    video.setAttribute('playsinline', '');   // iOS goes fullscreen without it
    video.muted = true;
    await video.play().catch(() => {});

    return { status: 'ready', detail: null };
}

// Which of the failures this was. NotAllowedError covers both "the person said no" and "the frame
// this page is in was never granted a camera", and those need different sentences — a permission
// policy rejection is not something the person holding the phone can fix from the address bar.
function reason(error) {
    const name = error?.name;

    if (name === 'NotAllowedError') {
        return blockedByPolicy() ? 'blocked' : 'denied';
    }

    if (name === 'NotFoundError' || name === 'OverconstrainedError') return 'none';
    if (name === 'NotReadableError' || name === 'AbortError') return 'busy';
    if (name === 'SecurityError') return 'insecure';

    return 'unavailable';
}

// Permissions Policy does not grant an iframe the camera unless the embedding page says allow=
// "camera". Chrome and Firefox expose that here; where they do not, the answer is "assume the
// person decided", which is the more common case anyway.
function blockedByPolicy() {
    try {
        if (window.self === window.top) return false;
        return document.featurePolicy?.allowsFeature?.('camera') === false;
    } catch {
        return false;
    }
}

/// Grabs the current frame as a data URL. Square, centre-cropped, because the portrait is round.
export function capture(videoId, size = 900) {
    const video = document.getElementById(videoId);
    if (!video || !video.videoWidth) return null;

    const side = Math.min(video.videoWidth, video.videoHeight);
    const sx = (video.videoWidth - side) / 2;
    const sy = (video.videoHeight - side) / 2;

    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;

    const ctx = canvas.getContext('2d');

    // The preview is mirrored so people can aim; the photograph should not be, or every badge has
    // its writing backwards.
    ctx.drawImage(video, sx, sy, side, side, 0, 0, size, size);

    return canvas.toDataURL('image/jpeg', 0.85);
}

export function stop() {
    if (!stream) return;

    for (const track of stream.getTracks()) track.stop();
    stream = null;
}
