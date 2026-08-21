# Dead Reckoning — design notes

A replacement for lockyaw's *On-screen Quest and Character Tracker* (Nexus #48). Where that
mod pins a directional icon to the screen edge, Dead Reckoning spawns a **floating skull** that
the player physically follows: it hovers a few feet away and always drifts toward whatever is
being tracked.

## SESSION HAND-OFF (2026-08-20) — read this first
**Status:** v0.x work-in-progress, builds clean, auto-deploys to
`…/BepInEx/plugins/MoonlightPeaksMods/DeadReckoning`. Latest build installed. NOT yet released to Nexus.

**Working & confirmed in-game:** spawn skull soul blob (F9); NPC tracking (F8 native picker + a Track
button on each Relationships card); house/place tracking + free pin from the map (F6 while map open —
hover a badge or empty spot); cross-room routing (`RoomRouter`); on-screen line steering; wall
collision (Obstacle layer); distance leash; speed scales with player; tracking HUD window; scroll no
longer zooms in menus (Far Sight coexistence patch). Single active target — switching NPC/house/pin
works.

**Just changed, NOT yet user-confirmed (verify first):** map visuals — free pin = red diamond with
white "ping" waves; tracked house/NPC/picker-card get a **purple outline** sized to the badge's union
bounds (`MapMarkerHighlight`). User had rejected: yellow colour, a filled/tiny box, and a scale-pulse.
If the outline is still wrong, probe the map badge hierarchy (a `MapLocationMarkerListWidget`) to find
the real badge element.

**Open TODOs (rough priority):** 1) verify the purple outline + ping look right; 2) in-house precision
(once inside the target room, point at the exact `EntityLocation` instead of idling); 3) the "native
icon" Track button for the Relationships card (user chose icon-clone over the current subtle text
button — a one-shot `DR-CARDPROBE` hierarchy dump is wired under VerboseLogging to guide it); 4) turn
VerboseLogging OFF by default before release; 5) future: recolour the skull + aura (Tastic-Palette
methodology). Fragility to watch: free-pin in-room world pos assumes the player's persisted
`RoomPosition` is current.

**Conventions:** flat `src/`; end with a "Ready for you to test" marker when a build is installed;
never launch the game (user tests). Memory: `dead-reckoning-mod` + `game-update-mod-check-workflow`.

## What the skull can track
- **Players** (other players / co-op, or an NPC "character")
- **Quests** (active quest objective: NPC or location)
- **A pinned map spot** (player drops a pin, skull points there)

## Decisions locked with the user
- **Skull visual:** reuse the *actual* in-game skull prefab/mesh (clone it at runtime), not a
  custom asset. (User picked "Reuse game skull".)
- **Motion:** reuse the game's own `FloatingMovementBehaviour` so the bob/bounce/wind feel is
  identical, rather than a hand-rolled procedural bob. (User picked "Reuse game mover".)
- Skull always parks a few feet from the player, biased toward the tracked target's direction.

## What we know from the assembly (Vampire.Runtime.dll)
- No `Skull` type exists in code — the skull is a **content asset** (decoration or creature
  prefab), animated by shared movement components.
- `FloatingMovementBehaviour` (+ `...Asset`) is a full physics mover: velocity, wind-driven
  bob (`windTime`, banking seed), rotation, and entity bounce (`OnBounce`, `PlayerBounceForce`,
  `BounceForce`, `BounceCheckRadius/Height`, `BounceLayerMask`, `BounceCooldown`). It is a
  `MovementBehaviour<FloatingMovementBehaviourAsset>` bound to an `ICharacterView` + `Mover`.
- Related types to map: `CreatureView`, `ItemDisplayCreatureView`, `EntityCreature*`,
  `AnimatedFloater`, `FloatingBehaviour`, `MovementBehaviour<T>`, `Mover`, `ICharacterView`.

## Engineering findings (from decompile research)
The game already does exactly what we want in **`DraculambCreatureBehaviour`** (the Draculamb
floats during the witching hour) — that's our verbatim template.

