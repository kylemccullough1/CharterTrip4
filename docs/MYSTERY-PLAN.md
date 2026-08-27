# Murder at Braun Manor — implementation plan

How the content in `data/braun-manor/` becomes a running game inside this app.

Read [`ARCHITECTURE.md`](ARCHITECTURE.md) first if the three-project split and the single-JSON
store are not already familiar — this plan leans on both and does not re-explain them.

The phases below are ordered by **what unblocks what**, not by a calendar. Each one ends somewhere
you could stop.

---

## 1. The content

Thirteen files in `data/braun-manor/`, in git, committed as one unit. They are **content**: read at
startup, never written.

There used to be two copies of the data set and they disagreed about what the abilities did. The
older one is gone. What survives is the copy where killer and minion abilities are two-mode choices
(`plant`/`scrub`, `shield`/`decoy`), jester tampering is `subtle`/`blatant`, `story_beats.json`
carries the `tamper_system` block, and `README.md` documents five generator rules derived from a
2000-seed simulation.

| File | Shape |
|---|---|
| `characters.json` | 21 characters. `id, name, gender, title, voice, motive, fear, signature_item, acts{guilty,innocent}, seen{guilty,innocent}, herring_truth, trace{name,text,anchor_zone}, zones[], slots[], fixed_faction, tamper_insert, route_preference` |
| `zones.json` | 9 zones — **8 playable plus the study**, which is the murder scene and takes no players (`players_allowed: false`). Adjacency, `capacity{min,max}`, `grants_access`, three access routes, one clue spot each |
| `factions.json` | 6 factions summing to exactly 21, plus `ghost_rules`. Abilities carry `charges`, `shared`, `unlock`, and mode variants |
| `rounds.json` | 9 rounds, the six-phase trial procedure, the early-end rule |
| `story_beats.json` | `spine`, 5 `method_beats`, 3 `access_beats`, `signature_beats`, `conviction_reveals`, `endgame_reveals`, **`assembly_rules`**, **`tamper_system`** |
| `prompts.json` | Prompt-engine templates by category and faction; badge-scan telemetry notes |
| `ghosts_npcs.json` | 12 canned ghost lines, haunt, and standing orders for Braun + three facilitators |
| `main_screen.json`, `player_phone.json` | UI specifications for the two screens that matter |
| `README.md` | The generator algorithm, the balance snapshot, and the five post-simulation rules |
| `GAME-RUNDOWN.md`, `TESTING.md`, `braun-manor-characters.md` | The design documents |

`assembly_rules` is the important one. It states that **every sentence a player ever sees is a
template fill from these files** — nothing is authored at runtime. That is what makes this
tractable: the compiler is string substitution over ~200 authored blocks, not generation.

Slot supply, which is what the generator has to work with: 8 access-tagged characters, 5 means,
6 signature. Six characters can never be killers — Molly, Emilia, Sharkeisha and Remington carry no
`slots` at all, and Harry and Isla are `fixed_faction: inheritance` — leaving 15 eligible. Four
traces are zone-anchored (`carla` to the driveway, `harry`/`florence`/`daquan` to the lawn); the rest
are portable and spill to an adjacent zone when their zone already holds a clue.

Placement has room: the 8 playable zones want a minimum of 17 players and accept a maximum of 29,
so 21 sits comfortably inside. **Below 17 players the placement constraints cannot be satisfied at
all** — worth knowing, because it is the number at which the generator starts reshuffling forever
rather than failing.

---

## 2. Seven things the codebase forces

All seven verified against the tree.

### 2.1 The existing `MysteryState` is a different game — replace it, don't extend it

`Core/Models/Mystery.cs` models *Murder at West Egg Manor*: 26 characters, a mastermind, five
conspirators, `ReleaseRound` clue cards. Braun Manor shares none of that structure. The seed carries
26 West Egg characters with empty secrets.

Delete the type. Migration **v19** drops `trip.mystery` wholesale and writes the new shape
(`TripMigrations.CurrentVersion` is 18, so v19 is next). `TripMigrations` already establishes that a
removed property is ignored on load and gone on the next save, so the mechanism is cheap — but it
needs to be a *numbered* step rather than a silent model change, because the reveal is the one
screen where a half-migrated document is unrecoverable.

Two things about the size of this:

- **The rename is three places in the seed, not one.** The `slide-3` deco caption, `item-s8`'s
  title, and `Mystery.Title`. Plus `Mystery.razor`. And `item-s8`'s notes read "Gatsby dress
  code" — **that is a content decision, not a rename**. Braun Manor is not Gatsby, and that field
  is what guests read on their phones beforehand.
- **Deleting the type touches seven files.** `TripData.cs`, `SeedPreparation.cs` (the state reset
  the seed-refresh tool runs), `TripReplace.cs`, `TripImporter.cs` (import validation that counts
  mystery roles against roster size — it needs a new rule for the new shape), and assertions in
  `SeedDataTests.cs` and `SeedPreparationTests.cs`.

