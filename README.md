# CharterTrip4

The 4th Annual Charter Trip site — **Braun Manor, Denison TX, 28–30 August 2026.**

An ASP.NET Core **Blazor Server** app (.NET 10) that stores everything in a single JSON file.
Twenty-six people, four teams, one trophy, and eventually a murder mystery that deals private
character cards to everyone's phone.

> **New to Blazor or Azure?** Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) first. It explains
> what Blazor Server actually is, why there are three projects, and how the storage works — written
> for a C# developer who has not done web work.

---

## Running it

```bash
dotnet run --project src/CharterTrip.Web
```

Then open the URL it prints (typically `https://localhost:7224`).

```bash
dotnet test          # 53 tests
dotnet build
```

Requires the **.NET 10 SDK** — pinned in `global.json`. If you do not have it:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

Open `CharterTrip.sln` in Rider or Visual Studio; everything also works from the CLI.

---

## What works today (phase 1)

| Area | State |
| --- | --- |
| **Home** | Hero, rotating Art Deco carousel, standings panel, weekend-at-a-glance, status tiles |
| **Itinerary** | **Fully working.** Inline edit, add/delete, drag between days, ↑↓«» on touch, sort-by-time |
| **Venue & Area** | Real content — the house, nearby stores and parks, committee info |
| **Games** | Real rules for all six games; scoring widgets come in phase 4 |
| Teams, Murder Mystery, Checklist, Roster, Budget, Payments, Shopping | Styled placeholders — the data is already seeded, only the screens are missing |

The design system, both role shells (`AdminShell` / `MemberShell`), navigation, inline editing,
toasts and confirm dialogs are all in place and reusable.

**Everyone is currently an admin.** That is deliberate — see "the three seams" in the architecture
doc. Phase 2 swaps one DI registration for real logins without touching any page.

## Roadmap

| Phase | Adds |
| --- | --- |
| 2 | Personal join links + QR codes, cookie auth, real admin/member split, Teams + live scoring |
| 3 | Murder mystery: casting, deal-to-phones, round control, private clue drops, ballot and reveal |
| 4 | Jeopardy board with phone buzzers, remaining game scoring widgets |
| 5 | Budget, payments, shopping, per-person checklist, photo uploads |

---

## Layout

```
src/CharterTrip.Core/            models + rules; references nothing
src/CharterTrip.Infrastructure/  the JSON store, backups, photos
src/CharterTrip.Web/             Blazor Server UI
tests/CharterTrip.Tests/         xUnit
tools/CharterTrip.SeedRefresh/   rebuilds the seed from a live trip.json
data/trip.seed.json              the starting dataset (embedded into the build)
docs/                            ARCHITECTURE.md, DEPLOY.md
```

References point one way only: `Web → Infrastructure → Core`.

## Data

Everything lives in one JSON file, held in memory while the app runs and written back atomically
after a short debounce, with rolling backups.

| Environment | Location |
| --- | --- |
| Local | `src/CharterTrip.Web/App_Data/trip.json` (gitignored — it is state, not source) |
| Azure | `/home/data/trip.json` |

Delete your local `App_Data` folder to start over from `data/trip.seed.json`.

Clue media — pictures and video — is the one thing kept outside that file. The trip stores only
the path `/photos/<id>.jpg`. Megabytes of base64 in a document rewritten on every keystroke would
cost the thing that makes this design work, which is being able to read the trip in a diff.

Two folders answer that path, and which one you use decides whether a copied `trip.json` shows
pictures or broken images:

| Folder | Committed? | Use it for |
| --- | --- | --- |
| `src/CharterTrip.Web/wwwroot/photos/` | Yes — deploys with the app | Media prepared ahead of time. Works in every environment with nothing to copy. Needs a rebuild to pick up a new file. |
| `App_Data/photos/` (`/home/data/photos/` live) | No — runtime state | Anything uploaded through the admin UI during the trip. |

A committed file wins — `MapStaticAssets` registers it as a literal route, which outranks the
`/photos/{id}` handler in `Program.cs`. So put the Jeopardy board's media in `wwwroot/photos/`,
and a `trip.json` copied between laptop and live keeps its pictures on its own.

Pictures are resized to 1600px in the browser before upload, so there is no server-side image
library to keep patched. Video is stored as it arrives — transcoding would mean shipping ffmpeg
to hold one weekend's clips — and is capped at 64 MB a clip instead.

Deploying never touches either file. To move a trip between the two, use the **Data** page at
`/admin/import`: it downloads the live trip as a file, and imports one back over the top after
showing what the swap would change. See **Getting the data out, and putting it back** in
`docs/DEPLOY.md`.

The seed is the floor the app lands on when there is no data file, so it is worth keeping close to
the real trip rather than frozen at the first commit:

```bash
dotnet run --project tools/CharterTrip.SeedRefresh -- <path-to-a-live-trip.json>
```

It keeps everything the host wrote and drops everything the weekend produced — scores, buzzer
codes, mystery round state. See **Keeping the seed current** in `docs/DEPLOY.md`.

> **One process only.** The app assumes a single instance owns the file. On Azure, keep it at one
> instance with autoscale off, or edits will be lost.

## Deploying

[`docs/DEPLOY.md`](docs/DEPLOY.md) walks through Azure App Service from scratch. Short version:
create a Linux .NET 10 Web App, set `Trip__DataRoot=/home/data`, lock it to one instance, save the
publish profile as the `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret, and push to `main`.

Until that secret exists, the deploy workflow builds and tests, then stops cleanly instead of failing.

---

## Where the content came from

Transcribed from the committee's planning documents: the 2026 budget sheet, the meeting notes
(6/23 through 8/11), *Murder at West Egg Manor*, and the JeopardyLabs board.

Some judgement calls are baked into `data/trip.seed.json` and worth a look before the trip:

- **Itinerary times between the fixed points are estimates.** Check-in, meals, the 11:30am games,
  the murder mystery and checkout came from the notes; the spacing around them did not.
- **Five Jeopardy clues are blank and seven are marked `draft: true`** — reconstructed from the
  meeting notes rather than the finished board. They need checking before Friday.
- **Murder mystery secrets and objectives are empty.** The source PDF specifies that each innocent
  needs a secret, a motive, someone they are protecting and one suspicious activity; those are for
  the host to write.
- **Marilyn** is marked paid with no amount recorded, matching the sheet.
- **T-shirts** are priced at $21.73 to make a $565 lump for 26 shirts add up.