1. **Mover rig — reuse a real `CreatureView`, don't fake one.** The mover is `CharacterMover`
   (there is no `Mover` type). A `CreatureView` implements `ICharacterView` and already carries
   a live `CharacterMover` + `CharacterController`. Faking a minimal `ICharacterView` drags in
   `EntityCharacter/Customization/Collision/Animator` on a real `EntityRoot` graph — too deep.
   Spawn a real creature instead:
   `Object.Instantiate(AddressableLibrary<AssetLibrary>.Instance.CreatureView, RoomContainers.Instance.Objects)`
   then `.Load(new CreaturePersistence(<creatureEntityAsset>))`.
2. **Make it float (the Draculamb recipe):**
   `mover.SetMovementBehaviour(<FloatingMovementBehaviourAsset>)`; cast
   `mover.MovementBehaviour as FloatingMovementBehaviour`; ramp `FloatAmount → 1`; set
   `creatureView.Customization.LocalPosition = hover * Vector3.up`; add interaction blocker
   `"FloatingMovementBehaviour"`.
3. **Steering is FORCE-ONLY.** `FloatingMovementBehaviour.UpdateMovement` sets `movement =
   Vector3.zero` on line 1 — `Move()`/`SetDestination`/pathfinding are silently ignored. To aim
   the skull: each frame `floating.AddForce((target - pos).normalized * accel * dt)`. Built-in
   damping + `MaxVelocity` give authentic drift-and-settle; bounce/wind/bank come free.
4. **The FloatingMovementBehaviourAsset isn't in the global `AssetLibrary`** — pull it off a
   Draculamb behaviour (`witchingHourMovementBehaviour`) or load via Addressables. Don't `new`
   it (curves/masks/forces must be real).
5. **Target positions:** player = `MonoBehaviourSingleton<PlayerView>.Instance.Character.transform.position`;
   NPC = `EntityBehaviour<EntityCharacter>.Find(asset)`; location =
   `EntityBehaviour<EntityLocation>.TryFind(asset, out loc)`; quest = `GamePersistence.Instance
   .QuestObjectives` (filter `InProgress`) → `DirectorNode.Get(guid)` → resolve target asset.

## The one real unknown — the skull VISUAL
Assets load via Unity Addressables. **No addressable key contains "skull"** — the skull is
almost certainly a sub-asset (a `BodyViewAsset`/mesh) inside a larger bundle, not findable by
name from the DLL alone. Which creature/body-view the `CreatureView` becomes is decided by the
`IEntityAsset` we feed `CreaturePersistence`. So: real mover = solid; real skull visual = needs
**live in-game discovery** (a probe that dumps candidate creature/body-view asset names), which
fits the "probe the form first" methodology from the Tastic Palette series.

## Future (not now)
- **Recolor the skull and its effects.** A later phase, in the spirit of the Tastic Palette
  recolour series. Implication for now: clone the skull in a way that keeps its renderers /
  materials / VFX reachable so a recolour pass can grab them later — probe the form first,
  same methodology as Purrtastic/Fangtastic.

## Confirmed asset entry points (AddressableLibrary<AssetLibrary>.Instance)
- `.CreatureView` — base creature prefab to `Instantiate`.
- `.LambCreatureItemAsset` — the Draculamb (our known floater); `.SpouseCreatureItemAsset` too.
- Body-views: `.HumanBodyView`, `.BatBodyView`, `.LoveDemonBodyView`, `.AquaBodyView`,
  `.DefaultBodyViewAsset` — swap target for the skull look once identified.
- Mover assets: `.GroundedMovementBehaviour`, `.WaterMovementBehaviour`, `.DeityMovementBehaviour`
  — NO floating asset here; must pull `FloatingMovementBehaviourAsset` off a Draculamb behaviour
  (`witchingHourMovementBehaviour`) or via Addressables.

## First build plan — "probe + floating proof"
1. Spawn a Draculamb (`CreatureView.Load(new CreaturePersistence(LambCreatureItemAsset...))`).
   **GATE ON SAVE-SAFETY** — must not write to the save (verification in progress).