### 2.2 Authored content does not go in `trip.json`

The content is read at startup and never written. Putting 110 KB of static prose in `trip.json`
would mean the debounced writer rewrites all of it on every vote, and it would destroy the property
that makes this design work — being able to read the trip in a diff.

Follow the seed's own pattern exactly. `SeedLoader` embeds `data/trip.seed.json` as a manifest
resource so there is no "where did the file go?" problem on Azure; do the same:

```
data/braun-manor/*.json                      canonical content, in git
Infrastructure/Mystery/ScriptLoader.cs       embedded resources → one immutable object
Core/Mystery/Script/*.cs                     records mirroring the JSON
```

Registered as a singleton `IMysteryScript`. `trip.json` then holds only two things: **the deal**
(what this particular game generated) and **the live state** (what the room has done to it).

### 2.3 Identity is the blocker, and it is a prerequisite, not a phase

**The app currently has exactly one account.** `CookieCurrentUser` treats "authenticated" and "is an
admin" as the same fact. Everyone else is an anonymous guest. The game needs 21 distinct player
identities on 21 phones plus four organizer consoles.

The seam is already cut: `RosterPerson.JoinToken` exists and is referenced nowhere,
`TripPermissions` carries a `PersonId` nobody sets, and a `TripRole { Member, Admin }` enum is
already defined and unused. See phase 2.

One thing this has to account for: `Mystery.razor` is wrapped in `<AdminOnly>`, so today no guest
can reach the mystery at all. Every player-facing route sits outside that guard, keyed on `PersonId`.

### 2.4 Nobody scans a QR code inside this app

`QRCoder` generates; it does not read. In-app scanning means the `BarcodeDetector` API, which Safari
on iOS does not implement — and half the party will be on iPhones.

`TESTING.md` states the way out in its own first line: *a QR code is just a URL.* Every scannable is
a link the **phone's native camera app** opens:

```
/m/meet/{playerToken}     badge on a name tag  → logs the interaction edge
/m/clue/{clueToken}       the nine clue cards  → publishes to the board, opens the clue
```

No camera permission, no JS library, no iOS problem, and the scan handler is an ordinary Blazor
page. The share flow's "or pick from recent scans" fallback in `player_phone.json` covers the case
where someone's camera is being difficult.

The clue token has to be unguessable — nine sequential clue ids would let a bored player read all
nine clues from the sofa without walking anywhere, and walking there is the entire mechanic.

### 2.5 `MutateAsync` cannot return a value, and the shared charges race

`ITripStore.MutateAsync` takes an `Action<TripData>`. The mutation runs under `_stateGate`, so a
check performed *inside* the lambda is atomic — but there is no way to report the outcome back to
the caller except by capturing a closure variable, which works and reads badly.

This matters because the killers' single collective charge and the minions' single collective charge
are exactly the "two people press the button at the same moment" case, and `TESTING.md` calls it out
by name: *hammer with simultaneous requests, this WILL race.*

Add one overload to the interface:

```csharp
Task<T> MutateAsync<T>(Func<TripData, T> mutate, TripArea area, CancellationToken ct = default);
```

Every ability, vote and scan then reports *"did that work?"* honestly. See phase 4 — it comes before
the abilities rather than being retrofitted across eleven call sites.

### 2.6 Flush on the beats that matter

Writes are debounced. For an itinerary that is correct. For a live game it means a crash mid-trial
loses whatever happened since the last flush, and `TESTING.md` lists *kill the server mid-round,
restart* as a scenario because it expects that to happen.

Split the difference: `FlushAsync` immediately on round advance, conviction, and ability fire — the
handful of events that would be miserable to reconstruct — and let scans, shares and votes ride the
debounce like everything else.

### 2.7 One process, and the reveal is the worst moment to lose it

Already documented, already true, but the stakes change. A restart during Investigation is
survivable; during the reveal it is not.

The obvious fallback — run the app from a laptop on the house wifi — **does not work as the app is
configured.** `Program.cs` calls `UseHttpsRedirection()` and sets the auth cookie to
`SecurePolicy = Always`. Over plain HTTP the cookie is never issued, so nobody can sign in at all;
over HTTPS it means a self-signed certificate on 21 phones. Making it real needs a non-Azure config
switch dropping `SecurePolicy` to `SameAsRequest` and skipping the HTTPS redirect,
`--urls http://0.0.0.0:5000`, and `trip.json` **plus** the `keys/` directory copied across
(`Program.cs` persists Data Protection keys under the data root) — then a test on one real phone.

Until that exists, the printed packets are the only fallback, which is a good reason for phase 11 to
land early rather than last.

---

## 3. The phases

### Layer map

