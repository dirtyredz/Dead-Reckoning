# Changelog — Dead Reckoning

Written for us. The player-facing wording lives in [NEXUS.md](NEXUS.md); this file names the
subsystems, the game types leaned on, and the things that were tried and dropped.

One entry per **released** version, not per build — see
[12-versioning-and-release.md](../../12-versioning-and-release.md).

## 1.0.0

Published as [Nexus mod 144](https://www.nexusmods.com/moonlightpeaks/mods/144) on 2026-08-21.

First release. Built and iterated as `0.1.0`, collapsed into this single entry.

A replacement for lockyaw's
[On-screen Quest and Character Tracker](https://www.nexusmods.com/moonlightpeaks/mods/48): instead
of a directional icon pinned to the screen edge, a floating skull follows you through the world and
drifts toward whatever you are seeking, so you physically walk after it.

### Added

- **The skull is the game's own "soul blob" critter**, spawned at runtime via
  `CritterView.SpawnCritter(pos, soulblobItemAsset, gridSelector, startRandomState:false)`. The
  item assets and `GridSelector` come from the in-scene `SoulblobSpawner` (`.SoulBlobs`,
  `.Selector`); variant 0 is the skull (`SoulblobIndex` exposes the rest). Its own
  `SoulblobCritterBehaviour` wanders and flees the player, so it is disabled after spawn and the
  skull is steered by feeding `Mover.Move` each frame — `SoulblobMovementBehaviour` applies fed-in
  movement straight to the transform, while the bob comes from a separate `SoulblobAnimation`, so it
  keeps floating while driven.
- **Save-safe by design.** `CritterView.IsRegisteredInPersistence == false` — spawning a critter
  writes nothing to the save, so there is no cleanup and no risk to a file. (The creature/
  `CreatureView` path, tried first with the Draculamb, does write persistence and was abandoned for
  this reason.)
- **Four things to seek, one active at a time:**
  - An **NPC** — the game's own picker (`UIScreen<PickNpcScreen>`, default F6), or a **Track button**
    added to every card on the Relationships panel.
  - A **quest objective** — the active quest's target NPC or location, read from
    `GamePersistence.Instance.QuestObjectives`.
  - A **house or place** — double-click a badge on the open map to seek it.
  - A **free pin** — drop one anywhere on the map and the skull points at that spot.
- **Cross-room routing** (`RoomRouter`): a BFS over `NavigationLibrary.Rooms[].RoomSwitches` picks
  the door toward the target's room, so the skull leads you through the house rather than pushing at
  a wall.
- **Follows the walkable route** (`PathGuide`), computing an in-room obstacle-avoiding path with the
  game's A* Pathfinding Project so it leads you around furniture and walls instead of down a straight
  line. Falls back to the direct line (`FollowPath = false`).
- **Steering that reads on screen:** the skull sits on the 3D torso→torso line to the target,
  respects house-wall collision (spherecast vs the Obstacle layer only — house walls are Ground-layer
  and snagged floors otherwise), keeps a minimum clearance off the ground, and holds a standoff
  distance from the player. A distance leash snaps a stuck skull back to your side, and its speed
  scales with the player's (so it keeps up in cat-form sprint).
- **On-screen "seeking" text** naming the current target, and **map visuals** for what is tracked —
  a coloured outline on the tracked badge (`MapMarkerHighlight` / `MapMarkerTint`) and a diamond
  "ping" for a free pin (`MapPin` / `MapMarkerHighlight`).
- **Recolour the soul blob's flame** to a fixed hex colour (`RecolorFlame` / `FlameColor`), which
  also stops it varying between spawns.
- **Far Sight coexistence** (`CameraScrollPatch`): if the Far Sight zoom mod is installed, a
  reflection Harmony patch makes its `IsGameplay()` return false while our picker / Relationships
  panel is open, so its scroll-zoom stands down over our UI. Far Sight loads *after* this mod, so the
  patch is applied lazily from the first in-game frame, not `Awake`.
- Settings under **General / Follow tuning / Diagnostics**, surfaced in Mod Nook and Mod Menu.
  `VerboseLogging` defaults **off**.
