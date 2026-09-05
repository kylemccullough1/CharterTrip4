# CharterTrip4

The 4th Annual Charter Trip site — **Braun Manor, Denison TX, 28–30 August 2026.**

An ASP.NET Core **Blazor Server** app (.NET 10) that keeps the whole trip in one JSON file.
Twenty-five people on four teams, six scored games with the wall on a television and the controls on
phones, and a murder mystery that deals twenty-one private characters to everyone's phone and runs the
evening through three trials.

> **New to Blazor or Azure?** Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) first. It explains
> what Blazor Server actually is, why there are three projects, and how the storage works — written
> for a C# developer who has not done web work. Its permissions section predates personal join links
> and game codes; the current picture is in **Who is looking** below.

---

## Running it

```bash
dotnet run --project src/CharterTrip.Web
```

That uses the `http` launch profile and opens `http://localhost:5235`. The `https` profile adds
`https://localhost:7224`:

```bash
dotnet run --project src/CharterTrip.Web --launch-profile https
```

```bash
dotnet test          # 751 tests
dotnet build
```

Requires the **.NET 10 SDK** — pinned in `global.json`. Open `CharterTrip.sln` in Rider or Visual
Studio; everything also works from the CLI.

**Phones on the house wifi.** In Development the app skips the HTTPS redirect and lets the auth cookie
travel over plain http, so a phone pointed at `http://<laptop>:5235` can join. To reach it from
outside the network, `pwsh tools/share.ps1` opens a Cloudflare quick tunnel to the running app
(needs the `https` profile and `cloudflared`).

---

## What is here

Every page reads one in-memory `TripData` and writes through one method, so an edit on one screen
shows up on every phone watching that part of the trip without a refresh.

| Route | What it is |
| --- | --- |
| `/` | Hero and Art Deco carousel, countdown, weekend at a glance, the standings. Guests see the order; the committee sees the numbers, an **Edit points** link and a reset. |
| `/itinerary` | The guest guide: **Essentials**, **Schedule**, **Menu** and **Carpool** tabs, plus a **What to bring** packing card. Every line is inline-editable by the committee. The schedule is a timeline planner — hour rows, zoom, collapse empty hours, drag to move, a side editor per item. Carpools colour themselves from the car name. |
| `/venue` | The house, check-in and check-out, nearby stores and parks. |
| `/teams` | *Committee.* Drag people between teams and pick team colours. The roster is the one source of truth for membership. |
| `/points` | *Committee.* The score log — every point on the standings is one row here, from any game or added by hand. Add a row for minus ten to dock a team. |
| `/games` | *Committee.* Cards for the six games with rules, and **Reset every game**, which clears a night of play and never touches written content. |
| `/games/jeopardy` | The board on the wall: inline board editor (categories, clues, responses, pictures and video), title card, clue reveal, judging, Final Jeopardy with a timer. Phones at `/buzz` are the buzzers; the host's phone holds the answer. |
| `/games/spelling` | The bee's wall, which never shows the word. `/bee` is the other two screens: the host's phone with the word, its definition, part of speech and a sentence, and each speller's phone. Played by people, scored to teams. |
| `/games/sketch`, `/games/noodlecup`, `/games/beerrun` | Round-based scoring for Police Sketch, Pool Noodle Cups and Beer Run. Same engine, one splash and result screen per round. |
| `/games/relay` | The relay race: one gun starts every team's clock, each lead stops their own, fastest wins, short-handed winners earn more. |
| `/games/mystery` | Murder at Braun Manor — the door before the evening starts and the television once it has. See below. |
| `/admin/import` | *Committee.* **Data**: download the live trip as a file, import one back over the top after seeing what would change. |
| `/join`, `/join/{code}`, `/login`, `/logout` | The front door for every code on the trip, and the committee's sign-in. |
| `/healthz` | What the store thinks of itself: revision, people, whether it can persist, whether it is running from the seed. |

**Game Mode** is the switch on every wall screen that hides the site chrome and asks for full screen.
It is per browser tab, so the television can be in Game Mode while the phone driving it is not.

### Who is looking

Identity is a cookie, and there are three ways to get one:

| Way in | Who you are afterwards |
| --- | --- |
| The committee's username and password at `/login` | Admin, but not any one person. Credentials are PBKDF2-SHA256 hashes in the `Admin` configuration section — nothing in the repo turns back into a password. |
| A person's own `/join/{token}` link, or the party code plus tapping your name | That person. The four organizers are admins because the roster says so; the other twenty-one are not. Their team comes along for free. |
| A Jeopardy buzzer code, or a host code for Jeopardy or the bee | A team, or a job for one evening. No person. |

Pages ask a cascaded `TripPermissions` rather than the cookie, and `NavTree` prunes the admin-only
entries (Teams, Games, Data) from the menu per person. Typing an admin URL as a guest lands on a polite
"committee only" card with a sign-in link, not a 404. The cookie lasts thirty days, renewed on use, and
data-protection keys live beside `trip.json` so nobody is signed out by a deploy.

### Murder at Braun Manor

Twenty-one guests, four house parts, nine rooms, nine clue cards, six factions with phase-gated
abilities, three killers, three trials. The evening runs through fifteen phases from **Lobby** to
**Reveal** — arrivals and photos at the door, Braun's slide deck, introductions, the murder, the study,
a thirty-minute investigation, then discussion and trial three times over.

- **`/games/mystery`** is the public screen: two QR codes on the door, then the manor in a storm once
  the evening starts. Nothing that would spoil the night renders here.