2. Take over its `Mover` with the floating asset, ramp `FloatAmount→1`, hover offset.
3. Steer with `AddForce` toward the player each frame so it hovers a few feet away and follows.
4. Dump candidate body-view / creature asset names to the BepInEx log so the user can identify
   the real skull in-game (probe the form first, per Tastic Palette methodology).

## The skull IS the "soul blob" critter (user-confirmed)
The floating skull is the in-game **soul blob** creature. It's a *critter*, not a creature-entity,
with its own subsystem — a much cleaner path than the Draculamb:
- Spawn: `CritterView.SpawnCritter(pos, soulblobItemAsset, gridSelector, startRandomState:false)`
  (public static). Item assets + a `GridSelector` come from an in-scene `SoulblobSpawner`
  (`.SoulBlobs` list, `.Selector`). There are multiple soul blob variants — **index 0 is the skull** (user-confirmed in-game).
- **Save-safe by design:** `CritterView.IsRegisteredInPersistence == false`; setup builds a runtime
  entity + local customization. Nothing written to the save; no cleanup needed.
- Native float/bounce: `SoulblobMovementBehaviour : BaseFlyingCritterMovementBehaviour` — its
  `UpdateMovement` applies fed-in movement straight to the transform (obeys `Mover.Move`, unlike
  the Draculamb's mover which ignored it). Idle = hovers (no gravity, `forceOnGround` false). The
  bob is a separate `SoulblobAnimation` on the mesh, so it keeps bobbing while we steer.
- **Must neutralize its own behaviour:** `SoulblobCritterBehaviour` wanders and actively FLEES the
  player (`avoidPlayerRadius`, proximity-fright states). We `CritterBehaviour.enabled = false` after
  spawn, then drive `Mover.Move(velocity)` toward a hover point at standoff distance.

## NPC tracking (implemented)
- **Roster:** iterate `GamePersistence.Instance.EntityCharacters`, map `p.Guid` →
  `AddressableLibrary<NpcLibrary>.Instance.Find(guid)` (null ⇒ skip non-NPCs); fallback to
  `NpcLibrary.NpcConfigs`. Name via `NpcLibrary.GetNpcName(cfg, checkIsNameRevealed:false)`.
- **Menu = the game's OWN native screen** `UIScreen<PickNpcScreen>.Instance` (`Setup(list)` /
  `Show(title)` / `Hide()`, `OnNpcClicked` → `widget.Data` is the picked `NpcConfigAsset`). Portraits
  + names, fully native — satisfies "natural in the game". Requires a reference to
  `littlechickengamecompany.chicken-ui.runtime` (UIScreen/ListWidget/.Data live there).
- **Target position:** loaded → `EntityBehaviour<EntityCharacter>.TryFind(entity, out ec)` →
  `ec.transform.position`; off-room → `EntityCharacters.FindOrCreate(entity).NavPosition` (guard
  `Vector3.zero` when nav lib not loaded).
- **Steering with target:** hover point = `player + dirToTarget*standoff + up*hoverHeight` — the
  skull sits between you and the target, pointing the way. No target ⇒ holds its spot near you.

## Open access-point question (user, 2026-08-19)
User is unsure about opening the picker from the pause menu; wants it to feel natural. For now it's
a **hotkey (F8)**. The native `PickNpcScreen` already makes the picker itself feel native; the
remaining choice is only the *trigger* (hotkey / a pause-menu button à la ModNook / a map button).
Defer until user scouts in-game.

## Known issues / notes (2026-08-20)
- **FIXED — riverbed drop:** `CharacterMover.ForceOnGround` defaults **true** (snaps to terrain each
  frame); the game's fly states set it false. We disable the critter behaviour, so we now set
  `Mover.ForceOnGround = false` on spawn ourselves.
