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

    /// <summary>How many frames the strip is showing. Not the number that have joined.</summary>
    public int Count { get; private set; }

    /// <summary>Slot numbers currently on screen, in order.</summary>
    public IReadOnlyList<int> Slots => Enumerable.Range(1, Count).ToList();

    public int Add()
    {
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
        Count--;
    }

    public void Reset()
    {
        _people.Clear();
        Count = 0;
    }

    /// <summary>Who is holding this frame, or null while it is still on the code screen.</summary>
    public string? PersonFor(int slot) => _people.GetValueOrDefault(slot);

    /// <summary>
    /// This frame has been through the front door as somebody.
    ///
    /// The equivalent of writing the auth cookie, and it happens at exactly the same point in
    /// <c>JoinWithCode</c> — so a simulated phone and a real one take the same path to get here.
    /// </summary>
    public void Bind(int slot, string personId) => _people[slot] = personId;

    public int Joined => _people.Count;
}
