// The camera, for the photograph everybody takes at the door.
//
// <input type="file" capture> asks the operating system for a picture and hands back a file, which
// on a phone means leaving the page for the camera app and coming back. That is three taps and a
// context switch at the exact moment twenty-five people are queueing. getUserMedia asks the browser
// for the lens instead: one permission prompt, a live preview, and a shutter.
//
// It can fail for reasons that are nobody's fault — permission refused, no camera, an insecure
// origin — so every call reports rather than throws, and the page keeps a file picker behind it.

let stream = null;

export async function start(videoId) {
    stop();

    if (!navigator.mediaDevices?.getUserMedia) return 'unsupported';

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            // The front camera, and a portrait-ish frame, because this becomes a round portrait on
            // a board seen from across a room.
            video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 1280 } },
            audio: false
        });
    } catch (error) {
        // NotAllowedError is a decision, not a fault — the page says so rather than apologising.
        return error?.name === 'NotAllowedError' ? 'denied' : 'unavailable';
    }

    const video = document.getElementById(videoId);
    if (!video) { stop(); return 'unavailable'; }

    video.srcObject = stream;
    video.setAttribute('playsinline', '');   // iOS goes fullscreen without it
    video.muted = true;
    await video.play().catch(() => {});

    return 'ready';
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
