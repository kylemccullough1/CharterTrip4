using Microsoft.JSInterop;

namespace CharterTrip.Web.Services;

/// <summary>
/// The noises the games played on their feet make.
///
/// Every one of those pages wanted the same forty lines — import the module, unlock it, play a
/// cue, swallow the three ways that can fail on a phone in a garden, dispose it on the way out —
/// so it lives here once instead. Scoped, which in Blazor Server means one audio context per
/// circuit: the browser will not give a page more than one anyway, and this way the module is
/// imported on the first cue of the night rather than on every page load.
///
/// Nothing here throws. A game that cannot make a noise is a game that is still perfectly
/// playable, and the last thing a host wants mid-round is an error where the scoreboard was.
/// </summary>
public sealed class GameCues(IJSRuntime js) : IAsyncDisposable
{
    /// <summary>A team took the round: stars, and a chime to look up at.</summary>
    public const string Scored = "sparkle";

    /// <summary>A new round is open.</summary>
    public const string RoundStart = "roundStart";

    private IJSObjectReference? _module;
    private bool _broken;

    public async Task PlayAsync(string cue)
    {
        if (_broken) return;

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/game-audio.js");
            await _module.InvokeVoidAsync("unlock");
            await _module.InvokeVoidAsync(cue);
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away mid-cue. Nothing to do and nobody to tell.
        }
        catch (Exception)
        {
            // No audio on this device, or the browser refused the context. Stop asking rather
            // than paying for a failed interop call on every round for the rest of the night.
            _broken = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;

        try { await _module.InvokeVoidAsync("dispose"); } catch { /* the circuit is already gone */ }
        try { await _module.DisposeAsync(); } catch { /* likewise */ }

        _module = null;
    }
}
