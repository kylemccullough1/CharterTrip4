// Which packing items this person has ticked off.
//
// Deliberately not in trip.json: a packing list is one person's business, twenty-six people
// sharing one set of checkboxes would be chaos, and nobody wants their half-packed suitcase
// broadcast to the group. So it lives in this browser and goes no further.
//
// localStorage rather than sessionStorage, because the whole point is that you tick "swimsuit"
// on Monday and it is still ticked on Friday. sessionStorage empties the moment the tab closes,
// which would make the feature actively worse than a paper list.

const KEY = 'chartertrip.packing.v1';

/// The ids currently ticked. Returns an array so Blazor can take it as string[].
export function load() {
    try {
        const raw = localStorage.getItem(KEY);
        if (!raw) return [];

        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed.filter(id => typeof id === 'string') : [];
    } catch {
        // Private browsing, a full quota, or something else's data under our key. An empty list
        // is a perfectly good answer — the worst case is somebody re-ticks their boxes.
        return [];
    }
}

export function save(ids) {
    try {
        localStorage.setItem(KEY, JSON.stringify(ids ?? []));
    } catch {
        // Storage unavailable or full. The ticks still work for this visit; they just will not
        // be there next time, and there is nothing useful to say about it mid-pack.
    }
}

export function clear() {
    try { localStorage.removeItem(KEY); } catch { /* nothing to do */ }
}
