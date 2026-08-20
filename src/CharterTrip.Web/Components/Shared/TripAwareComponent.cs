using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Web.Auth;
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

    [CascadingParameter] protected TripPermissions Permissions { get; set; } = new(false, "Guest");

    /// <summary>Which slice of the trip this page renders. Changes elsewhere are ignored.</summary>
    protected abstract TripArea Watching { get; }

    protected TripData Trip => Store.Current;

    protected bool CanEdit => Permissions.CanEdit;

    protected override void OnInitialized() => Store.Changed += OnTripChangedAsync;

    private Task OnTripChangedAsync(TripChanged change) =>
        change.Affects(Watching) ? InvokeAsync(StateHasChanged) : Task.CompletedTask;

    /// <summary>Shorthand for "change the trip and save it".</summary>
    protected Task MutateAsync(Action<TripData> mutate) => Store.MutateAsync(mutate, Watching);

    public virtual void Dispose()
    {
        Store.Changed -= OnTripChangedAsync;
        GC.SuppressFinalize(this);
    }
}
