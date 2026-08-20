# Architecture

Written for someone who knows C# well but has not used Blazor or Azure before.
Read this first; it explains *why* the code is shaped the way it is.

---

## 1. What Blazor Server actually is

Forget "web frontend" for a moment. You have written a class that holds state, has event
handlers, and renders a view. Blazor is that, except the screen is a browser tab.

A `.razor` file is **one C# class with an HTML template fused into it** — XAML and its
code-behind welded into a single file:

```razor
@page "/counter"
<h1>Count: @count</h1>
<button @onclick="Add">Add one</button>

@code {
    private int count;               // a field — that's your state
    private void Add() => count++;   // a method — that's your event handler
}
```

Here is the part that surprises people. With **Blazor Server**, that object lives **in the
server's memory**, not in the browser. The browser holds a small JavaScript shim and a
**WebSocket** back to the server. Blazor calls that connection a *circuit*. When a user clicks:

1. The click travels up the socket to the server.
2. Your C# `Add()` runs — on the server, with a breakpoint you can actually hit in Rider.
3. Blazor re-renders the component in memory, diffs the new HTML against the old, and sends
   **only the changed bits** back down the socket.
4. The shim patches those bits into the page.

So there is **no JavaScript to write, no REST API, no DTOs, no JSON serialization** between a
frontend and a backend. There is no frontend. It is C# calling C#; the browser is a remote display.

**The cost:** every connected person holds an open socket and a little server memory, and if the
connection drops they see a "reconnecting" banner. For twenty-six people on house wifi, nothing.

**The payoff, and the reason it was chosen here:** because everyone's UI state lives in *your*
process, the server can update all of them at once. When an admin awards a point, the server
pushes new HTML to twenty-six phones. That is the murder-mystery feature, essentially for free.

> The other Blazor flavour, **WebAssembly**, downloads .NET into the browser. No socket, works
> offline — but then you would need a REST API, and it cannot push to other people. Wrong tool here.

---

## 2. Why three projects

References only ever point one direction:

```
CharterTrip.Web  ──►  CharterTrip.Infrastructure  ──►  CharterTrip.Core
   (screens)             (files, the outside world)      (models + rules)
```

| Project | Contains | Knows about |
| --- | --- | --- |
| **Core** | Models, business rules, interfaces | Nothing. No ASP.NET, no filesystem. |
| **Infrastructure** | Reading and writing the JSON file, photos on disk | Core |
| **Web** | Pages, layouts, components | Core + Infrastructure |

Core references *nothing*, which means you can unit test it with no setup at all — see
`ItineraryServiceTests`, which exercises real reordering logic without a browser or a file.

