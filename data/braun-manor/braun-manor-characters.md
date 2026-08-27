# Braun Manor — Character Roster & Variant Data

## The Spine (fixed, every game)

James Braun had assembled evidence against the **Midnight Syndicate**. He was going to hand it to a journalist within the week. The Syndicate marks its people with a **snail** — lapels, flasks, embroidery, letterhead watermarks. Braun died in his study at approximately **9:40 PM**. The study is reachable only through the **Entry** or the **Family Room**.

The west wing (guest bedrooms) is roped off for the night. Only the Grand Hall Extension Room at its far end is open.

---

## Data Model

Each character carries:

- **Zone** — fixed. Generates witness statements automatically between co-located characters.
- **Motive** — fixed. Their reason to want Braun gone. Public eventually; everyone has one.
- **Fear** — fixed. What they don't want found out. Drives evasiveness even when innocent.
- **Observable** — the one thing other people noticed them doing. **Two readings: GUILTY and INNOCENT.** Only this randomizes.
- **Slots** — which guilt slots this character is eligible for.

Slots: **A** = Access (near the study, lied about why) · **M** = Means (had the method) · **S** = Signature (carries the snail)

Generator draws 3 killers, one per slot, with validation: no two in the same zone, at least one outside the house, never both Brauns. Killers get GUILTY readings. Then draw **3 red herrings** among innocents who also get GUILTY readings. Everyone else gets INNOCENT.

---

## ENTRY — the chokepoint

### 1. Wilhelm Shepard — Politician · Slots: A, S
**Zone:** Entry, just outside the study door
**Motive:** Took Syndicate bribes for favorable policy. Braun's evidence names him.
**Fear:** That anyone finds the handkerchief.
**Observable — the handkerchief:**
- *GUILTY:* He was wiping his hands over and over with an embroidered handkerchief. Snails on it. He pocketed it fast when he noticed he was being watched, and his hands were still shaking after.
- *INNOCENT:* He was sweating through a handkerchief and pacing, rehearsing a pitch under his breath. He kept checking the study door because he'd been waiting forty minutes for Braun to see him and was getting humiliated about it.

### 2. Imogen Durham — Maid · Slots: A
**Zone:** Entry, sent away from the study
**Motive:** The Syndicate offered her money for information on the household.
**Fear:** That someone finds out she took their first payment already.
**Observable — the tea tray:**
- *GUILTY:* She was carrying tea to the study and came back without it, and without the tray. When asked, she said she'd been sent to clean the Grand Hall — but nobody in the Grand Hall saw her arrive.
- *INNOCENT:* She was carrying tea to the study when Wilhelm stopped her and ordered her to go deal with a spill in the Grand Hall. She was visibly annoyed about it and left the tray on the Entry console.

### 3. Yousef Mostafa — Family Attorney · Slots: A
**Zone:** Entry / hallway
**Motive:** In love with Isla. Drafted the prenup he'd rather she never signed.
**Fear:** That the joined bank accounts predate the prenup, which is his malpractice.
**Observable — the folder:**
- *GUILTY:* He was in the corridor with a document folder, and later he did not have it. He gave three different answers about where he'd been standing.
- *INNOCENT:* He was loitering in the corridor with a folder under his arm, clearly waiting to catch Isla alone. He looked mortified when anyone acknowledged it.

---

## FAMILY ROOM

### 4. Santiago Costa — College Professor · Slots: A
**Zone:** Family Room, among Braun's displayed collections
**Motive:** Braun refused him access to a manuscript he'd spent nine years chasing.
**Fear:** That the university already knows about the last piece he "borrowed."
**Observable — the cabinet:**
- *GUILTY:* He was crouched at the collection cabinet with the glass open. He was wearing gloves indoors, at a party.
- *INNOCENT:* He was nose-to-glass at the collection cabinet, talking out loud to nobody about provenance. He'd been there for the better part of an hour and hadn't touched a drink.

