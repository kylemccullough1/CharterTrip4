using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure;
using CharterTrip.Infrastructure.Storage;
using CharterTrip.Web.Auth;
using CharterTrip.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Storage: the JSON file, backups, and the photo folder. See CharterTrip.Infrastructure.
builder.Services.AddTripStorage(builder.Configuration);

// A relative DataRoot should mean "next to the app", not "wherever the process happened
// to be launched from" — otherwise `dotnet run` and a published build disagree.
builder.Services.PostConfigure<TripStoreOptions>(options =>
{
    if (!Path.IsPathRooted(options.DataRoot))
        options.DataRoot = Path.Combine(builder.Environment.ContentRootPath, options.DataRoot);
});

// PHASE 1 SEAM: everyone is an admin. Phase 2 replaces this single registration with
// join-link cookie auth and nothing else in the app has to change — every page already
// asks TripPermissions whether it may edit.
builder.Services.AddScoped<ICurrentUser, AlwaysAdminUser>();
builder.Services.AddScoped<CharterTrip.Web.Services.ToastService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Azure pings this to decide whether the app is alive, but "alive" is the easy half. An app
// whose data directory does not survive deployment serves a flawless site built from the seed
// and loses every edit on the next restart, so the store's own view of itself is reported too:
// `seeded` still true after the site has been in use means trip.json is not being kept.
//
// Deliberately still a 200 — the deploy workflow smoke-tests this endpoint, and a data-root
// problem should not be able to block shipping a fix during the trip.
app.MapGet("/healthz", (ITripStore store) =>
{
    var status = store.Status;
    return Results.Ok(new
    {
        status = status.CanPersist && !status.Seeded ? "healthy" : "degraded",
        revision = store.Current.Revision,
        people = store.Current.Roster.Count,
        updatedUtc = store.Current.UpdatedUtc,
        dataPath = status.DataPath,
        seeded = status.Seeded,
        canPersist = status.CanPersist
    });
});

// Touch the store during startup so a broken data file fails loudly here rather than on
// the first page request.
_ = app.Services.GetRequiredService<ITripStore>().Current;

app.Run();
