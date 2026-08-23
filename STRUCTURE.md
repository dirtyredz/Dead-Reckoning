# STRUCTURE — Dead Reckoning

<!-- Last full review: 2026-08-22 -->

Where things live in the **Dead Reckoning** mod and how the pieces fit. Pairs with
[README.md](README.md) (human quick-start) and the [docs/](docs/) set. This is a map of the *code
shape*; for how the tracking system actually works see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Overview

A BepInEx 5 / HarmonyX plugin for the Unity Mono game *Moonlight Peaks*. It spawns the in-game
soul-blob "skull" critter and steers it toward whatever the player is tracking (an NPC, a house/
place, a free-dropped map pin, or a quest objective), plus the on-screen and on-map feedback that
tells you what's being sought. Plugin source is **flat in `src/`** (no `src/DeadReckoning/`);
version is single-sourced from `src/DeadReckoning.csproj` via `GenerateModBuildInfo`.

Ships two files: `DeadReckoning.dll` + `track-icon.png`.

## Components

| Component | File | Responsibility |
|---|---|---|
| **Plugin entry** | `src/Plugin.cs` | `DeadReckoningPlugin`: BepInEx entry, config binding, `Harmony.PatchAll`, lazy Far Sight coexistence patch, `Log`. Creates the `SkullGuide` MonoBehaviour. |
| **Skull driver** | `src/SkullGuide.cs` | **The runtime core (God-file, ~1600 lines).** Target model + single-active-target invariant, critter spawn/despawn lifecycle, the per-frame `Update` loop, steering (leash, wall-avoid, ground-clamp, path-lead, idle wander), NPC picker UI, quest-objective resolution + quest HUD text, map double-click tracking + free pin, flame recolour, marker-highlight orchestration, diagnostics probes. See **Structural debt**. |
| **Quest node locator** | `src/QuestNodeLocator.cs` | Static. Bridges a "gather/mine `<item>`" objective to the world: reads the objective's `ItemAsset` from its `InjectionCollection`, then scans loaded `Interactable`s for a matching `IHarvestable`/`DestructibleView` node (the copper vein, the bush). Feeds `SkullGuide`'s quest resolution. |
| **Cross-room routing** | `src/RoomRouter.cs` | Static. BFS over `NavigationLibrary`'s room graph → world position of the door to head toward a target room. |
| **In-room pathing** | `src/PathGuide.cs` | Wraps the game's A* Pathfinding graph: a lead point a set distance along the walkable path to the target. |
| **Seeking HUD** | `src/TrackHud.cs` | Screen-space overlay: "Seeking: X" / the quest objectives list, with a ✕ stop control. Owns its canvas + Gelica font lookup. |
| **Map free-pin marker** | `src/MapPin.cs` | Red diamond + white ping waves drawn on the map at the pinned spot. |
| **Picker card highlight** | `src/PickerCardHighlight.cs` | Clones the NPC picker card's native selection frame, recoloured purple, + name-plate tint. |
| **Map badge highlight** | `src/MapMarkerTint.cs` | Recolours a tracked house/NPC map badge + adds ping waves (`DRMarkerTint` behaviour). |
| **Relationships Track button** | `src/RelationshipTrackButton.cs` | Harmony patch on `RelationshipDailyActivitiesWidget.Setup` → a Track pin in the daily-activity row (`DRTrackRef`). |
| **Quest Log Track button** | `src/QuestTrackButton.cs` | Harmony patch on `QuestScreen.ShowQuestInfo` → a "Seek Quest" button (`DRQuestButton`). |
| **Scroll coexistence** | `src/CameraScrollPatch.cs` | `WorldScrollBlock` flags + Harmony patches that stop the world camera zooming while the picker / Relationships panel is open, incl. the Far Sight mod coexistence postfix. |
| **Track icon loader** | `src/TrackIcon.cs` | Loads `track-icon.png` (config override → bundled) into a `Sprite`. |
| **Shared UI helpers** | `src/DRUi.cs`, `src/HoverScale.cs`, `src/DRIcons.cs` | `DRUi.FindDeep` (recursive transform search), `HoverScale` (pointer-hover scale animation, used by four callers), `DRIcons.BuildX` (draws an ✕). |

## Dependencies (direction of reference)

```
Plugin ──creates──▶ SkullGuide ──owns──▶ TrackHud, MapPin, PickerCardHighlight,
   │                    │                 MapMarkerTint, PathGuide, RoomRouter
   │                    └── reads config + Log from ◀── Plugin
   ├──PatchAll──▶ RelationshipTrackButton ─▶ SkullGuide.Instance, TrackIcon, HoverScale
   ├──PatchAll──▶ QuestTracker ────────────▶ SkullGuide.Instance, TrackIcon, HoverScale
   ├──PatchAll──▶ CameraScrollPatch (WorldScrollBlock ◀── set by SkullGuide)
   └──lazy patch─▶ FarSightCoexistPatch (in CameraScrollPatch.cs)
```

`SkullGuide.Instance` is the single shared entry point the Harmony-injected UI buttons call
(`ToggleTrack`, `ToggleQuest`, `IsTracked`, `IsQuestTracked`). Everything reads `DeadReckoningPlugin.Log`
and the `ConfigEntry` statics — that's an accepted hub, not debt.

## Key flows