### 5. Jacob Kruse — Pilot · Slots: A
**Zone:** Family Room, drifting
**Motive:** Braun bought his airline out of bankruptcy for pennies and kept the name.
**Fear:** That people find out he came here to beg for a job.
**Observable — the drifting:**
- *GUILTY:* He kept wandering out of the Family Room toward the study corridor and back, three or four times, always with a drink in hand as a reason to be moving.
- *INNOCENT:* He was drifting room to room looking for somewhere quiet to make a phone call. He smelled strongly of the liquor from the Extension Room and was not steady.

---

## KITCHEN

### 6. Molly Henderson — Newspaper Reporter · Slots: —
**Zone:** Kitchen, interviewing staff
**Motive:** Braun promised her the Syndicate story and then went quiet on her.
**Fear:** That she's already published one piece she can't fully source.
**Observable — the notebook:**
- *GUILTY:* She was pressing the kitchen staff hard about the study — who goes in, when, whether it locks.
- *INNOCENT:* She was working the kitchen staff for Syndicate gossip and writing down everything. She'd found the same snail insignia across three sets of documents and was excited about it to the point of being annoying.

### 7. Nishimoto Hakamori — Family Doctor · Slots: M
**Zone:** Kitchen, then out to the Driveway
**Motive:** Braun found out his U.S. credentials were never completed.
**Fear:** Exactly that.
**Observable — the case:**
- *GUILTY:* He was carrying his medical case out to his car in the middle of the party. He'd drawn blood from Braun earlier — but the vials in the case, when he set it down, didn't have labels on them.
- *INNOCENT:* He was moving his case to the car so he wouldn't forget it. He'd drawn Braun's blood earlier in the evening and was taking it to the lab in the morning. He complained about the errand to anyone who'd listen.

---

## SITTING ROOM / STAIRS

### 8. Isla Perry — Braun's Fiancée · **Inheritance** · Slots: —
**Zone:** Sitting Room, near the stairs
**Motive:** The prenup. She'd been told about it that week.
**Fear:** That people learn she'd already asked Yousef whether it could be voided.
**Observable — the dress:**
- *GUILTY:* She went upstairs in one dress and came back down in another. She said champagne got on it — but the champagne tower didn't go over until after she'd already changed.
- *INNOCENT:* She went up to change because the champagne tower came down and soaked her. She was furious about it and said so at length.

### 9. Emilia Cruz — Famous Actress · Slots: —
**Zone:** Sitting Room, warming up
**Motive:** Braun pulled funding from the film that was her break.
**Fear:** That people know she hasn't booked anything in two years.
**Observable — the exit:**
- *GUILTY:* She left the Sitting Room after Sutton took the microphone and wasn't seen again for close to twenty minutes. She won't say where she went.
- *INNOCENT:* She was doing vocal warm-ups alone, then stormed out when she heard Sutton start singing her set. She was found crying in the stairwell.

### 10. Giuliana Andolpho — Socialite · Slots: A, S
**Zone:** Sitting Room, moving through the house
**Motive:** Braun's evidence goes to the top of the Syndicate. That's her.
**Fear:** That anyone gets a good look at the flask.
**Observable — the silver:**
- *GUILTY:* She came out of the Extension Room flustered and there was something silver in her hand that she moved out of sight. Later, coming down the stairs, she was straightening her sleeve over her wrist. She'd been in three parts of the house in ten minutes.
- *INNOCENT:* She walked in on Cairo and Priya in the Extension Room and came out mortified, fanning herself with a silver cigarette case. She spent the next ten minutes telling people about it.

---

## GRAND HALL

### 11. Sutton Brady — Jazz Singer · **Minion** eligible · Slots: S
**Zone:** Grand Hall, performing
**Motive:** Braun's ex-lover. Believes Braun never stopped.
**Fear:** That the affair becomes public in front of Isla.
**Observable — the lapels:**
- *GUILTY:* Gold snail lapel pins. Custom work, not costume. He changed the subject twice when asked about them and covered them with his hand.
- *INNOCENT:* Gold snail lapel pins he'd borrowed off the event decorator an hour before. He kept complaining they were tacky and asking if he should take them off.

