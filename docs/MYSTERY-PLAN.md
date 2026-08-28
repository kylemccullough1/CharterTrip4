# Murder at Braun Manor — implementation plan

How the eight files in `files/` become a running game inside this app.

Read [`ARCHITECTURE.md`](ARCHITECTURE.md) first if the three-project split and the single-JSON
store are not already familiar — this plan leans on both and does not re-explain them.

---

## 0. The date

The murder mystery is on the itinerary for **Saturday 29 August 2026**. That is the fact that
shapes everything below, so it goes first rather than in a risks section at the bottom.

The spec in `files/` describes a two-hour, 21-player, five-faction social-deduction game with a
constraint-solving generator, a prompt engine, a live main screen, three-phase trials, ghosts, and
a bot soak harness. That is several weeks of work honestly estimated. What follows is therefore
ordered as a **cut line**, not a wish list: Tier 1 is the game that has to exist Saturday, Tier 2
is what makes it good, Tier 3 is what the spec asks for and this weekend will not get.

Every tier ends at a playable state. Nothing in Tier 1 has to be thrown away to reach Tier 2.

---

## 1. What is actually in `files/`

Eight data files and three design documents. **There are two copies of the data set and they are
not the same.**

| | |
|---|---|
| `files/*.json` | the earlier draft |
| `files/mnt/user-data/outputs/braun-manor/*.json` | **canonical — use this one** |

The `mnt/` copy is newer: killer and minion abilities became two-mode choices (`plant`/`scrub`,
`shield`/`decoy`), jester tampering became `subtle`/`blatant`, `story_beats.json` gained the whole
`tamper_system` block, and its `README.md` carries five generator rules derived from a 2000-seed
simulation that the outer copy has never heard of. `ghosts_npcs.json`, `rounds.json` and
`zones.json` are byte-identical in both.

Four files exist **only** at the top level and have no `mnt/` twin — `main_screen.json`,
`player_phone.json`, `braun-manor-characters.md`, `GAME-RUNDOWN.md`, `TESTING.md`.

**First action, before any code:** promote the `mnt/` set plus those four to `data/braun-manor/`,
delete `files/` and `files.zip`. Two copies of a rules file that disagree about what an ability
does is the single most likely way to ship a broken game.

### The data, briefly

| File | Shape |
|---|---|
| `characters.json` | 21 characters. `id, name, gender, title, voice, motive, fear, signature_item, acts{guilty,innocent}, seen{guilty,innocent}, herring_truth, trace{name,text,anchor_zone}, zones[], slots[], fixed_faction, tamper_insert, route_preference` |
| `zones.json` | 9 playable zones + the study. Adjacency, min/max capacity, `grants_access`, three access routes, one clue spot each |
| `factions.json` | 6 factions + ghost rules. Abilities carry `charges`, `shared`, `unlock`, and mode variants |
| `rounds.json` | 9 rounds / 110 minutes, the six-phase trial procedure, the early-end rule |
| `story_beats.json` | The spine, 5 method beats, 3 access beats, the signature beat, conviction cards, endgame templates, **`assembly_rules`**, **`tamper_system`** |
| `prompts.json` | Prompt-engine templates by category and faction; badge-scan telemetry notes |
| `ghosts_npcs.json` | 12 canned ghost lines, haunt, and standing orders for Braun + three facilitators |
| `main_screen.json`, `player_phone.json` | UI specifications for the two screens that matter |

`assembly_rules` is the important one. It states that **every sentence a player ever sees is a
template fill from these files** — nothing is authored at runtime. That is what makes this
tractable: the compiler is string substitution over ~200 authored blocks, not generation.

Slot supply, which is what the generator has to work with: 8 access-tagged characters, 5 means,
6 signature. Harry and Isla are `fixed_faction: inheritance` and never killers. Four traces are
zone-anchored (`carla`→driveway, `harry`/`florence`/`daquan`→lawn); the rest are portable and
spill to an adjacent zone when their zone already holds a clue.

---

## 2. Seven decisions the codebase forces

### 2.1 The existing `MysteryState` is a different game — replace it, don't extend it

`Core/Models/Mystery.cs` models *Murder at West Egg Manor*: 26 characters, a mastermind, five
conspirators, `ReleaseRound` clue cards. Braun Manor shares none of that structure. The seed
carries 26 West Egg characters with empty secrets.

Delete the type. Migration **v19** drops `trip.mystery` wholesale and writes the new shape.
`TripMigrations` already establishes that a removed property is simply ignored on load and gone on
the next save, so this is a small step — but it needs to be a *numbered* step rather than a silent
model change, because the reveal is the one screen where a half-migrated document is unrecoverable.

