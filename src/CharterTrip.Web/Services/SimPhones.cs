using System.Collections.Concurrent;

namespace CharterTrip.Web.Services;

/// <summary>
/// Twenty-five independent sessions on one laptop, for testing.
///
/// The problem this exists to solve: identity lives in a cookie, and same-origin iframes share the
/// cookie of the page around them. So a strip of embedded phones cannot each sign in as somebody
/// different — the last one home replaces everybody, including the committee's own session on the
/// board behind them.
///
/// <c>?as=</c> dodged that by rendering a character's phone without ever signing in, which is fine
/// for looking at a screen and useless for testing the way people actually get into the game. This
/// is the other half: <c>?sim=3</c> is a session, held here rather than in a cookie, so frame three
/// can walk the real front door — type the code, tap a name, read the letter — while frame four is
/// somebody else entirely.
///
/// In memory and therefore gone on restart, which is correct: these are not real people, and a
/// simulated room surviving a deploy would be worse than losing it. Development only, registered
/// nowhere else.
/// </summary>
public sealed class SimPhones
{
    private readonly ConcurrentDictionary<int, string> _people = new();

    /// <summary>
    /// Bumped to make one frame reload, and only that one.
    ///
    /// It rides in the iframe's src, so changing it is what moves that frame — and leaving every
    /// other slot's alone is what stops a phone somebody is halfway through reading a letter on
    /// from being yanked back to the start.
    /// </summary>
    private readonly ConcurrentDictionary<int, int> _nonce = new();

    /// <summary>
    /// Where each frame is currently pointed.
    ///
    /// A real phone reaches a clue card by walking into a room and holding its camera at a QR code,
    /// which is exactly the part a laptop cannot do. Driving the frame to the same URL the camera
    /// would have opened is the nearest honest substitute: the page that runs is the real page, and
    /// everything downstream of it — the scan record, the trail, the tamper offer — is real too.
    /// </summary>
    private readonly ConcurrentDictionary<int, string> _routes = new();

    /// <summary>How many frames the strip is showing. Not the number that have joined.</summary>
    public int Count { get; private set; }

    /// <summary>Slot numbers currently on screen, in order.</summary>
    public IReadOnlyList<int> Slots => Enumerable.Range(1, Count).ToList();

    public int Add()
    {
        if (Count >= MaxPhones) return Count;

        Count++;
        return Count;
    }

    /// <summary>
    /// Drop the last frame, and whoever was signed into it.
    ///
    /// Their seat in the game is not given back — they joined for real, and a phone going flat is
    /// not the same as never having arrived. Discard the game to start over.
    /// </summary>
    public void RemoveLast()
    {
        if (Count == 0) return;

        _people.TryRemove(Count, out _);
        _nonce.TryRemove(Count, out _);
        _routes.TryRemove(Count, out _);
        Count--;
    }

    public void Reset()
    {
        _people.Clear();
        _nonce.Clear();
        _routes.Clear();
        Count = 0;
    }

    /// <summary>Who is holding this frame, or null while it is still on the code screen.</summary>
    public string? PersonFor(int slot) => _people.GetValueOrDefault(slot);

    public int NonceFor(int slot) => _nonce.GetValueOrDefault(slot);

    /// <summary>The page this frame is on. The front door until somebody sends it somewhere.</summary>
    public string RouteFor(int slot) => _routes.GetValueOrDefault(slot) ?? "/join";

    /// <summary>Send one frame to a page, the way a camera would have.</summary>
    public void Go(int slot, string route)
    {
        _routes[slot] = route;
        Reload(slot);
    }

    /// <summary>Send this frame round again, wherever its state now says it belongs.</summary>
    public void Reload(int slot) => _nonce.AddOrUpdate(slot, 1, (_, n) => n + 1);

    /// <summary>
    /// This frame has been through the front door as somebody.
    ///
    /// The equivalent of writing the auth cookie, and it happens at exactly the same point in
    /// <c>JoinWithCode</c> — so a simulated phone and a real one take the same path to get here.
    /// </summary>
    public void Bind(int slot, string personId) => _people[slot] = personId;

    /// <summary>
    /// The ceiling on the strip.
    ///
    /// Twenty-four, not twenty-five: the twenty-fifth phone is the one in the hand of whoever is
    /// testing, holding Braun. A strip that seats the whole cast seats the tester out of the only
    /// part with anything to drive.
    /// </summary>
    public const int MaxPhones = 24;

    public bool IsFull => Count >= MaxPhones;

    public int Joined => _people.Count;
}
