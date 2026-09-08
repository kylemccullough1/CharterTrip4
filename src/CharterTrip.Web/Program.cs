using Microsoft.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CharterTrip.Core;
using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure;
using CharterTrip.Infrastructure.Storage;
using CharterTrip.Web.Auth;
using CharterTrip.Web.Components;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

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

// Who is looking. One account, the committee's; everyone else browses as a guest. Pages ask
// TripPermissions rather than the cookie, so this registration is the only thing that decides.
builder.Services.Configure<AdminCredentialOptions>(
    builder.Configuration.GetSection(AdminCredentialOptions.Section));
builder.Services.AddSingleton<IAdminSignIn, AdminSignIn>();
builder.Services.AddScoped<ICurrentUser, CookieCurrentUser>();

// Whether this deployment expects to be looked at from inside somebody else's page.
//
// The showcase is embedded in duckdgoose.net, and a cookie is only sent into a frame on another
// site if it says SameSite=None. The default is Lax for the sign-in cookie and Strict for the
// antiforgery one, and Strict is the one that bites first: the login form posts, its antiforgery
// cookie is not sent because the top-level site is not this one, validation fails, and the guard
// further down redirects back to /login. From the visitor's side the password simply does not
// work — no error, no clue, and it works perfectly when the same site is opened in its own tab.
//
// SameSite=None requires Secure, and a Secure cookie is silently discarded on a plain http
// origin, so this stays off in Development where the fallback plan is this app on a laptop and
// twenty-five phones over house wifi. It is also off for the live trip: that deployment is not
// framed by anything, and relaxing a cookie nobody needs relaxed is how CSRF protection gets
// quietly given away.
var framed = SiteMode.ReadOnly && !builder.Environment.IsDevelopment();

// Chrome is retiring unpartitioned third-party cookies. Partitioned (CHIPS) keeps this working
// past that: the cookie is still sent into the frame, but it is filed under the embedding site,
// so a sign-in made inside duckdgoose.net belongs to that page and not to the whole browser.
// Browsers that do not know the attribute ignore it, so there is nothing to detect here.
static void Embeddable(Microsoft.AspNetCore.Http.CookieBuilder cookie)
{
    cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None;
    cookie.SecurePolicy = CookieSecurePolicy.Always;
    cookie.Extensions.Add("Partitioned");
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "chartertrip.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

        // Always in Production, which is Azure and is HTTPS anyway. In Development it has to be
        // SameAsRequest, because the fallback plan for the night is this app on a laptop and
        // twenty-five phones on the house wifi over plain http — and a browser silently discards a
        // Secure cookie on an http origin. Not "fails to sign in with an error": accepts the tap,
        // redirects, and lands the guest back on "you're not in this one yet" with nothing to
        // explain it. That cost an evening to find on a phone.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // A whole weekend, renewed on use. Signing in again halfway through Saturday because a
        // cookie expired is exactly the friction this is meant to remove.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/";

        if (framed) Embeddable(options.Cookie);
    });

// The antiforgery cookie defaults to SameSite=Strict, which is the stricter of the two and the
// one that actually stops the sign-in going through from inside the frame. Same conditions.
builder.Services.AddAntiforgery(options =>
{
    if (framed) Embeddable(options.Cookie);
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Where trip.json lives, resolved the same way TripStoreOptions resolves it below — the keys
// have to land in the persistent directory too, and this runs before options are bound.
var dataRoot = builder.Configuration["Trip:DataRoot"] is { Length: > 0 } configured ? configured : "App_Data";
if (!Path.IsPathRooted(dataRoot))
    dataRoot = Path.Combine(builder.Environment.ContentRootPath, dataRoot);

// Data protection encrypts the auth cookie, and its keys live in memory unless told otherwise —
// which would sign the committee out on every restart and every deploy. Keeping them beside
// trip.json puts them on the one directory this app knows is persistent.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot, "keys")))
    .SetApplicationName("CharterTrip");
builder.Services.AddScoped<CharterTrip.Web.Services.ToastService>();
builder.Services.AddScoped<CharterTrip.Web.Services.MediaAttachments>();
builder.Services.AddScoped<CharterTrip.Web.Services.GameCues>();

// Whether this screen is being a game rather than a website. Scoped, because a browser tab is
// exactly the thing that is or is not a game: the television can be in Game Mode while the phone
// driving it is not.
builder.Services.AddScoped<CharterTrip.Web.Services.GameModeState>();

// Twenty-five independent sessions on one laptop, so the sim strip can walk the real front door
// instead of impersonating past it. Development only — see SimPhones.
builder.Services.AddSingleton<CharterTrip.Web.Services.SimPhones>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Skipped in Development for the same reason as the cookie above: a phone pointed at
// http://<this-laptop>:5235 would be redirected to https on a development certificate it has never
// heard of, which is a full-page security warning rather than a party.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// Let duckdgoose.net frame this site, and nobody else.
//
// Blazor Server protects its circuit from clickjacking by sending `X-Frame-Options: SAMEORIGIN`
// and `Content-Security-Policy: frame-ancestors 'self'` on every page, which is why the portfolio's
// window showed only "refused to connect". Those defaults are written by the components endpoint
// while it runs, so the override is registered early and applied late: OnStarting fires just
// before the headers go out, after the framework has written its own.
//
// X-Frame-Options cannot name a second origin (ALLOW-FROM died with old Edge), so it is removed
// and frame-ancestors carries the whole policy. Every current browser honours frame-ancestors.
// The list is an allow-list of origins Kyle controls; an attacker's page still cannot frame this
// site, which is the entire threat clickjacking protection exists for.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Remove("X-Frame-Options");
        context.Response.Headers["Content-Security-Policy"] =
            "frame-ancestors 'self' https://duckdgoose.net https://www.duckdgoose.net http://localhost:5173";
        return Task.CompletedTask;
    });
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// A stale form is a retry, not a wall.
//
// An antiforgery token is tied to a cookie, and the cookie moves when the server restarts or when a
// page has been sitting open on a phone in somebody's pocket. The framework's answer is a bare 400
// reading "A valid antiforgery token was not provided" — which, on the one screen twenty-five people
// meet this game through, is a dead end nobody in a dark room can act on. Sending them back to the
// same page re-renders it with a fresh token and the next tap works.
//
// Checked here rather than caught downstream because UseAntiforgery handles its own exception and
// turns it into a 400 by the time anything else could see it — so the check has to happen first.
// IsRequestValidAsync buffers the form, which leaves it readable for the real middleware behind it.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var guarded = path.StartsWithSegments("/join") || path.StartsWithSegments("/login");

    if (guarded && HttpMethods.IsPost(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();

        if (!await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.Redirect(path + "?stale=1");
            return;
        }
    }

    await next();
});

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
//
// Takes JsonTripStore, not ITripStore, and that matters in showcase mode: ITripStore is a
// per-circuit sandbox there, so an endpoint asking for it would be handed a brand-new copy of
// the baseline and would report on a document that had existed for a microsecond. Health is a
// question about the file, so it is asked of the thing that owns the file.
app.MapGet("/healthz", (JsonTripStore store) =>
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
        canPersist = status.CanPersist,
        readOnly = CharterTrip.Core.SiteMode.ReadOnly
    });
});

