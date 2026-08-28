namespace CharterTrip.Web.Services;

/// <summary>
/// Whether this screen is currently being a game rather than a website.
///
/// The wall is a laptop plugged into a television with twenty-five people looking at it, and every
/// pixel of site chrome on it — the navigation bar, the footer's venue address, the gold ADMIN pill,
/// a "Sign out" link — is a pixel admitting the game is a web page somebody opened. Game Mode is the
/// one switch that says otherwise: chrome goes, every testing panel goes, and the screen asks for
/// the whole display.
///
/// Scoped, so there is one per circuit. That is deliberately the highest point this can live:
/// a browser tab is exactly the thing that is or is not being a game, so the TV can be in Game Mode
/// while the host's phone driving it is not. It is not in trip.json for the same reason — a flag
/// that travelled would strip the chrome off twenty-five guests' phones at once — and it is not
/// persisted, because a wall left in Game Mode overnight would hide the navigation from whoever
/// opens the laptop next.
///
/// Being scoped is also what makes it survive walking the wall from the bee to Jeopardy, which is
/// the case that actually matters on the night.
/// </summary>
public sealed class GameModeState
{
    public bool On { get; private set; }

    /// <summary>Which game asked, so the way out can name it. Null when nobody has.</summary>
    public string? Owner { get; private set; }

    /// <summary>
    /// Whether the browser has actually given us the display.
    ///
    /// Tracked separately from <see cref="On"/> and never conflated with it. Escape revokes full
    /// screen whenever it likes, and iOS Safari will not grant it to an arbitrary element at all —
    /// so a Game Mode that <em>was</em> full screen would be a Game Mode that silently does nothing
    /// on the phone the bee is run from. Full screen is an upgrade this asks for and never needs.
    /// </summary>
    public bool FullScreen { get; private set; }

    /// <summary>
    /// Synchronous, like <see cref="ToastService"/>'s: subscribers are components, and what they do
    /// with it is call InvokeAsync(StateHasChanged).
    /// </summary>
    public event Action? Changed;

    public void Enter(string owner) => Set(true, owner);

    public void Leave() => Set(false, null);

    public void Toggle(string owner)
    {
        if (On) Leave(); else Enter(owner);
    }

    /// <summary>What the browser says, reported by the scope. Not a request — a fact.</summary>
    public void ReportFullScreen(bool full)
    {
        if (FullScreen == full) return;

        FullScreen = full;
        Changed?.Invoke();
    }

    private void Set(bool on, string? owner)
    {
        if (On == on) return;

        On = on;
        Owner = on ? owner : null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Nobody is in Game Mode. The default for a component that somehow renders outside the scope,
    /// so no page has to null-check its way through a render — the same trick
    /// <see cref="Auth.TripPermissions.Guest"/> uses.
    /// </summary>
    public static readonly GameModeState Off = new();
}
