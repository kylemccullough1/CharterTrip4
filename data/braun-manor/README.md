# Braun Manor — the story

Eight JSON files. They are the **starting point** for one specific evening, not a live data source:
`StoryLoader` copies them into `trip.json` the first time a game is created, and from then on the
story is edited on the site at `/games/mystery/story` like everything else on this trip.

So an edit here only reaches a trip that has never seeded one. To pick up a change on a trip that
already has a story, edit it on the site — or discard and reseed, which is safe, because discarding
clears the evening and never the story.

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

## The prose is not written yet

**Every unwritten string is a row of dots.** That is the convention, and one predicate —
`MysteryText.IsPlaceholder` — reads it everywhere: the editor draws the field as a blank to fill in,
the Content gaps panel counts what is left, a player-facing screen leaves the line out rather than
showing dots to the room, and

```bash
grep -c '"\.\+"' data/braun-manor/*.json
```

says how much is outstanding.

What is **already real**, because the game cannot run coherently without it:

- every character's id, name and job
- which room each of the 21 stands in
- the faction layout, and which three are the killers
- which clue card sits in which room
- who has history with whom
- what the phase machine can ask of people, and when

All of it is changeable in the editor. It is real here so that the evening plays from day one and
the writing can happen against something that works.

---

## The structure, and what it is protecting

Nine of the tests in `MysteryStoryTests` assert facts about this content rather than about code.
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
