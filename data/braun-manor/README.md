# Braun Manor — the story

Eight JSON files. They are the **starting point** for one specific evening, not a live data source:
`StoryLoader` copies them into `trip.json` the first time a game is created, and from then on the
story is edited on the site at `/games/mystery/story` like everything else on this trip.

So an edit here only reaches a trip that has never seeded one. See *Changing it after a trip has
one* below for what to do when it already has.

| File | Holds |
|---|---|
| `characters.json` | 21 guests plus the 4 house parts: sheet, room, faction, guilt, dialogue |
| `zones.json` | 9 rooms — 8 playable plus the study, which takes nobody |
| `factions.json` | 6 factions, their abilities, and which phase each ability unlocks in |
| `clues.json` | 9 cards, one pinned to each room |
| `slides.json` | the deck Braun walks the room through before the party |
| `objectives.json` | what the phase machine fires, and what staff can send by hand |
| `beefs.json` | pairs of guests with history. Nothing to do with the murder |
| `beats.json` | the letter, the murder, the study, the rules, the ending |

`GAME-RUNDOWN.md`, `TESTING.md` and `braun-manor-characters.md` are design documents. Nothing reads
them.

---

## The prose is written

Every string in these eight files is now written — the voices, the backstories, the grudges, the
nine cards, the six faction briefings, the deck Braun walks the room through, and the letter, the
announcement and the ending in `beats.json`. `MysteryStoryTests.The_shipped_story_is_written` walks
all of it and fails on anything that slips back.

**The convention that got it here still stands: an unwritten string is a row of dots.** One
predicate — `MysteryText.IsPlaceholder` — reads it everywhere, and it is what a field added to the
model tomorrow will arrive as: the editor draws the field as a blank to fill in, the Content gaps
panel counts what is left, a player-facing screen leaves the line out rather than showing dots to
the room, and

```bash
grep -c '"\.\+"' data/braun-manor/*.json
```

says how much is outstanding. It should print zero on every line.

Two of these strings carry a hole for the game to fill, and they are load-bearing rather than
decorative:

- `beats.json`'s `tamper_subtle` and `tamper_blatant` each carry `{insert}`, which
  `ScanShareService.Compose` replaces with the framed guest's belongings. A frame written without
  one makes the killers' Plant and the jester's self-framing do nothing at all, silently.
- `objectives.json`'s `go-*` entries carry `{target}` and `{zone}`, declared in `slots` and
  substituted by `ObjectiveBus`. A slot with no brace loses its target; a brace with no slot ships a
  brace to somebody's phone.

Both are asserted in `MysteryStoryTests`.

---

## Changing it after a trip has one

These files are the **starting point**, not a live data source, so an edit here only reaches a trip
that has never seeded a story. `StoryLoader.SeedInto` is guarded on `MysteryStory.Seeded`.

To change a story a trip already has: edit it on the site at `/games/mystery/story`, which is what
the editor is for. Discarding the evening does not help — that clears the play and never the story.

Rewriting the files and expecting deployed trips to pick it up needs a numbered step in
`TripMigrations`, the way `ToV32_TheStoryIsWritten` delivered this writing. That one replaced the
story wholesale, which was safe only because the ids did not change and `Mystery.Play` refers to
everything by id: the cast, the badge tokens, the clue tokens, the scans and the votes all survived.
A rewrite that renames or removes an id cannot do that and needs to reckon with `Play` itself.

---

## The structure, and what it is protecting

Most of the tests in `MysteryStoryTests` assert facts about this content rather than about code.
They are worth reading before changing anything structural, but three are load-bearing:

**Three killers, one per guilt slot — access, means, signature — in three different rooms.** Two
killers in one room is a single conversation that alibis both of them, and the evening turns on
nobody being able to clear a hand that easily.

**The access killer stands somewhere that reaches the study.** Only the Entry, the Family Room, the
Driveway and the Lawn do. A story where nobody could have got to the body does not hold together.

**Everybody has history with at least two people.** Somebody with no history has nothing to open a
conversation with but the weather, and the mingling round is the part that fails quietly.

Room capacities in `zones.json` are advisory now — the generator that enforced them is gone — but
they are still the sensible shape of the house, and the 21 guests currently sit inside all of them.

---

## What used to be here

This was a generator. A seeded dealer placed everybody, drew three killers by guilt slot, picked red
herrings and laid out clues; a compiler then wrote every sentence from ~200 template blocks and a
guilty-or-innocent reading per character. Roughly 2,150 lines of C# existed to make that reproducible
and balanced across thousands of possible games.

This one is played once. All of it is gone, and with it the reason the prose had to be embedded and
read-only: there is no compiler to fill templates, so the story can live in the trip and be edited
from a phone in a kitchen on the afternoon of the party.

The old content — two readings per character, the round book, the prompt engine, the ghost lines and
the two UI specification files — is in git history at `90ba14d` if any of it is worth mining for the
rewrite.
