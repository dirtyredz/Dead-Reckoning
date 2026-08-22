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

One target at a time, of one of four kinds, held as separate fields in `SkullGuide`:

| Kind | Fields | Set by |
|---|---|---|
| NPC | `tracked : NpcConfigAsset` | picker, Relationships button, map NPC badge |
| Place/house | `trackedRooms : List<RoomAsset>` | map house badge |
| Free pin | `pinRoom` + `pinRoomPos` + `hasPin` (also fills `trackedRooms`) | map double-click on empty spot |
| Quest | `trackedQuestNode` + `trackedQuestData` | Quest Log "Seek Quest" button |

**Invariant:** every setter clears the other three kinds. This is enforced by hand in each of
`SetTracked`, `SetTrackedRooms`, `SetPin`, `OnNpcPicked`, `TrackQuest`, and `ClearTarget` — a known
footgun (see [GOTCHAS.md](GOTCHAS.md); a past bug where a stale free-pin shadowed an NPC came from
exactly this). Candidate for extraction into a `TrackTarget` type ([BACKLOG.md](BACKLOG.md)).

## Steering pipeline (`Steer`, each frame)

1. **Player-speed tracking** — smoothed, so the velocity cap can outrun a sprinting player.
2. **Leash** — if the skull strays past `MaxLeash`, `Mover.Teleport` snaps it back beside you.
3. **Resolve the target world point** (`TrackedWorldPos`):
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

No target ⇒ Perlin-noise idle wander around the player.

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

The Quest Log "Seek Quest" button ([QuestTracker.cs](../src/QuestTracker.cs)) calls
`SkullGuide.TrackQuest`. Each refresh (`RefreshQuestTarget`, throttled) reads the current in-progress
objective and finds its target:
1. The last still-required NPC (`RequiredNpcList` minus `DoneNpcList`), if any.
2. Else the **last gold-coloured (`#FCEBAE`) token** in the objective title — the game's colour for a
   character/location reference ("Deliver to *Orlock*", "Go to the *Town Hall*"). Resolved to an NPC,
   or failing that to a place's rooms (same routing as a map house).

The resolved target is applied by setting `tracked`/`trackedRooms`, so quest tracking rides the exact
same steering path as manual tracking. The HUD echoes the full objectives list.

## Feedback surfaces

- **Seeking HUD** (`TrackHud`) — screen-space overlay, own canvas + Gelica font; "Seeking: X" or the
  quest objectives list, with a ✕ to stop.
- **Map** — free pin gets a red diamond + ping (`MapPin`); a tracked house/NPC badge is recoloured +
  pinged (`MapMarkerTint`); the NPC picker card gets the native selection frame cloned purple
  (`MapMarkerHighlight`). Tracked-NPC highlight follows them live between their own badge (outdoors)
  and their house badge (indoors).

## Game-integration points (Harmony, all additive Postfixes)

| Patch | Target | Adds |
|---|---|---|
| `RelationshipDailyActivitiesWidgetSetupPatch` | `RelationshipDailyActivitiesWidget.Setup` | Track pin in the daily-activity row |
| `QuestScreenShowInfoPatch` | `QuestScreen.ShowQuestInfo` | "Seek Quest" button |
| `InputMouseScrollDeltaPatch` | `Input.MouseScrollDelta` getter | zeroes scroll while our menus are open |
| `GameCameraZoomBlockPatch` | `GameCamera.ProcessPlayerCameraToggle` | skips the game's zoom-toggle while our menus are open |
| `FarSightCoexistPatch` | `FarSightPlugin.IsGameplay` (reflection, only if installed) | makes Far Sight stand down over our menus |

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
