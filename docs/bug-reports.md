# Bug reports — quest tracking

Quest steering is inferred, not handed to us: Moonlight Peaks stores **no world position** on a
quest objective (only NPC / item / counter requirements + the objective text). So Dead Reckoning
reads the objective and works out where to send you — the quest NPC, the gather/mine node, the
named place, or a recipient named in the objective. Because it's inference, some quests need to be
taught individually. This file tracks the ones we know about and how to report new ones.

## How to report a mis-tracked quest

Post in the Nexus **Bugs** tab (or open an issue on the repo) with:

- **Quest name** — exact, as shown in the Quest Log (this is the single most useful field).
- **Objective text** — the line that's active when the skull misbehaves.
- **What the skull did** — led you to the wrong place / wouldn't lead at all / stopped short.
- **What was correct** — where the step actually wanted you to go (NPC, spot, item).
- Ideally, the `DR-QUESTPROBE` log line for that objective: set **Verbose logging = ON**
  (Diagnostics), hit **Seek Quest**, and grab the `DR-QUESTPROBE` block from
  `BepInEx/LogOutput.log`. It shows the objective's internal name, its NPC requirements, and its
  child node types — which is exactly what tells us how to resolve it.

## Known cases

### A Croak and a Crest — RESOLVED in 1.2.0

- **Objective:** shown as *"Bring a Copper Bar to the little pond in Moonlit Pines"*.
- **Symptom:** the skull led to the Moonlit Pines region and then stopped; the real target is
  **Yabbis**, an NPC sitting at the pond, but the visible title never names him.
- **Cause:** the objective carries **no NPC requirement** and the visible title's only gold token is
  the region ("Moonlit Pines"), so DR could only resolve the region. Yabbis' name lives only in the
  objective's **internal dev-name**: *"Bring a copper bar to Yabbis' pond in Moonlit Pines"*.
- **Fix:** `FindNpcMentioned` now scans the objective's `ObjectiveName` for a real, map-resolvable
  NPC and steers to them ahead of the region. Also in this quest, the earlier *"Mine Copper Ore in
  the Cave of Echoes"* step now points at the actual ore vein via `QuestNodeLocator`.

## Known limitations (by design, for now)

- **Delivery / turn-in objectives** where the recipient is *not* named anywhere in the objective
  (title or dev-name) and isn't an NPC requirement stay at **region level** — nothing in the data
  links the objective to its hand-in point, so DR can only get you to the area.
- **Gather/mine steps** resolve to a node only once you're **in the region** and only for nodes the
  loaded scene exposes (harvestables + mineable destructibles). A destructible's drop table is
  randomised and can't be read, so a mineable node is matched on its own placed item, not its drops
  — an oddly-named node can be missed.