### 12. Martha Kim — Detective in Training · **Detective** · Slots: S
**Zone:** Grand Hall, working the room
**Motive:** Braun stonewalled her department's inquiry.
**Fear:** That Remington finds out she's been building the file behind his back.
**Observable — the questions:**
- *GUILTY:* She was asking very specific questions about the Syndicate to people who had no reason to know anything — as if checking who'd been told what.
- *INNOCENT:* She was aggressively networking, trying to get on Remington's good side by bringing him Syndicate leads. It was transparent and slightly embarrassing.

### 13. Hugo Hahn — Movie Director · **Jester** eligible · Slots: M
**Zone:** Grand Hall, on the phone
**Motive:** Braun's funding pull bankrupted his production company.
**Fear:** Who he borrowed from instead. And what they do about late payment.
**Observable — the call:**
- *GUILTY:* He took a call, went very quiet, and afterward asked two separate people whether Braun was still in the study. He had no reason to need to know.
- *INNOCENT:* He took a call from his team and came back grey. He told at least one person, unprompted, that he was finished and that it didn't matter anymore.

### 14. Sharkeisha Noh — Famous Painter · Slots: —
**Zone:** Grand Hall, at her easel
**Motive:** Braun hired her to work the party she thought she'd been invited to.
**Fear:** Nothing much. She's the cleanest person here and that itself is suspicious to some.
**Observable — the canvas:**
- *GUILTY:* Her canvas has a figure in the study doorway that she painted over. The underpainting is still visible if you look at it in the light.
- *INNOCENT:* She was set up at her easel all evening, sulking about being asked to work. She notices details — she can tell you exactly who was wearing what and where they stood.

---

## EXTENSION ROOM

### 15. Cairo Iwobi — Neighboring Millionaire · Slots: M
**Zone:** Extension Room
**Motive:** A property survey found he'd been using Braun's land. He had to give it back.
**Fear:** That his wife finds out about Priya.
**Observable — the cellar door:**
- *GUILTY:* He had a key to the Extension Room's storage that he shouldn't have had, and he was in there twice.
- *INNOCENT:* He was making out with Priya in the Extension Room and got walked in on. He has spent the rest of the night trying to control the story.

### 16. Priya Patel — Golf Pro · Slots: —
**Zone:** Extension Room
**Motive:** Her father is a former Syndicate member. Braun's evidence would name him.
**Fear:** Her father's name coming up at all.
**Observable — the reaction:**
- *GUILTY:* When the Syndicate came up in conversation she went white and left the room. Twice.
- *INNOCENT:* She was with Cairo in the Extension Room and got walked in on by Giuliana. She thought she saw a snail-shaped flask in Giuliana's hand and has been quietly unsure whether to say so.

### 17. Solomon Roka — Bootleg King · **Minion** eligible · Slots: M, S
**Zone:** Extension Room, running the liquor
**Motive:** Braun planned to expose his distribution network.
**Fear:** The manifest in his coat pocket.
**Observable — the entrance:**
- *GUILTY:* He came into the house through a side entrance instead of the front, carrying two cases. One of those cases never made it to the bar.
- *INNOCENT:* He came in a side door because the front was jammed with valet traffic and he was hauling crates. He's been pushing free samples on everyone since, loudly.

---

## DRIVEWAY

### 18. Remington Whitley — Police Detective · **Detective** · Slots: —
**Zone:** Driveway, walking the cars
**Motive:** He wanted to be the one to break the Syndicate. Braun was going to the press instead.
**Fear:** That his own department's leak traces back through him.
**Observable — the cars:**
- *GUILTY:* He was going car to car in the driveway with a flashlight, looking through windows, well before anyone died.
- *INNOCENT:* He was walking the cars out of habit, checking plates against a list he keeps. He'll admit to this readily and thinks it's normal.

