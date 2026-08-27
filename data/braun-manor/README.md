# Braun Manor — Game Data & Generator Spec

Six data files, treated as tables. Nothing in them is written at runtime; the generator composes, never authors.

| File | Contents |
|---|---|
| `characters.json` | 21 characters: identity, motive, fear, signature item, guilty/innocent acts, seen-texts, herring exoneration, trace clue, zone whitelist, slot tags |
| `zones.json` | 9 zones, adjacency, access routes (corridor / window / side path), capacities, clue spots |
| `factions.json` | 7 factions incl. ghosts: counts, knowledge webs, win conditions, abilities with charges + unlocks |
| `rounds.json` | Full schedule (~110 min), trial procedure (top-4 nominate → defend → top-2 convicted), reveal-card policy |
| `story_beats.json` | Spine, 5 method beats, 3 access-route beats, signature beat, conviction cards, endgame templates, assembly rules |
| `prompts.json` + `ghosts_npcs.json` | Prompt engine templates + badge telemetry; ghost canned set; Braun & facilitator standing orders |

## Cast changes for the guest list (12F / 9M)

- **Cut:** Jacob Kruse (pilot) — thinnest observable, nothing else references him
- **Now female:** Sutton Brady (jazz singer, still Braun's ex), Carla Lucciano (chauffeur), Dr. Aiko Nishimoto (family doctor)
- **Result:** 12 female roles (Isla, Molly, Emilia, Imogen, Priya, Florence, Martha, Sharkeisha, Giuliana, Sutton, Carla, Nishimoto), 9 male (Harry, Hugo, Yousef, Cairo, Remington, Santiago, Da'Quan, Solomon, Wilhelm)
- Jou/Ali/Kyle/Em → Braun + Leo + Chloe + Bertram

## Generator algorithm

```
1. PLACE      each character → one zone from their whitelist,
              respecting zone min/max capacities
2. VALIDATE   ≥2 access-tagged characters landed in access-granting zones
              (entry, family_room, lawn, driveway); every zone ≥ min capacity
              → else reshuffle
3. DRAW KILLERS
              ACCESS  = random(access-tagged ∩ access-granting zone ∩ not inheritance)
              MEANS   = random(means-tagged ∩ not chosen ∩ not inheritance)
              SIGNATURE = random(signature-tagged ∩ not chosen ∩ not inheritance)
              constraint: no two killers share a zone → else redraw
4. DRAW HERRINGS
              3 innocents get their GUILTY variant; weight toward
              slot-tagged characters so herrings resemble killers
5. DRAW FACTIONS (from the 16 remaining non-killer, non-inheritance):
              2 minions (weight: signature-tagged), 3 detectives,
              2 jesters (weight: ruin-fear class), 9 villagers
6. SET VARIANTS
              killers + herrings → guilty acts/seen; all others → innocent
7. BUILD TEXTS (pure template fill from story_beats.json):
              study scene   = base + method_beat[MEANS].scene_flavor
              killer briefs = slot beat + cover_story(assigned herring)
              witness stmts = per player: seen[variant] of 2-3 co-located
              braun dig-dirt = rival's seen.guilty (always guilty reading)
              endgame texts = every conditional branch pre-filled
8. PRINT      9 clue QR sheets: each GUILTY character's trace in their zone;
              remaining zones take neutral spine clues (tea service, dress,
              scene) so every zone has exactly one
9. EXPORT     per-player packets + main-screen script + facilitator sheet
              (facilitator consoles show the guilty list and killer draw)
```

## Why modular blocks, not pre-written full stories

Placement × killer draw × herrings is millions of distinct games. Full-story pre-generation is impossible and unnecessary: every sentence a player ever sees is one of these blocks with names filled in. ~200 authored blocks → total coverage, deterministic compile, reviewable in one sitting. Optional AI restyle pass may adjust voice per character's `voice` field — facts are locked.

## Balance snapshot (6 conviction slots)

| Faction | Win | Est. |
|---|---|---|
| Town (9 villagers + 3 detectives) | 2+ killers convicted | re-sim |
| Killers + minions (5) | 2+ killers survive | re-sim |
| Jester (each) | be convicted | ~50% |
| Braun (each) | rival convicted + survive | ~30% |

**Ruleset B is settled**: killers win on 2+ of 3 surviving, town wins on 2+ of 3 convicted. Those are
exhaustive and mutually exclusive across the 6 conviction slots — 0 or 1 convicted is a killer win,
2 or 3 is a town win, and there is no outcome where nobody wins.

The two `~45%` / `~55%` figures that used to sit in this table are gone rather than adjusted. They
were computed against a mixed reading — town needing a clean 3-for-6 while killers needed only 2
surviving — which left a dead zone at exactly 2 convicted and is not the ruleset any more. Town now
needs 2 of 6 rather than 3, so its odds went up by an amount nobody has measured. Re-run the
simulation before trusting a number here.

What has not changed: town is working against herrings, blame-take and false plants, and the
detective toolkit (6 syncs, 3 forensics, 3 hard questions) is what pulls it back toward even. Braun
is the hard seat by choice — no fallback, pure knife-fight, and the dig-dirt ability is their
equalizer.

## Flags for build time

1. **Blame-take + killer_check interaction is specified** (check tells the truth, blame only fools the reveal card) — keep it that way or detectives lose their only ground truth.
2. **Ghost silence is a hard rule.** Canned messages only. The first time a dead player free-texts "it's Wilhelm," the game is over.
3. **Facilitator consoles see everything.** They are the manual override for the 5% the prompt engine misses — and the reason a shy-player game still works.
4. **Playtest seeds on paper.** Generate 3 seeds, read the witness-statement webs, confirm each killer has ≥2 threads pointing near them. A seed where the SIGNATURE killer drew Martha (ledger) plays very differently from one where it drew Sutton (pins).

## Generator rules added after batch simulation (2000-seed runs)

1. **Dual-tag half-weighting:** in each slot draw, weight candidates by 1/len(slots). Without it, Solomon/Wilhelm sit at ~30% killer rate.
2. **Lone-guilty-witness rejection:** reject any seed where a killer's only co-located witness also drew a guilty variant (herring/minion). Costs ~0.3 reshuffles per seed.
3. **Route preference:** if the ACCESS killer has `route_preference`, it overrides the zone's route.
4. **Clue spillover:** if a zone already holds a clue, portable traces (no anchor_zone) shift to an adjacent clueless zone. Anchored traces never move.
5. **Cross-zone sighting (recommended):** any killer with exactly 1 co-located witness gets one additional observation assigned to a player in an adjacent zone ("seen from the doorway"). Keeps every killer at >=2 threads.

Post-fix killer distribution: 12-24% band across 15 eligible characters (uniform ideal ~16%).
Data changes applied: Carla is access-only with kitchen added to her whitelist (was 46% killer rate); her snail glove button stays — a snail-carrier who can never be the signature killer is a free red herring on the signature thread.
Never-killers by design: Molly, Emilia, Sharkeisha, Remington (no slots) + Harry, Isla (fixed inheritance). Irrelevant for a one-shot; a meta risk only on replays with the same group.