- **`/m`** is one person's phone: their character, what they have found, who they have met, their
  abilities, the ballot. The four running the evening hold the same page with a control tab.
- **`/m/clue/{token}`** and **`/m/meet/{token}`** are the printed clue cards and name tags. A QR code is
  only ever a URL, opened by the phone's own camera — there is no in-app scanner because iOS Safari has
  none. **`/{number}`** is the short way to a card for anyone who types instead of scans.
- **`/games/mystery/story`** is where the story is written and edited, inline, like the Jeopardy board.
  **`/games/mystery/print`** produces the paper: the door poster, clue cards, name tags and personal
  links, and is the fallback if the app dies mid-evening.

The story and the play are two halves of `MysteryState`. The story — characters, rooms, clues, prose —
ships as eight JSON files in `data/braun-manor/`, embedded in the build and copied into `trip.json` the
first time a game is created; from then on it is edited on the site. The play is what the room did,
and discarding a game clears only that half, so the evening can be rehearsed against the story as many
times as it takes. [`data/braun-manor/README.md`](data/braun-manor/README.md) covers the content and
what the tests protect.

### Testing without twenty-five phones

In Development every wall screen has a **🧪 Testing** panel (hidden in Game Mode) that adds simulated
phones as iframes on the laptop. Each one walks the real front door — types the code, taps a name —
as its own session, so a full room can be assembled and driven from one machine. Up to twenty-four,
leaving the twenty-fifth for whoever is testing.

---

## Layout

```
src/CharterTrip.Core/            models, services, the mystery engine, the word bank; references nothing
src/CharterTrip.Infrastructure/  the JSON store, migrations, backups, import, photos, story loader
src/CharterTrip.Web/             Blazor Server UI, auth, audio and video assets
tests/CharterTrip.Tests/         xUnit — 751 tests, including a full mystery evening end to end
tools/CharterTrip.SeedRefresh/   rebuilds the seed from a live trip.json
tools/share.ps1                  puts the dev server on the internet for a phone
tools/word-data/enrich.py        fills the bee's word bank from public dictionary data (needs internet once)
data/trip.seed.json              the starting dataset (embedded into the build)
data/braun-manor/                the mystery's story files (embedded) and its design documents
docs/                            ARCHITECTURE.md, DEPLOY.md, MYSTERY-PLAN.md (historical)
music/, Video  and pics/         raw source assets; the app serves copies from wwwroot/audio, img and video
```

References point one way only: `Web → Infrastructure → Core`. Core has no ASP.NET and no filesystem,
which is why the game engines and the whole mystery are unit-tested without a browser.

Two things are compiled in as source rather than seeded as state: the 3,850-word Scripps bank the
bee draws from, in six difficulty tiers, and the Braun Manor story files.

## Data

Everything lives in one JSON file, held in memory while the app runs and written back atomically
after a short debounce, with rolling backups every fifteen minutes. Old files are upgraded on load by
numbered migrations (`TripMigrations`, currently at schema 37), so a `trip.json` from any earlier build
still opens.

| Environment | Location |
| --- | --- |
| Local | `src/CharterTrip.Web/App_Data/trip.json` (gitignored — it is state, not source) |
| Azure | `/home/data/trip.json` |

Delete your local `App_Data` folder to start over from `data/trip.seed.json`.

Photos and clue media are the one thing kept outside that file; the trip stores only the path
`/photos/<id>`. Two folders answer that path:

| Folder | Committed? | Use it for |
| --- | --- | --- |
| `src/CharterTrip.Web/wwwroot/photos/` | Yes — deploys with the app | Media prepared ahead of time. Works everywhere with nothing to copy. Needs a rebuild to pick up a new file. |
| `App_Data/photos/` (`/home/data/photos/` live) | No — runtime state | Anything uploaded during the trip: clue media, and the face each person takes on their own phone when they join a game. |

A committed file wins. Pictures are resized to 1600px in the browser before upload; video is stored as
it arrives, capped at 64 MB a clip, and served with range requests so Safari will play it.

Deploying never touches the data. To move a trip between laptop and live, use the **Data** page at
`/admin/import`. To keep the seed close to the real trip:

```bash
dotnet run --project tools/CharterTrip.SeedRefresh -- <path-to-a-live-trip.json>
```

It keeps everything the host wrote and drops everything the weekend produced — scores, codes, the
mystery's play. See **Keeping the seed current** in [`docs/DEPLOY.md`](docs/DEPLOY.md).

> **One process only.** The app assumes a single instance owns the file. On Azure, keep it at one
> instance with autoscale off, or edits will be lost.

## Deploying

Two GitHub Actions workflows: `ci.yml` builds and tests every pull request and push to `main`, and
`deploy.yml` publishes to the Azure App Service **chartertrip** on every push to `main`, then smoke-tests
`/healthz`. So a push to `main` is a deploy.

[`docs/DEPLOY.md`](docs/DEPLOY.md) walks through App Service from scratch. Short version: create a Linux
.NET 10 Web App, set `Trip__DataRoot=/home/data`, lock it to one instance, save the publish profile as
the `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret. Until that secret exists the deploy workflow builds and
tests, then stops cleanly instead of failing.

---

## What is not here

Budget, payments, shopping and a per-person checklist were in early plans and early data; they were
removed from the model rather than built, and the site does not track money. The Newlywed Game and the
champagne tower were cut from the weekend. The murder mystery was first designed as a generator that
dealt a fresh game every time; that was replaced by one hand-written story, and
[`docs/MYSTERY-PLAN.md`](docs/MYSTERY-PLAN.md) is kept only for the reasoning that survived.
