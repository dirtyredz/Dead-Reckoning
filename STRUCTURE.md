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
| **Cross-room routing** | `src/RoomRouter.cs` | Static. BFS over `NavigationLibrary`'s room graph → world position of the door to head toward a target room. |
| **In-room pathing** | `src/PathGuide.cs` | Wraps the game's A* Pathfinding graph: a lead point a set distance along the walkable path to the target. |
| **Seeking HUD** | `src/TrackHud.cs` | Screen-space overlay: "Seeking: X" / the quest objectives list, with a ✕ stop control. Owns its canvas + Gelica font lookup. |
| **Map free-pin marker** | `src/MapPin.cs` | Red diamond + white ping waves drawn on the map at the pinned spot. |
| **Picker card highlight** | `src/MapMarkerHighlight.cs` | Clones the NPC picker card's native selection frame, recoloured purple, + name-plate tint. |
| **Map badge highlight** | `src/MapMarkerTint.cs` | Recolours a tracked house/NPC map badge + adds ping waves (`DRMarkerTint` behaviour). |
| **Relationships Track button** | `src/RelationshipTrackButton.cs` | Harmony patch on `RelationshipDailyActivitiesWidget.Setup` → a Track pin in the daily-activity row. Also hosts the shared `HoverScale` + `DRTrackRef`. |
| **Quest Log Track button** | `src/QuestTracker.cs` | Harmony patch on `QuestScreen.ShowQuestInfo` → a "Seek Quest" button (`DRQuestButton`). *(Name is misleading — the actual quest-tracking logic lives in `SkullGuide`.)* |
| **Scroll coexistence** | `src/CameraScrollPatch.cs` | `WorldScrollBlock` flags + Harmony patches that stop the world camera zooming while the picker / Relationships panel is open, incl. the Far Sight mod coexistence postfix. |
| **Track icon loader** | `src/TrackIcon.cs` | Loads `track-icon.png` (config override → bundled) into a `Sprite`. |
| **Icon helper** | `src/DRIcons.cs` | `BuildX` — draws an ✕ from two crossed bars. |

## Dependencies (direction of reference)

```
Plugin ──creates──▶ SkullGuide ──owns──▶ TrackHud, MapPin, MapMarkerHighlight,
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
- **Quest** → `RefreshQuestTarget()` reads the current in-progress objective, resolves its target
  NPC or gold-token location name, and drives the NPC/room steering by proxy.

## Conventions

- Flat `src/`; `pack.ps1` + `Directory.Build.props` are **workspace-synced canonicals** — never edit
  here (regenerated by `../../tools/sync-mod-files.ps1`). Version bumps go in the csproj only.
- Additive-only game integration: every Harmony patch is a Postfix that *adds* UI; the working
  tracking path is never mutated by the button code.
- Commit identity: `dirtyredz <dirtyredz@live.com>`.

## Structural debt

Full triage with priorities in [docs/BACKLOG.md](docs/BACKLOG.md). Headlines (from the 2026-08-22
full review):

- **`SkullGuide.cs` is a ~1600-line God-file** (2× the 800-line cap) spanning ~9 responsibilities:
  target model, spawn lifecycle, steering, NPC picker UI, quest resolution, map/free-pin input,
  flame recolour, marker-highlight orchestration, and diagnostics. The single biggest structural
  item. Cleanest seams to extract: **quest-objective resolution** (P1), **diagnostics/probe dumps**
  (P1), **the target model as its own type** (P1), **flame recolour** (P2), **NPC picker UI** (P2).
- **`HoverScale` and `FindDeep`/`FindChildDeep` are duplicated / misplaced.** `HoverScale` (shared by
  four files) lives inside `RelationshipTrackButton.cs`; a deep-child transform search is copy-pasted
  in five files. Both want a small shared `DRUi` home (P1).
- **`QuestTracker.cs` is misnamed** — it holds the Quest Log *button* UI, while the quest *tracking*
  logic is in `SkullGuide`. Rename to `QuestTrackButton.cs` for one-responsibility-per-file (P2).
- **Two near-identical marker-highlight files** (`MapMarkerHighlight` clones a frame; `MapMarkerTint`
  recolours badges) share the ping/`FindDeep`/base-colour patterns — a possible shared seam, but they
  target genuinely different widget shapes; low priority (P2).

_Living doc — refresh with /project-docs when it drifts._