- **Feedback = behaviour, not a label (user preference 2026-08-20).** Removed the floating label
  (and `TrackerLabel.cs`/`GameFonts.cs`). Now: a live fix ⇒ skull LEADS toward the target; no target
  OR unresolvable target ⇒ skull IDLES near the player, lazily floating and wandering (Perlin drift
  of the anchor angle + gentle standoff/height oscillation). The lead-vs-wander behaviour is the
  "am I tracking?" tell.
- **Cross-room tracking (houses) — implemented.** `RoomRouter.DoorToward(current, npcRoom)` points
  the skull at the door leading toward the NPC's room; multi-room houses handled by BFS over
  `NavigationLibrary.Rooms[].RoomSwitches`. Entry points: current room `GamePersistence
  .CurrentRoomAsset`; NPC room `EntityCharacters.FindOrCreate(entity).Room`; loaded doors
  `RoomSwitch.All` (`.Door.transform.position`, `.TargetLocation` → `NavigationLibrary.GetRoomAsset`).
  Do NOT steer at `NavPosition` — it's per-room offset nav space, not world. BFS throttled to ~2.5Hz
  and cached. When the player enters the NPC's room, `EntityCharacter.Instances` picks them up and it
  switches back to live tracking automatically.
- **Only wanders now when there's genuinely no route** (no target, nav lib not loaded, or no door
  path). Non-NPC pets (cat) still won't resolve if their live entity guid mismatches — future work
  (match `CreatureView`/critter entities).

## Fixes 2026-08-20 (round 2)
- **Straight-line aiming:** steering no longer flattens to XZ + fixed hover height (that read as "up
  and to the side" and collapsed for near-vertical targets). Tracking now places the skull on the
  true 3D line from `player+hoverHeight` to `target+0.8` at `min(standoff, dist*0.85)` — points right
  at them incl. up/down. Idle wander unchanged.
- **Survives scene changes:** the soul blob is a room object destroyed on room unload (`active` goes
  Unity-null). Added `wantActive` intent flag + auto-respawn (throttled 0.75s) so the skull comes
  back in the new scene; `ResolveSources` re-finds the room's `GridSelector` (soul blob `ItemAsset`
  list persists). F9 now toggles `wantActive` rather than spawning directly.

## Fixes 2026-08-20 (round 3)
- **On-screen line:** the visible skull mesh floats above the point we steer, so it read high on
  screen. Now measure that lift (`SkullVisualLift` from renderer bounds) and steer that much lower;
  tracking line anchored at `TrackEyeHeight` (0.9→**0.5**) so the mesh lands on the me→target screen
  line (perspective: a point on the 3D segment projects onto the on-screen segment).
- **Wall collision:** flying mover translates directly (no collider), so it clipped houses. Added
  `AvoidWalls` — spherecast the intended step, clamp + slide along the surface. Config `Collide`
  (default on). Needs a `UnityEngine.PhysicsModule` reference.
  - **Layer:** `"Default"` hit the bridge, not walls. `Obstacle|NonFlyingObstacle` snagged on small
    props (got stuck). Final: **`AssetLibrary.ObstacleLayerMask` only** — grid objects are tagged
    `ObstacleLayer` when they block flying (walls/buildings) vs `NonFlyingObstacleLayer` for props a
    flyer passes (decompile 222023/223018). So Obstacle-only = walls/buildings, no small-prop snag.
- **Distance leash:** `MaxLeash` (default **7**). If the skull strays past it, `Mover.Teleport` snaps
  it back beside the player + clears the route cache. Works well per user. NOTE: changing the code
  default does NOT rewrite an existing `.cfg` value — user must set 7 in the mod menu if they already
  have 8 saved.
- **Wall collision — layer + normal, take 2.** `RoomBounds`(Default) was the whole-area bounding box,
  not house walls. Directional `DR-WALLPROBE` (8-way SphereCast, triggers on, logs dist/layer/trigger/
  nY) at a wall showed house walls are SOLID colliders on the **Ground layer [9]** named `Collider`,
  with `nY` negative (−0.3…−0.7). Fix: `envMask = ObstacleLayerMask | GroundLayerMask | (1<<0)` and
  block only when NOT up-facing: `if (hit.normal.y >= 0.5f) return vel;` — floors/decks/gentle slopes
  (nY≈+1) pass, walls (nY≤0) block. Layer read at runtime so a game update can't stale it.