The itinerary item still reads "Murder at West Egg Manor". Rename it in the same migration.

### 2.2 Authored content does not go in `trip.json`

The eight files are content: read at startup, never written. Putting 60 KB of static prose in
`trip.json` would mean the debounced writer rewrites all of it on every vote, and it would destroy
the property that makes this design work — being able to read the trip in a diff.

Follow the seed's own pattern exactly. `SeedLoader` already embeds `data/trip.seed.json` as a
manifest resource so there is no "where did the file go?" problem on Azure; do the same:

```
data/braun-manor/*.json                      canonical content, in git
Infrastructure/Mystery/ScriptLoader.cs       embedded resources → one immutable object
Core/Mystery/Script/*.cs                     records mirroring the JSON
```

Registered as a singleton `IMysteryScript`. `trip.json` then holds only two things: **the deal**
(what this particular game generated) and **the live state** (what the room has done to it).

### 2.3 Auth is the blocker, and it is a prerequisite, not a phase

This is the largest gap between what exists and what the game needs, and it is worth being blunt
about it: **the app currently has exactly one account.** `CookieCurrentUser` treats
"authenticated" and "is an admin" as the same fact. Everyone else is an anonymous guest.

The game needs 21 distinct player identities on 21 phones plus four organizer consoles. There is
no version of Tier 1 that skips this.

The seam is already cut, which is the good news: `RosterPerson.JoinToken` exists and is unused,
and `TripPermissions` already carries a `PersonId` nobody sets. The work is:

- `/m/join/{token}` → look up the roster person → issue a cookie with a `PersonId` claim
- `CookieCurrentUser` reads that claim instead of assuming admin
- `TripPermissions.IsAdmin` becomes a claim, not a synonym for being signed in

Keep the committee's existing username/password path untouched beside it. One new sign-in route,
one changed method. Roughly half a day, and it unblocks Teams and live scoring too.

### 2.4 Nobody scans a QR code inside this app

`QRCoder` generates; it does not read. In-app scanning means the `BarcodeDetector` API, which
Safari on iOS does not implement — and half the party will be on iPhones.

`TESTING.md` states the way out in its own first line: *a QR code is just a URL.* Every scannable
is a link the **phone's native camera app** opens:

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

This matters because the killers' single collective charge and the minions' single collective
charge are exactly the "two people press the button at the same moment" case, and `TESTING.md`
calls it out by name: *hammer with simultaneous requests, this WILL race.*

Add one overload to the interface:

```csharp
Task<T> MutateAsync<T>(Func<TripData, T> mutate, TripArea area, CancellationToken ct = default);
```

Every ability, vote and scan then reports *"did that work?"* honestly. Small change; do it before
writing any of the abilities rather than retrofitting eleven call sites.

### 2.6 Flush on the beats that matter

Writes are debounced. For an itinerary that is correct. For a live game it means a crash mid-trial
loses whatever happened since the last flush, and `TESTING.md` lists *kill the server mid-round,
restart* as a scenario because it expects that to happen.

Split the difference: `FlushAsync` immediately on round advance, conviction, and ability fire —
the handful of events that would be miserable to reconstruct — and let scans, shares and votes
ride the debounce like everything else.

### 2.7 One process, and Saturday is the wrong night to find out

Already documented, already true, but the stakes change. An Azure restart during Investigation is
survivable; during the reveal it is not. Have the app runnable from a laptop on the house wifi as
a fallback, and the printed packets as the fallback to *that*.

---

## 3. The build

### Layer map

```
Core/Mystery/Script/           records mirroring the eight JSON files (immutable, loaded once)
Core/Mystery/Deal/             Dealer.cs — placement, killer draw, herrings, factions, clue layout
Core/Mystery/Text/             Compiler.cs — template fill per assembly_rules
Core/Mystery/                  MysteryService.cs — the rules, static, JeopardyService pattern
Core/Models/Mystery.cs         the deal + live state that go in trip.json
Infrastructure/Mystery/        ScriptLoader.cs
Web/Components/Pages/Mystery/  the six screens
```

`Dealer` and `Compiler` are pure functions of `(script, roster, seed)`. That is what makes
`?seed=1234` reproducible, and reproducibility is what makes the whole thing testable without
21 phones.

`MysteryService` follows `JeopardyService` exactly: static methods over `TripData`, called from
inside `MutateAsync`. No state of its own, no injection, trivially unit-testable.

### Tier 1 — the game has to exist Saturday