```
Core/Mystery/Script/           records mirroring the content files (immutable, loaded once)
Core/Mystery/Deal/             Dealer.cs — placement, killer draw, herrings, factions, clue layout
Core/Mystery/Text/             Compiler.cs — template fill per assembly_rules
Core/Mystery/                  MysteryService.cs — the rules, static, JeopardyService pattern
Core/Models/Mystery.cs         the deal + live state that go in trip.json
Infrastructure/Mystery/        ScriptLoader.cs
Web/Components/Pages/Mystery/  the screens
```

`Dealer` and `Compiler` are pure functions of `(script, roster, seed)`. That is what makes
`?seed=1234` reproducible, and reproducibility is what makes the whole thing testable without
21 phones.

`MysteryService` follows `JeopardyService` exactly: static methods over `TripData`, called from
inside `MutateAsync`. No state of its own, no injection, trivially unit-testable. `ITripStore` is a
singleton, so its `Changed` event already fans out to every open circuit — that is the live-update
mechanism for the phones and the main screen, and it needs nothing new.

### Phase 0 — land the content ✅

Done. The data set is in `data/braun-manor/`, committed and pushed. It previously existed only in a
git stash entry, untracked on every branch, on a stash stack shared with four other worktrees.

### Phase 1 — the script layer

`Core/Mystery/Script/*.cs` records mirroring the JSON. `Infrastructure/Mystery/ScriptLoader.cs`
reading them as embedded resources, copying `SeedLoader` verbatim. Registered as a singleton
`IMysteryScript`.

One test asserting the shape: 21 characters, 9 zones, 6 factions summing to 21, and that every
`slots` entry names a real slot.

### Phase 2 — generic code login

One route, `/login/{code}`, resolving a code and landing the person in the right place:

1. a `RosterPerson.JoinToken` → issue the cookie with a `PersonId` claim, redirect to their home
2. a Jeopardy team or host code → the buzzer

`CookieCurrentUser` reads the `PersonId` claim instead of assuming admin, and `IsAdmin` comes from
`RosterPerson.Role == TripRole.Admin` rather than from being signed in at all. The committee's
existing username/password path stays untouched beside it.

`/buzz/{Code}` was Jeopardy-specific and should never have set the pattern; it becomes a redirect to
`/login/{code}` so anything already printed keeps working. `Buzz.razor` is otherwise the template
for every phone screen that follows — a working, no-chrome, live-updating page with a host view and
a player view in one file, on `BareLayout`, inheriting `TripAwareComponent`.

This unblocks Teams and live scoring too, not just the mystery.

### Phase 3 — migration v19

West Egg out, Braun Manor in, per §2.1: the new model shape, the three seed renames, the dress-code
decision, and the seven referencing files. Also the provenance line in the root `README.md`, which
still credits *Murder at West Egg Manor* as a source — true of the old seed, misleading after this.

### Phase 4 — the `MutateAsync<T>` overload

§2.5. Before any ability, vote or scan is written, so a refusal has somewhere to be reported.

### Phase 5 — `Dealer`

The nine generator steps from `README.md` plus its five post-simulation rules: dual-tag
half-weighting, lone-guilty-witness rejection, route-preference override, clue spillover, cross-zone
sighting. Deterministic from a seed.

Tests assert the invariants: three killers in distinct zones, never one of the six ineligible
characters, herrings exclude killers, zone capacities met, every zone holds exactly one clue. Cap
the reshuffle loop and fail loudly to the host console rather than spinning — below 17 players the
constraints are unsatisfiable and no amount of reshuffling will help.

Casting is straight assignment, hand-picked or randomised. Gender is not an input.

### Phase 6 — `Compiler`

Study scene, killer briefings, witness statements, cover stories, clue texts. Pure substitution per
`assembly_rules`.

### Phase 7 — the host console

`/games/mystery` becomes the host console: manual round advance, the full guilty list, and an
override for everything. **This is the safety net for every phase after it** — anything the app
cannot do, Braun's player does by hand from this page. That is why it comes before the player-facing
screens rather than after them.

### Phase 8 — the player phone

`/m`. Character tab, knowledge tab as a card hand, prompts tab as a static list. Map and notes come
later.

### Phase 9 — trials

The six phases from `rounds.json`, votes on phones, tally on the screen. Both tie rules are fully
specified in the file — all tied players nominated at the top-4 cut; revote then the earlier open
tally at the top-2 cut — and both need implementing or a trial can hang. Give the host console a
force-advance for the case nobody predicted.

The early-end rule is the subtle one: it fires only on all three killers convicted, **not** on the
two that win town the game. See §4.

### Phase 10 — the main screen

`/games/mystery/screen`. Roster grid, clue feed, trial takeover, conviction cards. The map panel
comes later.

### Phase 11 — the print route

