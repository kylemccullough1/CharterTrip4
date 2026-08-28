using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Web.Services;
using Microsoft.AspNetCore.Components;

namespace CharterTrip.Web.Components.Shared;

/// <summary>
/// What Police Sketch, Pool Noodle Cups and Beer Run all do the same way.
///
/// All three are a fixed number of rounds with a result to record before moving on, so all three
/// announce the round, show who scored, and get out of the way again. That was written three
/// times before this existed, and it had already drifted: one of them played a noise, one of them
/// timed the card differently, one of them did not celebrate at all.
///
/// A page supplies which game it is; everything about how a round feels is here.
/// </summary>
public abstract class RoundGamePage : TripAwareComponent
{
    [Inject] protected GameCues Cues { get; set; } = default!;

    protected override TripArea Watching => TripArea.Games;

    /// <summary>Which game's rows in the score log are this page's.</summary>
    protected abstract string GameId { get; }

    /// <summary>This game's live state, read fresh from the trip every time.</summary>
    protected abstract RoundGame State { get; }

    protected Game? Definition => Trip.Games.FirstOrDefault(g => g.Id == GameId);

    // ------------------------------------------------------------------ the result card

    /// <summary>Long enough to read a name and look up; short enough not to hold up the next round.</summary>
    private static readonly TimeSpan ResultFor = TimeSpan.FromMilliseconds(2200);

    private CancellationTokenSource? _showing;

    protected IReadOnlyList<ScoreRow>? ResultRows { get; private set; }
    protected string? ResultKicker { get; private set; }
    protected string? ResultCaption { get; private set; }

    protected bool ShowingResult => ResultRows is { Count: > 0 };

    /// <summary>
    /// Put up who scored, with a noise, and take it down again.
    ///
    /// Cancelling the one before matters: two quick rounds would otherwise leave the first
    /// timer to clear the second round's card early. Nothing here is awaited by the click that
    /// caused it — a circuit handles its events one at a time, and waiting out the animation
    /// would leave the next tap queued behind it.
    /// </summary>
    protected async Task ShowResultAsync(IReadOnlyList<ScoreRow> rows, string? kicker = null, string? caption = null)
    {
        var scored = rows.Where(r => r.Points != 0).ToList();
        if (scored.Count == 0) return;

        _showing?.Cancel();
        _showing?.Dispose();
        var cts = new CancellationTokenSource();
        _showing = cts;

        ResultRows = scored;
        ResultKicker = kicker;
        ResultCaption = caption;
        await InvokeAsync(StateHasChanged);

        await Cues.PlayAsync(GameCues.Scored);

        try
        {
            await Task.Delay(ResultFor, cts.Token);
            ResultRows = null;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) { /* another round was scored first */ }
        catch (ObjectDisposedException) { /* the page went away mid-celebration */ }
    }

    // ------------------------------------------------------------------ announcing a round

    /// <summary>Which round this page has already announced out loud, so it announces each one once.</summary>
    private string? _announced;

    /// <summary>
    /// Whether the screen is settled enough for a new round to announce itself. Overridden by a
    /// game that has something else to get through first — Sketch waits for a character to be
    /// picked, so the noise does not land while the host is still choosing one.
    /// </summary>
    protected virtual bool ReadyToAnnounce => !ShowingResult;

    protected override void OnAfterRender(bool firstRender)
    {
        if (State.Phase != PartyGamePhase.Playing)
        {
            _announced = null;
            return;
        }

        if (!ReadyToAnnounce) return;

        var key = RoundSplash.KeyFor(State);
        if (_announced == key) return;

        var first = _announced is null;
        _announced = key;

        // Nothing on the very first paint — the round was already open when the page loaded.
        if (!first) _ = Cues.PlayAsync(GameCues.RoundStart);
    }

    // ------------------------------------------------------------------ the usual actions

    protected Task BeginAsync() => MutateAsync(t => RoundGameService.Begin(Select(t)));

    protected Task SkipRoundAsync() =>
        MutateAsync(t => RoundGameService.NextRound(Select(t)));

    protected Task ResetAsync() =>
        MutateAsync(t => RoundGameService.Reset(t, Select(t), GameId));

    /// <summary>
    /// Finds this game's state on a trip being mutated. The <see cref="State"/> property reads
    /// the live document; a mutation has to be applied to the one the store is about to save.
    /// </summary>
    protected abstract RoundGame Select(TripData trip);

    public override void Dispose()
    {
        _showing?.Cancel();
        _showing?.Dispose();
        base.Dispose();
    }
}