// The other half of /admin/import: hand back the live trip as a file. Without this, getting a
// copy of the deployed data means an SSH session or the Kudu file browser, which is a lot to
// ask of someone whose actual goal is "let me plan Saturday on my laptop".
//
// Serialized from memory rather than read off disk, so it is the trip as it stands right now
// and not as it was before the last debounced save.
//
// This is the whole trip, including the mystery solution and every buzzer code, so it is behind
// the same sign-in as the admin pages rather than merely unlinked.
//
// JsonTripStore for the same reason as /healthz, and with a second one on top: in showcase mode
// this hands back the real trip rather than whatever the person downloading it has been playing
// with. A file called trip.json that quietly contained a stranger's afternoon of edits would be
// a worse export than no export at all.
app.MapGet("/admin/trip.json", (JsonTripStore store) =>
{
    var json = JsonSerializer.Serialize(store.Current, TripJson.Options);
    var name = $"trip-r{store.Current.Revision}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", name);
}).RequireAuthorization();

// Clue pictures and videos are far too big to live inside trip.json, so they are files beside it
// and the trip only stores the path. This is the route that serves them.
//
// An id is a fresh guid and its bytes never change, so the response is immutable for a year: the
// board can put the same clue on twenty phones without twenty fetches. Replacing a clue's media
// mints a new id, so nothing goes stale.
//
// Range processing is not a nicety here. Safari refuses to play a video at all from a response
// that does not accept ranges, which would mean clue videos working on the host's laptop during
// setup and on nobody's phone at the party. The id doubles as the ETag because it identifies the
// bytes exactly — a different id is a different file, always.
//
// Media reaches /photos/ two ways, and this route is only the second of them:
//
//   wwwroot/photos/   committed to git, deploys with the app, exists in every environment.
//                     MapStaticAssets above registers each file as a LITERAL endpoint, and a
//                     literal segment outranks the "{id}" parameter here — so a committed file
//                     wins and never reaches this handler. Needs a rebuild to appear.
//   the data folder   uploaded through the admin UI at runtime, lives beside trip.json, and is
//                     therefore NOT in the repo or in a downloaded trip.json.
//
// Prepared media belongs in wwwroot so that downloading the live trip and running it locally
// shows pictures instead of broken images. Anything uploaded during the weekend lands in the
// data folder and is served below, exactly as before.
app.MapGet(TripMedia.UrlPrefix + "{id}", async (string id, IPhotoStore media, HttpContext http, CancellationToken ct) =>
{
    var stream = await media.OpenAsync(id, ct);
    if (stream is null) return Results.NotFound();

    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

    var contentType = Path.GetExtension(id).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".m4v" => "video/x-m4v",
        ".ogv" => "video/ogg",
        _ => "image/jpeg"
    };

    return Results.File(
        stream,
        contentType,
        entityTag: new EntityTagHeaderValue($"\"{id}\""),
        enableRangeProcessing: true);
});

// Touch the store during startup so a broken data file fails loudly here rather than on
// the first page request.
_ = app.Services.GetRequiredService<JsonTripStore>().Current;

app.Run();
