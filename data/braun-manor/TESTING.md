# TESTING.md — Solo Test Harness (no QR codes, no 25 phones)

## Principle: a QR code is just a URL, a phone is just a session

Every physical artifact in this game is a rendering of a URL or a session token. Test the URL, print the rendering Thursday night. Build **one dev-mode flag in the real app**, not a separate simulator — a simulator tests the simulator.

`DEV_MODE=true` enables everything below. Ship with it off.

## 1. Sessions without phones
- Join link per player: `/join?as=wilhelm&game=<id>`. Each browser tab = one phone. 21 tabs is heavy; you rarely need more than 4-5 open — the god console covers the rest.
- Real-device spot checks: your phone + laptop + iPad on the same game for the flows that must feel right in the hand (badge scan, share, vote).

## 2. Scan bypass
- Every scannable (badge, clue, tamper target) gets a dev-mode button/dropdown equivalent: `[DEV] scan badge → <player picker>`, `[DEV] scan clue → <clue picker>`.
- The handler is the same code path as the camera scan — bypass only replaces the camera input, never the logic.

## 3. God console (the core tool)
One page, four panels:
- **Puppet grid**: all 21 players, current state chip (zone-agnostic: role, faction, charges, hand size, alive/ghost). Click a player → act AS them: share any card, scan anything, vote, fire any ability, send ghost message.
- **State inspector**: raw game state — true kill count vs public counter, tamper states per clue with original/current text, all four graphs (badge, info-flow, clue-scan, ability log), prompt queue per player.
- **Timeline控制**: current round, manual advance, jump-to-round (re-runs unlock triggers idempotently), clock speed multiplier (1x / 10x / paused).
- **Generation panel**: seed input for reproducible games (`?seed=1234`), regenerate button, roster editor for no-show handling (drop players, regen morning-of).

## 4. Headless bot soak (the real test)
A script that plays full games via the same HTTP/WS API the phones use. Dumb policies are fine:
- villagers: vote weighted by public suspicion, share a random card sometimes
- detectives: sync random met-player, forensics a random clue, check highest-voted, vote accordingly
- killers: vote off-thread, use scrub in R4, share cover cards
- jesters: self-frame twice, volunteer for nomination
- minions: fire shield/decoy when triggered

Run `--games 100`. You are not testing strategy — you are testing that **no game ever wedges**. Assert after every action and at game end:

### Invariants (assert continuously)
1. Every game reaches `reveal` state; no round waits forever
2. Conviction count per trial is exactly 2 (or early-end fired on true count 3)
3. True kill count only ever increments on actual killer conviction; public counter may diverge only via shield/decoy
4. Shared charges (killer plant/scrub, minion loyalty) fire at most once globally — hammer with simultaneous requests, this WILL race
5. A clue holds max one tamper; second tamper attempt is a silent no-op
6. Sync rejects targets whose badge the detective never scanned
7. Ghosts cannot vote, cannot share cards, can only send canned messages
8. Dead players' unfired ability charges are void; killers' shared charge survives while any killer lives
9. Main screen clue text always equals latest-scan version, never auto-updates on tamper
10. Every player's phone renders post-murder: role, cover story (killers), witness cards populated
11. Generator invariants on every regen: 3 killers slot-valid + non-co-located, herrings exclude killers, lone-guilty-witness rejection, zone capacities met

## 5. Scenario checklist (manual, god console, ~1 hour)
Run each once, watch the screens:
- [ ] Tamper in R2 → banner appears at trial 1, not before
- [ ] Tamper a PUBLISHED clue → board unchanged until re-scan → re-scan updates + new finder shown
- [ ] Forensics on tampered clue → detective phone shows original text; board untouched
- [ ] Shield on convicted killer → card says ASSOCIATE, counter frozen, early-end still fires on true 3
- [ ] Decoy on convicted minion → card says KILLER, counter ticks; game does NOT early-end
- [ ] Public counter reads 3 via decoy, game continues → confirm no end-state bug
- [ ] Early end mid-trial-2 → jesters/Brauns unresolved → reveal handles them as losses cleanly
- [ ] Both Brauns nominated same trial; both convicted → neither wins; exactly one convicted → other flagged winner at reveal
- [ ] Jester convicted → card reads GUEST, game continues, endgame reveals win
- [ ] Tie at nomination cut and tie at conviction cut → resolution rules fire, no hang
- [ ] Detective convicted with unused check → charge void, no orphan prompt
- [ ] killer_check on shielded-dead killer → still answers truthfully
- [ ] Ghost haunt → target phone flickers, no info leak anywhere
- [ ] No-show regen at 20 and 19 players → game generates, zones satisfy minimums
- [ ] Kill the server mid-round, restart → state recovers (this WILL happen Friday)

## 6. Friday-morning drill
Attendance confirmed → enter real roster → regen with final N → print QR sheets (clues + badges) from the print route → one full 10x-speed bot game on the production instance → lock the seed.