- **Track something** → a setter (`SetTracked` / `SetTrackedRooms` / `SetPin` / `TrackQuest`) clears
  the other target kinds (single-active-target invariant) and calls `EnsureSkull`; the `Update` loop
  spawns/re-spawns the critter and `Steer()` drives it each frame.
- **Steer** → `TrackedWorldPos()` resolves the live target/door → `PathGuide`/`RoomRouter` shape the
  lead point → wall-avoid + ground-clamp → `Mover.Move`. No target ⇒ idle wander.
- **Quest** → `RefreshQuestTarget()` reads the current in-progress objective and resolves, in priority
  order: target NPC → gather/mine scene node (via `QuestNodeLocator`, only once you're in the region) →
  gold-token location → idle. It also stops + dismisses the skull once `QuestPersistence.IsCompleted`.
  A gather/mine node is a live `Transform` (`trackedNode`) the skull steers straight to.

## Conventions

- Flat `src/`; `pack.ps1` + `Directory.Build.props` are **workspace-synced canonicals** — never edit
  here (regenerated by `../../tools/sync-mod-files.ps1`). Version bumps go in the csproj only.
- Game integration is two kinds of patch: the **UI-injection** patches (`RelationshipTrackButton`,
  `QuestTrackButton`) are additive Postfixes that only *add* UI and call `SkullGuide.Instance`, never
  mutating the tracking path. The **camera-coexistence** patches in `CameraScrollPatch` are different
  on purpose — a Prefix (`GameCameraZoomBlockPatch`) that *skips* the game's zoom-toggle and a
  value-replacing Postfix on the scroll getter — because they must suppress input while our menus are
  open. Don't describe them all as "additive".
- Commit identity: `dirtyredz <dirtyredz@live.com>`.

## Structural debt

From the 2026-08-22 full review (componentization + abstraction Sonnet lenses + a Codex cross-model
pass). Full triage with priorities in [docs/BACKLOG.md](docs/BACKLOG.md).

**Fixed in the review pass (mechanical, compile-verified):**
- ✅ **`FindDeep`/`FindChildDeep` de-duplicated.** Was 5 identical copies across 4 files
  (`MapMarkerTint` had it *twice*). Now one `DRUi.FindDeep`.
- ✅ **`HoverScale` extracted** from `RelationshipTrackButton.cs` into its own `HoverScale.cs` (it's a
  generic UI behaviour with four callers).
- ✅ **`QuestTracker.cs` renamed** to `QuestTrackButton.cs` to match its contents (the Quest Log
  button UI, not the quest-tracking logic, which lives in `SkullGuide`).
- ✅ **`RouteToNearestRoom` moved** from `SkullGuide` into `RoomRouter.DoorTowardFirstReachable`
  (accurate name — it returns the first *reachable* room in list order, not the nearest).
- ✅ **`MapMarkerHighlight.cs` renamed** to `PickerCardHighlight.cs` — it highlights NPC picker
  cards, not map markers.

**Open — the big one (backlogged, needs its own focused passes; do NOT drive-by):**
- **`SkullGuide.cs` is a ~1580-line God-file** (~2× the 800-line cap) spanning ~9 responsibilities.
  The review's recommended extraction order (the target model first, because it shrinks every other
  seam's diff):
  1. **Target model** → a `TrackingSelection` (what the user chose) + `ResolvedDestination` (where to
     steer) pair. Today it's **10** parallel fields (a `trackedNode` gather/mine target was added) with
     a "clear the other kinds" invariant hand-repeated across **~9** sites (`RefreshQuestTarget`'s apply
     block clears inline four times) — the exact shape that caused the past "stale free-pin shadowed an
     NPC" bug, and now grown further. This is the top extraction target. **P1.**
  2. **Quest-objective resolution** → `QuestObjectiveResolver` + a separate HUD formatter (it reads
     persistence/scene/time and parses rich text — not "pure/static" as first assumed). **P1.**
  3. **NPC picker UI** *and* **map-tracking input** → two controllers that issue tracking commands.
     ~350–400 lines. **P1.**
  4. **Feedback orchestration** (per-frame marker/HUD/pin scanning, `UpdateTrackedHighlights` etc.) →
     a presenter fed an immutable snapshot. **P1.**
  5. **Diagnostics probes** → keep each probe beside its subsystem (they touch quests, materials, UI
     trees, physics — a single `DrProbes` would just be another grab-bag). **P2.**
  6. **Flame recolour** → its own helper when the recolour feature (Phase 6) grows. **P2.**

**Open — smaller items (backlogged):**
- `SkullGuide.Instance` is a concrete service-locator the Harmony-injected buttons depend on, with no
  target-change event (`RefreshAll` scene-scans). Expose a narrow facade + `Changed` event once the
  target model is extracted. **P2.**
- `DRMarkerTint` holds three positionally-coupled parallel lists (`imgs`/`bases`/`cols`) — fold into
  one binding type. **P2, cheap.**
- The **ping-wave animation** is duplicated between `MapPin` and `DRMarkerTint` (`MapMarkerTint`) —
  identical ripple math. A narrow `PingWaves` helper is the real shared seam (`PickerCardHighlight`
  has no ping). Extract only if a third consumer appears. **P2.**

_Living doc — refresh with /project-docs when it drifts._
