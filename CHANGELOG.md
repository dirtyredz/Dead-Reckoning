# Changelog — Dead Reckoning

Written for us. The player-facing wording lives in [NEXUS.md](NEXUS.md); this file names the
subsystems, the game types leaned on, and the things that were tried and dropped.

One entry per **released** version, not per build — see
[12-versioning-and-release.md](../../12-versioning-and-release.md).

## 1.2.0

Quest tracking gets precise — it points at the actual node/recipient, not just the region — and the
idle "not seeking" behaviour is reworked so it reads as clearly different from leading.

### Fixed

- **A completed quest no longer leaves the skull seeking it forever.** `RefreshQuestTarget` resolved
  a target only from *in-progress* objectives; once the quest finished none were in progress, so it
  fell through to idle but `trackedQuestNode` stayed set — the skull kept wandering with the quest
  still on the HUD. Now it polls `QuestPersistence.IsCompleted` each refresh and `ClearTarget()`s
  (dismisses the skull) when the quest is done.

### Added

- **Gather/mine objectives point at the real node in the scene** (`QuestNodeLocator`, new). The game
  stores no world position on an objective — only NPC/item/counter requirements plus the region name
  in the title — so "Mine Copper Ore in the Cave of Echoes" could only ever resolve to the *region*.
  Now, once you're in that region, DR reads the objective's `ItemAsset` from its
  `SpeechInjectionCollection` (the asset behind the title's item token — no rich-text colour parsing)
  and scans loaded `Interactable`s for a matching node: an `IHarvestable` (bush/tree/pickup, matched
  on its `ItemAsset`) or a mineable `DestructibleView` (matched on its placed grid item). It steers
  to the nearest match (`trackedNode`, a live `Transform`) and reuses it until it's harvested, to
  avoid flip-flopping between two veins. **Delivery/turn-in objectives are deliberately left at
  region level** — nothing links an objective to its hand-in zone, and a destructible's loot is a
  randomised, event-driven `LootTableAsset` that can't be read without side effects.
- **Recipients named only in an objective's internal dev-name are now sought** (`FindNpcMentioned`).
  Some objectives carry no NPC requirement and hide the recipient from the visible title — e.g. the
  "A Croak and a Crest" delivery shows "the little pond in Moonlit Pines" but its `ObjectiveName` is
  literally "Bring a copper bar to Yabbis' pond in Moonlit Pines". DR now scans the dev-name for a
  real, resolvable NPC (whole-word match against `NpcLibrary`, last mention wins so the "to X"
  recipient beats an earlier mention) between the NPC-requirement and gold-token steps, and steers
  to them instead of the vague region.
- **Two idle-follow tuning knobs** — `IdleFollowSpring` and `IdleFollowDamping` (Follow tuning
  section) — expose the feel of the new idle behaviour below so it can be dialled in-game.

### Changed

- **Idle ("not seeking") no longer trails you at a fixed standoff.** It used to drift-wander around
  you at the tracking distance, which read almost identically to leading. It now loosely *follows
  you* via a spring-damper with its own momentum (`idleVel`): it lags when you run, then springs to
  catch up and gently overshoots — free-floating, never a locked gap. The rest target is the nearest
  point of a horizontal sphere around you (radius = hover distance), so a skull that's lagged to your
  south settles to your south instead of flying over your head to a fixed overhead spot. Leading
  ahead (seeking) vs. floating around you (idle) is now the clear "am I seeking?" tell.

## 1.1.0

Quest tracking now seeks **location** objectives, not just NPC ones.

### Fixed

- **A quest objective that points at a place (e.g. "Go to the Town Hall") is now sought.**
  `RefreshQuestTarget` only ever resolved an objective to an NPC — its required-NPC list, or the
  gold-coloured (`#FCEBAE`) target name in the title resolved through `FindNpcByName`. A location
  token resolved to no NPC, so the skull fell back to idle wandering instead of leading there. The
  1.0.0 notes claimed "target NPC or location"; only the NPC half actually worked.

### Added

- **Place resolution for quest targets** (`ResolveQuestLocation` / `FindLocationRooms`): when the
  gold token isn't a character, it's matched against the map's `MapLocationMarker`s — exact name
  first, then a looser contains match — over each marker's shown name, raw localized `LocationName`,
  and member room names, falling back to any `RoomAsset` whose own `GetRoomName` matches for places
  with no labelled marker. The resolved rooms drive the existing house/place path: `RoomRouter`
  leads door-to-door and the skull idles once you're inside. Cached by name (only hits are cached,
  so it retries until the map / `NavigationLibrary` is loaded), and `SameRoomSet` keeps the route
  from resetting each refresh tick. As a side effect the tracked quest location now also lights up
  on the map, the same as a tracked house.

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
