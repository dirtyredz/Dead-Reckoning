# ARCHITECTURE — Dead Reckoning

How the tracking system works, end to end. For *where the code lives* see the root
[STRUCTURE.md](../STRUCTURE.md); for *why* choices were made see [DECISIONS.md](DECISIONS.md).

## The idea

A replacement for lockyaw's *On-screen Quest and Character Tracker* (Nexus #48). Instead of pinning
a directional icon to the screen edge, Dead Reckoning spawns a **floating skull** the player
physically follows: it hovers a few feet away and drifts toward whatever is being tracked. When
nothing is tracked it idles and lazily wanders near you — the lead-vs-wander behaviour *is* the
"am I tracking?" feedback.

## Runtime shape

`DeadReckoningPlugin.Awake` (BepInEx entry) binds all config, runs `Harmony.PatchAll`, and adds a
single `SkullGuide` MonoBehaviour to its plugin GameObject. `SkullGuide.Update` is the heartbeat:
input handling, auto-(re)spawn, quest refresh, `Steer()`, and the HUD/map/highlight updates all run
from there. There is no other update loop; the Harmony patches only inject UI that calls back into
`SkullGuide.Instance`.

## The target model (single active target)

One *selection* at a time, held as separate fields in `SkullGuide`. The model is **two-layer**:
NPC / place / free pin / gather-node are *destination* fields the skull steers by; quest and job are
*selections* that stay set while a throttled refresh re-resolves which destination to write:

| Kind | Layer | Fields | Set by |
|---|---|---|---|
| NPC | destination | `tracked : NpcConfigAsset` | picker, Relationships button, map NPC badge — or a quest/job refresh |
| Place/house | destination | `trackedRooms : List<RoomAsset>` | map house badge — or a quest refresh |
| Free pin | destination | `pinRoom` + `pinRoomPos` + `hasPin` (also fills `trackedRooms`) | map double-click on empty spot |
| Gather/mine node | destination | `trackedNode : Transform` | quest resolution, via `QuestNodeLocator` (the live in-scene vein/bush) |
| Quest | selection | `trackedQuestNode` + `trackedQuestData` | Quest Log "Seek Quest" button |
| Job | selection | `trackedJobData : JobPersistence` | Quest Log "Seek Job" button |

**Invariant:** picking a new selection clears every *other* selection **and** destination field —
enforced by hand in the setters (`SetTracked`, `SetTrackedRooms`, `SetPin`, the NPC-pick handler,
`TrackQuest`, `TrackJob`, `ClearTarget`). The **refresh apply-branches are deliberately different**:
the four inline branches of `RefreshQuestTarget` and the one in `RefreshJobTarget` re-write only the
destination fields and must **not** clear their own selection — that's what keeps the mode alive
between refreshes.
A known footgun (see [GOTCHAS.md](GOTCHAS.md); a past bug where a stale free-pin shadowed an NPC came
from exactly this). Top candidate for extraction into a `TrackTarget` type ([BACKLOG.md](BACKLOG.md)).

## Steering pipeline (`Steer`, each frame)

1. **Player-speed tracking** — smoothed, so the velocity cap can outrun a sprinting player.
2. **Leash** — if the skull strays past `MaxLeash`, `Mover.Teleport` snaps it back beside you.
3. **Resolve the target world point** (`TrackedWorldPos`):
   - Gather/mine node → the live node `Transform`'s position (it's in the loaded room; A* leads to it).
   - NPC in the current room → its live transform.
   - NPC / place in another room → the **door** heading toward it (`RoomRouter`, BFS, throttled &
     cached). We steer at the door, *not* the NPC's `NavPosition` (that's per-room offset space).
   - Free pin in its room → the exact in-room world spot; a house → idle ("arrived").
4. **Lead point** — if `FollowPath`, `PathGuide` returns a point a standoff *along the A\* walkable
   path* (curves around furniture/walls); else a straight me→target line, capped short of the target.
5. **Visual-lift compensation** — the skull mesh floats above the steered mover point; the steer
   point is dropped by the measured lift so the mesh lands on the sightline.
6. **Ground clamp** — raise-only raycast so it rides over bridges/stairs but never drops into water.
7. **Wall-avoid** — spherecast the intended step against the obstacle layer; clamp + slide.
8. **Move** — `Mover.Move(velocity)`, capped. First frame after spawn teleports to the spot.

No target ⇒ **idle follow**: a spring-damper (`idleVel`, tuned by `IdleFollowSpring`/`IdleFollowDamping`)
homes toward the nearest point of a horizontal sphere around the player (radius = hover distance), so it
lags when you run and springs back (Perlin noise only breathes the radius). Leading-ahead (seeking) vs.
floating-around-you (idle) is the "am I seeking?" tell.

## Cross-room routing (`RoomRouter`)

Given the current room and a target room, returns the world position of the door to head toward.
Direct exit first; otherwise BFS over `NavigationLibrary`'s room-adjacency graph
(`RoomData.RoomSwitches`) to find the first hop, then the loaded `RoomSwitch` whose far side is that
room. Doors are resolved live from the current scene (`RoomSwitch.All`).

## In-room pathfinding (`PathGuide`)

Wraps the game's A* Pathfinding Project graph. Recomputes the path *shape* on a throttle, but derives
the lead point every frame by projecting the player's current position onto the path and walking
forward — smooth as you move. Routes like a land NPC (water-excluded tag mask) so it uses bridges.

## Quest tracking