- **Floor-first bug (clipping got WORSE):** adding Ground to the mask made a single `SphereCast` hit
  the floor first (up-facing → passed), bailing before the wall. Fixed with `SphereCastAll`: skip
  hits with `distance<=0` (overlapping) and `normal.y>=0.5` (floors/decks/slopes), keep the nearest
  remaining hit as the wall, clamp+slide on it. Now floors are ignored and the wall behind blocks.
- **Game update 2026-08-20:** mod still loads clean (no errors); layers unchanged (Ground 9, Default
  0, Player 21). Masks are read live from AssetLibrary so they self-adjust.
- **Height off ground:** new config `GroundClearance` (default **0.7**, was const 0.3) — raises the
  surface-follow floor so the skull flies higher over stairs/bridges and clears low invisible
  colliders. Raise further if it still snags.
- **Speed keeps up:** velocity cap is now `max(14, playerSpeed*1.8 + 3)` — tracks measured player
  speed (smoothed) so the skull outruns sprinting (cat form) instead of lagging/triggering the leash.
- **Bridge arch / surface-follow:** skull height was tied to player Y, so on the arch the deck ahead
  is higher → it clipped through. `ClampAboveGround` raycasts `GroundLayerMask` down from above the
  hover point and raises the steer-point to `groundY + clearance - visualLift` (raise-only, so over
  water/void it's a no-op and won't drop into the river). NOTE: if the bridge deck isn't on
  `GroundLayer`, this won't catch it — the layer probe will confirm.

## UI / interaction work (2026-08-20)
- **DONE — picker scroll no longer zooms:** while the native `PickNpcScreen` is open we
  `PlayerView.Instance.Input.InputBlocker.Add("DeadReckoningNpcPicker")` and remove it when the
  screen closes (poll `pickerScreen.gameObject.activeInHierarchy`; also on pick/OnDestroy). UI
  list-scroll is unaffected, so the wheel scrolls the list without zooming the camera.
