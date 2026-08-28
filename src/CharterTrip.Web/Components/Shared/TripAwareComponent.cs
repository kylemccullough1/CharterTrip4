using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Web.Auth;
using CharterTrip.Web.Services;
using Microsoft.AspNetCore.Components;

namespace CharterTrip.Web.Components.Shared;

/// <summary>
/// Base class for any page that shows trip data.
///
/// It subscribes to ITripStore.Changed and re-renders when the area it cares about changes.
/// Today that mostly matters for one browser with two tabs open; the moment phase 2 gives
/// twenty-six people their own logins, the same subscription is what makes the scoreboard
/// update on every phone at once. That is why it exists now rather than later.
/// </summary>
public abstract class TripAwareComponent : ComponentBase, IDisposable
{
    [Inject] protected ITripStore Store { get; set; } = default!;

    [Inject] protected IWebHostEnvironment Env { get; set; } = default!;

    [Inject] protected SimPhones Sims { get; set; } = default!;

    /// <summary>
    /// Which simulated phone this is, for the testing strip. Development only, and null on every
    /// real device.
    /// </summary>
    [SupplyParameterFromQuery(Name = "sim")] protected int? Sim { get; set; }

    [CascadingParameter] protected TripPermissions Permissions { get; set; } = new(false, "Guest");

    /// <summary>
    /// Whether this screen is being a game rather than a website.
    ///
    /// Cascaded from above the router, and every page gets it whether it asked or not — because
    /// what Game Mode mostly does to a page is take things away from it, and a page that did not
    /// know would keep drawing its testing panel behind a hidden navigation bar.
    /// </summary>
    [CascadingParameter] protected GameModeState GameMode { get; set; } = GameModeState.Off;

    /// <summary>Which slice of the trip this page renders. Changes elsewhere are ignored.</summary>
    protected abstract TripArea Watching { get; }

    protected TripData Trip => Store.Current;

    /// <summary>
    /// Whose phone this is.
    ///
    /// One expression, in one place, because it was copied into five pages and the sixth would
    /// have got it subtly wrong. A real phone carries its identity in a cookie; a simulated one
    /// carries a slot number in its URL, because twenty-five iframes on one origin share one
    /// cookie and the last one home would otherwise replace everybody — including the committee's
    /// own session on the board behind them.
    /// </summary>
    protected string? MePersonId =>
        IsSimPhone ? Sims.PersonFor(Sim!.Value) : Permissions.PersonId;

    /// <summary>
    /// Whether this is a frame in the testing rail rather than somebody's phone.
    ///
    /// It matters beyond identity, and that is the part that is easy to miss: a frame shares the
    /// cookie of the laptop around it, so a page that asked "am I the host?" of the cookie would
    /// answer yes on every frame the moment the laptop picked up a host code. A simulated phone is
    /// its slot and nothing else — no person, no team and no job it was not bound to.
    /// </summary>
    protected bool IsSimPhone => Env.IsDevelopment() && Sim is > 0;

    protected bool CanEdit => Permissions.CanEdit;

    /// <summary>
    /// Host controls: editable, and not while the room is watching. Setup drawers, "New codes" and
    /// the like are the website showing through, so Game Mode takes them with the rest of it.
    /// </summary>
    protected bool CanHost => Permissions.CanEdit && !GameMode.On;

    protected override void OnInitialized()
    {
        Store.Changed += OnTripChangedAsync;

        // The cascade is IsFixed — the state object is the same one all night — so the event is
        // how a page hears that it changed. Costs one subscription per page and saves every page
        // from having to know about Game Mode at all.
        GameMode.Changed += OnGameModeChanged;
    }

    private Task OnTripChangedAsync(TripChanged change) =>
        change.Affects(Watching) ? InvokeAsync(StateHasChanged) : Task.CompletedTask;

    private void OnGameModeChanged() => _ = InvokeAsync(StateHasChanged);

    /// <summary>Shorthand for "change the trip and save it".</summary>
    protected Task MutateAsync(Action<TripData> mutate) => Store.MutateAsync(mutate, Watching);

    public virtual void Dispose()
    {
        Store.Changed -= OnTripChangedAsync;
        GameMode.Changed -= OnGameModeChanged;
        GC.SuppressFinalize(this);
    }
}