The Quest Log "Seek Quest" button ([QuestTrackButton.cs](../src/QuestTrackButton.cs)) calls
`SkullGuide.TrackQuest`. First, if `QuestPersistence.IsCompleted` is set the quest is done, so
`RefreshQuestTarget` `ClearTarget()`s (dismisses the skull) rather than idling forever. Otherwise each
refresh (throttled) reads the current in-progress objective and resolves its target, in priority order:

1. The last still-required NPC (`RequiredNpcList` minus `DoneNpcList`), if any.
2. Else an NPC named by the **gold-coloured (`#FCEBAE`) token** in the visible title — the game's
   curated character/location reference ("Deliver to *Orlock*"). Authoritative, so it's tried before 3.
3. Else an NPC named only in the objective's **internal dev-name** (`FindNpcMentioned`) — the recipient
   the visible title can hide (dev-name "Bring a copper bar to *Yabbis'* pond", shown as "the little
   pond"). Whole-word run match against `NpcLibrary`, latest mention wins.
4. Else a **gather/mine node** ([QuestNodeLocator](../src/QuestNodeLocator.cs)) — once you're in the
   region, the objective's item (from its `SpeechInjectionCollection` / item-requirement) is matched to
   the nearest loaded `IHarvestable` / mineable `DestructibleView` in the scene (`trackedNode`).
5. Else the gold token as a **place** → its rooms (same routing as a map house).

The resolved target is applied by setting `tracked`/`trackedRooms`/`trackedNode`, so quest tracking
rides the exact same steering path as manual tracking. Delivery/turn-in objectives with no named
recipient stay at region level (nothing links them to a hand-in point). The HUD echoes the objectives.

## Job tracking

Jobs (the job-board tasks listed under the quest tab) render through `QuestScreen.ShowJobInfo` — a
separate path from `ShowQuestInfo` — so they get their own patch and a "Seek Job" binding of the same
button (v1.2.1; before that the button was stale on job entries). `SkullGuide.TrackJob` holds the
`JobPersistence` (identity by `Guid`), and `RefreshJobTarget` (throttled, like the quest refresh)
resolves the **hand-in NPC** via `JobPersistence.CompletionNpcConfigAsset` — the game's own
"sub-persistence override ?? subject NPC", so job types that switch hand-in NPC mid-way stay correct —
and applies it by setting `tracked`, riding the same NPC steering. A completed or past-deadline job
`ClearTarget()`s (dismisses the skull); completed/expired entries get no button. The HUD shows
"Seeking: Job for <NPC>", matching the game's own job title. There is no per-step objective data on a
job (requirements live in the sub-persistence), so job tracking is hand-in-NPC-level only.

## Feedback surfaces

- **Seeking HUD** (`TrackHud`) — screen-space overlay, own canvas + Gelica font; "Seeking: X" or the
  quest objectives list, with a ✕ to stop.
- **Map** — free pin gets a red diamond + ping (`MapPin`); a tracked house/NPC badge is recoloured +
  pinged (`MapMarkerTint`); the NPC picker card gets the native selection frame cloned purple
  (`PickerCardHighlight`). Tracked-NPC highlight follows them live between their own badge (outdoors)
  and their house badge (indoors).

## Game-integration points (Harmony)

Two categories. The **UI-injection** patches are additive Postfixes that only add UI; the
**camera-coexistence** patches deliberately suppress input (a Prefix and a value-replacing Postfix)
while our menus are open — not additive, on purpose.

| Patch | Kind | Target | Effect |
|---|---|---|---|
| `RelationshipDailyActivitiesWidgetSetupPatch` | Postfix (additive) | `RelationshipDailyActivitiesWidget.Setup` | adds the Track pin to the daily-activity row |
| `QuestScreenShowInfoPatch` | Postfix (additive) | `QuestScreen.ShowQuestInfo` | adds the "Seek Quest" button |
| `QuestScreenShowJobInfoPatch` | Postfix (additive) | `QuestScreen.ShowJobInfo` | rebinds that button as "Seek Job" on job entries (hides it for completed/expired jobs) |
| `InputMouseScrollDeltaPatch` | Postfix (value-replacing) | `Input.MouseScrollDelta` getter | zeroes scroll while our menus are open |
| `GameCameraZoomBlockPatch` | **Prefix (skips)** | `GameCamera.ProcessPlayerCameraToggle` | skips the game's zoom-toggle while our menus are open |
| `FarSightCoexistPatch` | Postfix (reflection, only if installed) | `FarSightPlugin.IsGameplay` | makes Far Sight stand down over our menus |

The Far Sight patch is attached lazily from `SkullGuide`'s first `Update` because Far Sight loads
*after* us (see [GOTCHAS.md](GOTCHAS.md)).

## Save-safety

The skull is the in-game **soul-blob critter** (`CritterView.SpawnCritter`), which is
`IsRegisteredInPersistence == false` — it builds a runtime entity and writes nothing to the save. We
disable its own (fleeing) behaviour, set `ForceOnGround = false`, and drive `Mover.Move` ourselves.
Nothing to clean up; it's destroyed on room unload and auto-respawned while tracking.

## Build & release

Standard workspace chain (see the root [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md)):
`Directory.Build.props` deploys the DLL + `track-icon.png` to `BepInEx/plugins/MoonlightPeaksMods/
DeadReckoning` on build; `pack.ps1` produces `dist/DeadReckoning-<version>.zip` in Nexus layout;
version is single-sourced from the csproj `<Version>`. Published as
[Nexus mod 144](https://www.nexusmods.com/moonlightpeaks/mods/144).