- **DONE — Track button in the Relationships panel.** Harmony now in the mod (`0Harmony` ref,
  `new Harmony(guid).PatchAll()` in Awake). `RelationshipListWidgetOnSetupPatch` (Postfix on
  `RelationshipListWidget.OnSetup`) → `RelationshipTrackButton.Attach`: reads the card's NPC via
  `((ListWidget<RelationshipWidgetData>)w).Data.NpcConfigAsset`, adds a self-built Track button
  (top-right of each card, card's own font), click → `SkullGuide.Instance.ToggleTrack(cfg)` +
  `RefreshAll()`. Label shows Track/Tracking. Additive only — existing tracking path untouched
  (`SetTracked/IsTracked/ToggleTrack` mirror the picker without changing `OnNpcPicked`).
  Insight from lockyaw's QuestTracker (`RelationshipListWidgetOnSetupPatch` → status suffix), NOT copied.
  May need position/style tuning after seeing it in-game.
- **TODO (later) — aura colour controls** for the skull (recolour its aura/VFX), Tastic-Palette style.
- **Options live in ModNook / Mod Menu** (config already tagged `ModMenu.Section`/`ModMenu.Label`).

## Picker + button feedback (2026-08-20)
- **FIXED — picker couldn't close:** opened outside its Director flow, `PickNpcScreen` had no cancel.
  Now `PickNpcKey` toggles it (open→close) and `InputUtility.GetCancelInputDown()` (Esc/B) closes it;
  `ClosePicker()` hides + unblocks + clears `pickerScreen`.
- **Track button softened** (smaller 58×20, font 12, bg alpha 0.55) as interim — user said it "breaks
  immersion". Needs a real style decision (icon vs subtle text vs card-highlight) — asked.
- **Scroll-zoom — the REAL culprit was another mod: Far Sight** (`FarSightPlugin`, camera zoom mod).
  Diagnostics proved our patches fire and `WorldScrollBlock.Blocked==true` in menus, yet it still
  zoomed → Far Sight does its OWN scroll zoom (Rewired axis 24), standing down only when its
  `IsGameplay()` is false (player in `PlayerJournalStateMachine`/decorate). Our hotkey picker isn't a
  journal state, so Far Sight kept zooming. FIX: reflection-patch (only if Far Sight loaded)
  `FarSightPlugin.IsGameplay` postfix → force `false` while `WorldScrollBlock.Blocked`, so Far Sight
  stands down over our picker/relationship panel. (`Plugin.TryPatchFarSight` + `FarSightCoexistPatch`.)
  Kept our own `GameCamera.ProcessPlayerCameraToggle` prefix + `MouseScrollDelta` postfix for when
  Far Sight isn't installed. NOTE: the relationship panel is a journal screen, so Far Sight already
  stood down there; the visible issue was the hotkey picker. Minor: Far Sight may snap to default
  zoom when the picker opens (it StandDowns).
  - **LOAD-ORDER GOTCHA:** Dead Reckoning loads BEFORE Far Sight, so `AccessTools.TypeByName(
    "FarSightPlugin")` was null in our Awake → patch silently no-op'd. Fixed: attach lazily from
    `SkullGuide`'s first `Update` (`DeadReckoningPlugin.TryPatchFarSight`, one-shot via `farSightChecked`),
    by which point all plugins are loaded. Confirm via log "Far Sight detected — it will stand down…".
  - **CONFIRMED FIXED in-game (2026-08-20).** Scroll no longer zooms in the picker.
- **TODO next — native icon Track button** (user chose it). Added a one-shot `DumpHierarchy`
  (`DR-CARDPROBE`, VerboseLogging) to read the card's real UI tree (gift/speech icon buttons: type,
  size, sprite) so the Track control can clone/match them instead of the current subtle text button.
- **TODO — native icon Track button (user chose this):** clone a card's existing icon button for
  native style + icon instead of the current subtle text button. Next.
- **TODO — highlight tracked NPC in the picker** (would need a `PickNpcListWidget` patch). Deferred.