The concrete payoff: Core declares the **interface** `ITripStore` ("something that can hold the
trip data"), and Infrastructure supplies the JSON implementation. If JSON is outgrown, write a
`SqlTripStore` in Infrastructure and **Core and Web never change** — they only ever knew the
interface. That is what the layering is buying, and it is worth internalising because it is how
most serious C# codebases are organised.

---

## 3. How data is stored

`trip.json` is not read on every request. The whole trip is **deserialized once at startup into a
single object held in memory**, and that object is the truth for as long as the app is running.
The file is only where it goes to survive a restart.

```csharp
public interface ITripStore
{
    TripData Current { get; }                                  // reads hit memory, never disk
    Task MutateAsync(Action<TripData> mutate, TripArea area);  // the ONLY write path
    Task FlushAsync(CancellationToken ct = default);
    event Func<TripChanged, Task>? Changed;
}
```

`JsonTripStore` is registered as a **singleton** — one instance, one file, one writer.

**Reads** are a field access, so rendering a page costs no I/O.

**Writes** all funnel through `MutateAsync`, which:
1. takes a `SemaphoreSlim` so two edits can never interleave,
2. applies your change and bumps `Revision`,
3. schedules a **debounced** save (~500 ms, so a burst of typing costs one disk write),
4. raises `Changed` so every interested component re-renders.

**Saving is atomic.** `AtomicFileWriter` writes to `trip.json.tmp`, forces it to physical disk,
then renames it over the real file. A rename is atomic, so a crash mid-save can never leave a
truncated `trip.json`. That is ten lines to remove a category of disaster.

**Backups.** `BackupHostedService` copies the file aside on startup and every 15 minutes, keeping
the last 20. The realistic failure at 1am is not a disk fault, it is a person deleting the wrong
thing — and recovery is then "copy a file back".

**If the file is corrupt** it is renamed to `trip.json.unreadable-{timestamp}` rather than deleted,
and the app starts from the seed. You never lose the evidence.

> ### The one rule you must not break
> This design assumes **exactly one process owns the file**. On Azure, keep the App Service at
> **one instance with autoscale off**. Two instances means two writers, and writes get silently lost.

### Where the data root lives

| Environment | Path | Why |
| --- | --- | --- |
| Local | `src/CharterTrip.Web/App_Data` | gitignored; it is state, not source |
| Azure | `/home/data` | `/home` is the only folder that survives a redeploy |

Set by config key `Trip:DataRoot` (on Azure, the app setting `Trip__DataRoot`). A relative path is
resolved against the app's content root in `Program.cs`, so `dotnet run` and a published build agree.

### The seed

`data/trip.seed.json` is the starting dataset, **embedded into the Infrastructure assembly** at
build time. That means there is no "where did the seed file go?" problem on Azure — if the app is
running, the seed is there. On first run, if no `trip.json` exists, the seed is written out.

`SeedDataTests` is what stops the hand-maintained JSON and the C# models drifting apart. A renamed
property fails a test instead of silently producing an empty page.

---

## 4. The three seams

Phase 1 deliberately builds three things it does not fully need yet. This is the anti-refactor
insurance: cheap now, expensive later.

### Seam 1 — one write path

Everything goes through `ITripStore.MutateAsync`. Nothing else touches the file. Adding scoring or
the murder mystery later means adding models and calling the same method.

### Seam 2 — the change event

`TripAwareComponent` is a base class that subscribes to `ITripStore.Changed` and re-renders when
the area it watches changes:

```csharp
public class Itinerary : TripAwareComponent
{
    protected override TripArea Watching => TripArea.Itinerary;
}
```

Right now that mostly matters for two tabs on one machine. The moment phase 2 gives twenty-six
people logins, **the same subscription is what makes every phone update at once** — no new code.
The `TripArea` filter is why editing the budget will not re-render twenty-six character cards.

### Seam 3 — permissions

`TripPermissions` is cascaded from `MainLayout` and every page asks it before offering an edit:

```razor
@if (CanEdit) { <button @onclick="AddItemAsync">+ Add item</button> }
```

Today `AlwaysAdminUser` says everyone is an admin. Phase 2 swaps that **one DI registration** for
join-link cookie auth. Pages already ask the question; only the answer changes.

Two shells already exist — `AdminShell` and `MemberShell` — with different navigation, chosen in
`MainLayout` (Blazor's `@layout` is fixed per page, so the choice is made inside one layout).
`NavTree` marks which entries are admin-only and prunes them per person.

---

## 5. How to add a new page

1. **Model** — add or extend a class in `CharterTrip.Core/Models`, and add the field to `TripData`.
2. **Seed** — add matching camelCase JSON to `data/trip.seed.json`.
3. **Test the shape** — add an assertion to `SeedDataTests` so the two cannot drift.
4. **Logic** — put anything non-trivial in a static service in `CharterTrip.Core/Services`
   (see `ItineraryService`) and unit test it. Keep it out of the `.razor` file.
5. **Page** — create `Components/Pages/Thing.razor`:
   ```razor
   @page "/thing"
   @inherits TripAwareComponent

   @code { protected override TripArea Watching => TripArea.Thing; }
   ```
   Read with `Trip.Something`. Write with `MutateAsync(t => ...)`. Gate edits on `CanEdit`.
6. **Navigation** — add one line to `NavTree.All`, with `AdminOnly: true` if it is committee-only.

`Itinerary.razor` is the worked example — inline editing, add/delete, reordering, drag and drop.

---

## 6. Things worth knowing

**Inline editing** uses `EditableText`, which renders a plain `<span>` for members and swaps to a
real `<input>` on click for admins. It commits on blur and on Enter, and cancels on Escape. Real
inputs rather than `contenteditable` — easier to reason about and no JS interop.

**Drag and drop** on the itinerary handles `dragstart`, `dragenter` and `drop`, but deliberately
*not* `dragover` — that fires every few milliseconds and each handler would be a server round-trip.
`@ondragover:preventDefault="true"` with no handler lets the browser allow the drop for free. The
↑ ↓ « » buttons exist because drag is unreliable on phones.

**Inputs are marked `draggable="false"`** inside draggable cards, otherwise Chrome starts a drag
when you try to place a cursor.

**The carousel** rotates on a server timer, which costs one small render per client every 6.5
seconds. Fine at this scale; if it ever became a crowd, move it to a CSS animation.

**Computed properties** like `BudgetLine.Total` are marked `[JsonIgnore]` so they do not get
written into the file.