### 19. Carlo Lucciano — Chauffeur · Slots: A, S
**Zone:** Driveway, valet and door
**Motive:** Loyal to the Syndicate. He does what he's told.
**Fear:** Nothing. That's what's unnerving about him.
**Observable — the side of the house:**
- *GUILTY:* He escorted a guest around the side of the mansion, away from the entrance, and was gone long enough that arrivals backed up. He came back alone and would not say who it was.
- *INNOCENT:* He walked a badly drunk guest around the side so they could be sick out of sight of the door. He was short and impatient about it and complained the whole time.

---

## LAWN

### 20. Harry Braun — Estranged Brother · **Inheritance** · Slots: A
**Zone:** Lawn, smoking
**Motive:** Removed from the family estate. Came to make a case for getting back on it.
**Fear:** That people learn he was removed for cause, not for a falling-out.
**Observable — the walk:**
- *GUILTY:* He was outside smoking, but he was on the study side of the house, under the window, and there is a scatter of cigarette ends there.
- *INNOCENT:* He was outside smoking and watching the door, working up the nerve to go in. He was there long enough to see everyone who came and went from the driveway.

### 21. Florence Li — Investment Banker · Slots: M
**Zone:** Lawn, in the garden beds
**Motive:** Braun's estate audit would surface accounts she manages.
**Fear:** That someone asks what she keeps in the clutch.
**Observable — the garden:**
- *GUILTY:* She was cutting something from the far bed — not roses — and put the cuttings into her clutch rather than leaving them with the flowers.
- *INNOCENT:* She was cutting roses in the dark because she'd rather be gardening than at the party. She apologized for being antisocial to three separate people.

### 22. Da'Quan Wilson — Personal Trainer · Slots: —
**Zone:** Lawn, then in and out
**Motive:** Braun ignored his program and cost him a marketing win before the wedding.
**Fear:** That the certification he brags about lapsed in March.
**Observable — the window:**
- *GUILTY:* He was standing on the lawn looking in through the windows on the study side and stayed there a long time.
- *INNOCENT:* He was outside working up the nerve to ask Florence to dance. From where he stood he could see the corridor windows, and he saw someone pacing in front of the study for a long stretch.

---

## Facilitators (not playable, not suspects)

- **Leo Raphael — French Chef** (Kitchen). Nudges toward the snail thread if it hasn't surfaced.
- **Chloe Zimmerman — Fashion Designer** (Grand Hall). Keeps the room mingling, watches for wallflowers.
- **Bertram Ault — Estate Butler** (roaming). Handles the lights, the clue drop, and the study.

---

## Clue QR Placement — one per zone

| Zone | Clue names a detail, never a person |
|---|---|
| **Study** | The scene. Blood, no body. A cold cup of tea. A drawer forced open and empty. |
| **Entry** | An abandoned tea tray on the console. Two cups, not one. |
| **Family Room** | The collection cabinet is unlocked. One shelf has a clean rectangle in the dust. |
| **Kitchen** | A delivery manifest with a snail watermark, initialed by someone. |
| **Sitting Room** | A champagne-stained dress bagged in the stairwell closet. Stain is on the back. |
| **Grand Hall** | A dropped gold lapel pin. Snail. Custom hallmark on the reverse. |
| **Extension Room** | A silver flask, snail-shaped, half full. Contents smell wrong. |
| **Driveway** | Tire marks from a car that pulled around the side and back. Cigarette ends beside them. |
| **Lawn** | A cut stem in the far bed. Not a rose. Fresh. |

Each is inert until you find the person who knows whose it is. That's the trading economy.

---

## Balance Summary

| Faction | Count | Win condition | Est. rate |
|---|---|---|---|
| Killers | 3 | 2+ survive all three trials | ~45% |
| Minions | 2 | Same as killers | ~45% |
| Detectives | 3 | 2 of 3 killers convicted | ~55% |
| Villagers | 10 | Same as detectives | ~55% |
| Jesters | 2 | Be convicted at any trial | ~30% each |
| Inheritance | 2 | Rival convicted + survive; **or** fewer total votes if neither convicted | ~50% each |

Jester and Inheritance wins are **silent and non-terminal**. They do not end the game. All winners are revealed together at the end.