## Map / place tracking (2026-08-20)
- **DONE — track houses/places from the map.** The map is hover-based (no clickable markers):
  `MapScreen.MapCursor.HoveredInteractable`. New key **F6** (`MapTrackKey`): while the map is open,
  track the hovered marker — `MapLocationMarkerListWidget` → its `MapLocationMarker.RoomAssets` +
  `GetLocationName()` (a house/place); `MapNpcMarkerListWidget` → its `NpcConfigAsset` (unifies with
  NPC tracking). Target model generalized: `trackedRooms : List<RoomAsset>` alongside `tracked` (NPC);
  `HasTarget()`, `SetTrackedRooms`. Routing reuses `RoomRouter.DoorToward` (`RouteToNearestRoom` tries
  each room). In the target room ⇒ idle ("arrived"). The map is itself the "list of named places"
  (user's insight: hover+key = the free pin too).
- **DONE — free pin (precise).** First version only tracked the *room* → useless when you pin inside
  your current room (it idled "arrived"). Now `TryFreePin` resolves the exact spot: smallest
  `MapRoomMarker` rect under cursor (`ScreenPointToLocalPointInRectangle`, camera-aware) → inverse-lerp
  the local point into `RoomData.NavigationGraphRect` → `RoomData.NavToRoomPosition` = room-local pos
  (ported from lockyaw's `TryGetRoomPositionAtMapUiPosition`). `SetPin(room,roomPos,name)` stores it.
  Steering: in the pin's room → `PinWorld() = PlayerPos + (pinRoomPos − playerRoomPos)` (player room
  pos from `EntityCharacters.FindOrCreate(AssetLibrary.PlayerEntity).RoomPosition`); out of room →
  route via `RoomRouter`. GOTCHA: `Input` = the game's global class → `Input.MousePosition`. Fragile:
  the relative in-room conversion assumes the player's persisted `RoomPosition` is current.
- **TODO — in-house precision:** once inside the target room, point at the exact `EntityLocation`
  rather than idling. Deferred.
- **DONE — free-pin map marker.** `MapPin` draws a red diamond on the map at the pinned spot each
  frame via `MapWidget.GetUIPositionFromRoomPosition(pinRoomPos, pinRoom)` (world-UI point), parented
  under the room-markers container so it pans/zooms with the map. Shows only while the map is open and
  a pin is set; hidden if the pinned room isn't on the active map page (helper returns Vector3.zero).
- **DONE — tracked-target borders.** `MapMarkerHighlight.Set(widget, on)` adds a pulsing gold 4-edge
  border (badge icon stays visible). `UpdateTrackedHighlights` each frame: map house badges
  (`MapLocationMarkerListWidget` whose `RoomAssets` overlap `trackedRooms`, not a pin), map NPC badges
  (`MapNpcMarkerListWidget.Data == tracked`), and the NPC picker cards (`PickNpcListWidget.Data ==
  tracked`) — via `FindObjectsByType` only while the map/picker is open. Free pin keeps the pulsing red
  diamond; houses/NPCs get the border instead (diamond didn't align with the floating badge).
- **Highlight revisions (user feedback):** border colour gold→**purple** (game selection style; yellow
  broke immersion). Border now sizes to the badge's largest Image (`BestRect`) not the widget root —
  the map badge root was tiny → "tiny yellow box"; picker cards already worked.
- **Free-pin = ping, not pulse.** `MapPin` now = solid red diamond (dark rim) + 3 white "ping"
  diamonds expanding/fading on staggered phases (radiating waves, like a game ping).
- **DONE — tracking HUD + map feedback.** `TrackHud` = small fixed top-left overlay window showing
  "Tracking: <name>" / "Tracking: nobody yet" while the skull is spawned; updates instantly when you
  F6 a house or pick an NPC (that IS the map-track confirmation the user asked for). Own Gelica-font
  lookup (no shared GameFonts). Config `ShowHud`. (NOTE: distinct from the earlier floating
  over-skull label the user rejected — this is a fixed status window they requested.)

## Bug: free pin killed NPC tracking (2026-08-20, fixed)
- Symptom: NPC tracking worked until you used a free pin, then stopped. Cause: `TrackedWorldPos`
  checks the place target (`trackedRooms`) FIRST, and `OnNpcPicked` (the F8 picker) set `tracked`
  but never cleared `trackedRooms`, so a prior free-pin/house target shadowed the NPC. Fix: clear
  `trackedRooms = null` in `OnNpcPicked` (the relationship-button `SetTracked` and map-NPC paths
  already did). Invariant: every target setter clears the other target types (single active target).
- Diagnostics left in (VerboseLogging-gated): `DR-MAPTRACK`, `DR-FREEPIN`, `DR-NPCPOS`.

## Status
- Build DONE + deployed. `SkullGuide`: spawn skull soul blob (index 0), disable flee behaviour,
  `ForceOnGround=false`, steer with `Mover.Move`; F8 native NPC picker → lead toward chosen NPC in
  the same room, or toward the door into their room/house when they're elsewhere (`RoomRouter`); F7
  clears. Idle float-and-wander only when no route. Save-safe.
- Controls: **F9** spawn/despawn, **F8** pick NPC (native screen), **F7** clear target.
  Config: `SoulblobIndex` (0=skull), hover distance/height, follow strength.
- NEXT: settle the access point; then other tracking modes (quest objective, pinned map spot) and
  the item-display half. Later: recolour pass (Tastic-Palette style) on the soul blob material/VFX.
- Research: `MonoBehaviourSingleton<T>`/`Signal<T>` in `Chicken.Utilities`; `UIScreen<T>` in
  `Chicken.UI` (chicken-ui.runtime dll). Static holder is `DeadReckoningPlugin`. Decompile cached.