1. **Content promoted and loading.** `data/braun-manor/`, records, `ScriptLoader`, one test that
   asserts 21 characters, 9 zones, 6 factions and that every `slots` entry names a real slot.
2. **Auth.** §2.3. Join links, `PersonId`, admin as a claim.
3. **Migration v19.** West Egg out, Braun Manor in, itinerary item renamed.
4. **`Dealer`.** The nine generator steps from the data set's own `README.md` plus the five
   post-simulation rules: dual-tag half-weighting, lone-guilty-witness rejection, route-preference
   override, clue spillover, cross-zone sighting. Deterministic from a seed. Tests assert the
   invariants — three killers in distinct zones, never Harry or Isla, herrings exclude killers,
   zone capacities met.
5. **`Compiler`.** Study scene, killer briefings, witness statements, cover stories, clue texts.
   Pure substitution.
6. **The player phone** at `/m`. Character tab, knowledge tab as a card hand, prompts tab as a
   static list. Map and notes can wait.
7. **The host console** at `/mystery/host`. Manual round advance, the full guilty list, and an
   override for everything. **This is the safety net for every feature that does not get built** —
   anything the app cannot do, Braun's player does by hand from this page.
8. **Trials.** The six phases from `rounds.json`, votes on phones, tally on the screen. The
   nomination cut and the conviction cut both need their tie rules or a trial can hang.
9. **The main screen** at `/mystery/screen`. Roster grid, clue feed, trial takeover, conviction
   cards. The map panel is Tier 2.
10. **The print route.** Nine clue QR sheets and 21 badges. Without this there is no physical
    game, and it has to be printed **Friday**.
11. **The reveal.** Every endgame template in `story_beats.json` is already written; this screen
    fills them in and reads them out.

That is a complete, winnable, three-trial murder mystery. It is missing the prompt engine, the
ghosts, the abilities and the map.

### Tier 2 — what makes it good

12. **Abilities.** Detective sync / forensics / hard question, killer plant-or-scrub, minion
    shield-or-decoy, jester self-frame, Braun dig-dirt. Each is a small pure function plus an
    unlock gate. Sync's "you must have scanned their badge" constraint depends on badge scanning
    existing, so it lands after 13.
13. **Badge scanning and card sharing.** The interaction graph and the info-flow graph.
14. **Ghosts.** Canned messages, the peanut gallery, haunt. Cheap, and the payoff is
    disproportionate — convicted players stay in the game for another hour.
15. **The prompt engine.** Five-minute cadence, the five priority rules. This is what keeps quiet
    players moving, and it is genuinely the difference between a good party and fifteen people
    standing near a wall. If Tier 2 gets cut to one item, make it this one — though the three
    facilitators are the manual version of it, and are why the game survives without it.
16. **The map panel** on both screens.
17. **The facilitator console.**

### Tier 3 — the spec's back half

18. God console (`TESTING.md` §3), headless bot soak (§4), the eleven continuous invariants,
    photo-upload invitations, the AI voice restyle pass, the endgame stats screen.

The bot soak is the right way to prove no game ever wedges, and it is not happening in three days.
Its substitute is §5 of `TESTING.md` run by hand from the host console — the fifteen-item scenario
checklist, about an hour, and it catches the wedges that matter.

---

## 4. Risks, ranked

1. **Three days.** Tier 1 is aggressive for the time available. Decide tonight which tier is the
   target, and print the paper packets either way.
2. **A wedged trial.** A tie at the nomination cut with no rule firing, and the room stands still.
   The tie rules in `rounds.json` are specified — implement both cuts, and give the host console a
   force-advance for the case nobody predicted.
3. **iOS.** Test the camera-app-opens-a-link flow on a real iPhone before Friday, not Saturday.
4. **A generator that cannot satisfy its constraints** at a reduced roster after Friday
   cancellations. Cap the reshuffle loop and fail loudly to the host console rather than spinning.
5. **The shared-charge race** (§2.5) — the one bug that would let both killer modes fire.

---

## 5. Open decisions

These are not code questions. They gate the build.

- **Ruleset A or B for the killer win condition.** `factions.json` marks it *pending sim*: A is
  "one killer survives", B is "two survive, town wins at 2+ convicted". The endgame text and the
  early-end rule both branch on this.
- **The roster is 25 and the game wants 21 players + 4 organizers.** That works exactly — but
  which four play Braun, Leo, Chloe and Bertram has to be settled before casting can run.
- **Character-to-person casting.** 12 female roles, 9 male. Hand-assigned or randomised within
  gender.
- **Finder visibility on the clue feed.** `main_screen.json` flags it as a deliberate knob.
- **Which tier is Saturday's target.**