Nine clue QR sheets and 21 badges. `QrImage.razor` already renders these and needs no new
dependency. Without this there is no physical game — and while §2.7 stands, the printed packets are
the only fallback, so this is worth pulling earlier than its number suggests.

### Phase 12 — the reveal

Every endgame template in `story_beats.json` is already written; this screen fills them in and reads
them out.

### Then, in order of payoff

**Abilities** — detective sync / forensics / hard question, killer plant-or-scrub, minion
shield-or-decoy, jester self-frame, Braun dig-dirt. Each is a small pure function plus an unlock
gate. Sync's "you must have scanned their badge" constraint depends on badge scanning, so it lands
after it.

**Badge scanning and card sharing** — the interaction graph and the info-flow graph.

**Ghosts** — canned messages, the peanut gallery, haunt. Cheap, and the payoff is disproportionate:
convicted players stay in the game for another hour. Ghost silence is a hard rule; the first time a
dead player free-texts "it's Wilhelm", the game is over.

**The prompt engine** — five-minute cadence, the five priority rules. This is what keeps quiet
players moving, and it is genuinely the difference between a good party and fifteen people standing
near a wall. The three facilitators are the manual version of it, and are why the game survives
without it.

**The map panel** on both screens, then **the facilitator console**.

**Then instrumentation** — the god console (`TESTING.md` §3), the headless bot soak (§4), the eleven
continuous invariants, photo-upload invitations, the AI voice restyle pass, the endgame stats screen.
The bot soak is the right way to prove no game ever wedges; its substitute is §5 of `TESTING.md` run
by hand from the host console — fifteen scenarios, about an hour, and it catches the wedges that
matter.

### The route family, stated once

- `/login/{code}` — the only code-in-URL entry, for every game.
- `/m/*` — **only** what goes inside a QR code or on a name tag (`/m`, `/m/meet/{t}`,
  `/m/clue/{t}`). Short URL, denser code, fewer characters to type by hand when a camera will not
  focus.
- `/games/mystery` and `/games/mystery/screen` — host console and main screen. `NavTree` and the
  games grid keep pointing at a real page, and `AdminOnly` keeps guarding both for free.

---

## 4. Open decisions

One left. The rest are settled: the organizers are already cast (Jou, Ali, Kyle and Em to Braun,
Leo, Chloe and Bertram), the roster is trimmed to 21 playable roles with Jacob Kruse cut, gender is
not an input to casting, and the win condition is decided — see below.

- **Finder visibility on the clue feed.** `main_screen.json` sets `finder_shown: true` and flags it
  as a deliberate knob: it credits hunters and makes re-scanning a tampered clue a public act. Flip
  to anonymous if playtest shows scan-shyness.

### The win condition: Ruleset B, settled

**Killers win on 2+ of 3 surviving. Town wins on 2+ of 3 convicted.** Across the six conviction
slots those are exhaustive and mutually exclusive — 0 or 1 convicted is a killer win, 2 or 3 is a
town win — so unlike the old mixed reading there is no outcome where nobody wins. All six places in
the content that stated a win condition now agree.

Two consequences the trial code has to respect:

- **Reaching 2 convictions does not end the game.** Town's win is evaluated after the third trial,
  and the room is not told it has already won. The early end fires only on a clean sweep of all
  three, where there is genuinely nothing left to catch. Without that, a trial-1 double-hit would
  end a two-hour game forty minutes in and strand every jester and Braun who hadn't scored yet.
- **The balance estimate is void, not adjusted.** The old `~45%`/`~55%` figures were computed
  against the contradictory reading, and town's job just got easier by an unmeasured amount — 2 of
  6 slots rather than 3. The `README.md` table now reads `re-sim` rather than carrying a number
  nobody has earned.

`story_beats.json` gained a `town_win_partial` endgame template for the 2-of-3 case, because the
existing `town_win` text asserts all three hands were caught and the reveal screen reads it aloud.
That new line is the one piece of prose in the data set not written by the original author — worth a
voice pass before the night.

`rounds.json`'s `total_runtime_minutes` said 110 while its nine rounds sum to 120, which is exactly
the itinerary slot. The field now says 120. The schedule is unchanged; only the summary that lied
about it is.

---

## 5. Risks, ranked

1. **A wedged trial.** A tie at a cut with no rule firing, and the room stands still. Both tie rules
   are specified in `rounds.json` — implement both cuts, and give the host console a force-advance
   for the case nobody predicted.
2. **iOS.** Test the camera-app-opens-a-link flow on a real iPhone well before the night.
3. **A generator that cannot satisfy its constraints** at a reduced roster after cancellations. The
   floor is 17 players. Cap the reshuffle loop and fail loudly to the host console rather than
   spinning.
4. **The shared-charge race** (§2.5) — the one bug that would let both killer modes fire.
5. **No laptop fallback yet** (§2.7). Print the packets.
