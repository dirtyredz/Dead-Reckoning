# DECISIONS — Dead Reckoning

Design/architecture decisions and their rationale, newest first. Rejected alternatives kept so we
don't re-litigate them.

## ADR-011 — The default Track icon ships inside the DLL, not beside it
The default Track-button icon is an `<EmbeddedResource>` in `DeadReckoning.csproj` with a pinned
`<LogicalName>` (`DeadReckoning.track-icon.png`), loaded from the assembly at runtime. **Rationale:**
`pack.ps1` stages only `DeadReckoning.dll` into the release zip, so any loose asset is a dev-machine
artifact that never reaches a Nexus installer. **This already shipped broken:** v1.2.1 copied a loose
`track-icon.png` next to the DLL, so every real install fell back to a text label on the Track buttons
while the dev install looked correct. The `DeployPlugin` target's loose copy was **removed
deliberately** as part of this — the dev install must be byte-for-byte what ships, or the divergence
hides exactly this class of bug. A user PNG at `BepInEx/config/DeadReckoning/track-icon.png` still
overrides the embedded default, and the DLL-adjacent probe that used to sit between the two was
removed with it — a stale loose PNG there would mask a broken embedded resource, which is the
v1.2.1 failure all over again. **Rejected:** keeping the deploy-copy (the thing that broke); and
teaching `pack.ps1` to bundle the PNG — it is a workspace-synced generated file that must not be
hand-edited in this repo, and a second loose file would still go missing for anyone who installed
only the DLL.

## ADR-010 — Additive UI integration; input suppression only where required
**UI integration** is additive Postfixes that only add UI (`RelationshipDailyActivitiesWidget.Setup`,
`QuestScreen.ShowQuestInfo`); the existing tracking path is never mutated, and buttons call
`SkullGuide.Instance` mirror methods (`ToggleTrack`, `ToggleQuest`). This keeps the F-key/picker flow
intact and resilient to game updates. **Camera coexistence is the deliberate exception:** to stop the
world camera zooming over our menus we use a Prefix that skips `GameCamera.ProcessPlayerCameraToggle`
and a value-replacing Postfix on the scroll getter — suppressing input is the whole point there, so
those are *not* additive. **Rejected:** transpilers, and prefixes into the game's own *tracking* flow.

## ADR-009 — Feedback is behaviour + HUD, not a floating label
The skull *leads* when it has a fix and *idles/wanders* when it doesn't — that motion is the primary
"am I tracking?" tell. A fixed top-corner **Seeking HUD** confirms the name. **Rejected:** a floating
label pinned over the skull (`TrackerLabel.cs`/`GameFonts.cs`, since deleted) — the user found it
immersion-breaking.

## ADR-008 — Single active target, cleared by every setter
Exactly one target (NPC / place / free-pin / quest) is active at a time. Each setter clears the other
kinds. **Rationale:** simple mental model, one skull, one destination. **Cost:** the invariant is
hand-maintained across six methods and has bitten us (a stale free-pin once shadowed an NPC). A
`TrackTarget` value type is backlogged to make the invariant structural.

## ADR-007 — Quest targets resolved from the gold objective token
Most objectives carry no NPC requirement — the real target is the gold-coloured (`#FCEBAE`) name in
the objective title. We parse the **last** gold token and resolve it to an NPC, else to a place's
rooms. **Rejected:** relying only on `RequiredNpcList` (misses vendor/recipient/location targets).

## ADR-006 — Steer at the door, not the NPC's NavPosition
For an off-room target we steer toward the **world position of the door** leading toward their room
(`RoomRouter` BFS). **Rationale:** `NavPosition` lives in per-room offset nav space, not current
world coordinates — aiming at it directly points nowhere.

## ADR-005 — Follow the A* walkable route, not a straight line
`PathGuide` leads a standoff along the game's A* path so the skull curves around furniture/walls.
Falls back to a straight me→target line when no path is available. Config `FollowPath` toggles it.

## ADR-004 — Wall collision: obstacle layer only
Wall-avoidance spherecasts against `AssetLibrary.ObstacleLayerMask` **only** (read live at runtime so
a game update can't stale it). **Rejected after in-game probing:** Default layer (hit the bridge),
`Obstacle|NonFlyingObstacle` (snagged on props), and adding Ground (floor-first `SphereCast` bug).
House walls sit on the Ground layer and remain a **known, accepted clip** — the alternatives felt
worse. Ground clamp is a separate raise-only raycast so it rides bridges without dropping into rivers.

## ADR-003 — The skull is the soul-blob critter (not the Draculamb)
The floating skull is the in-game **soul-blob** critter (`CritterView.SpawnCritter`, variant index
0). **Rationale:** critters are save-safe by design (`IsRegisteredInPersistence == false`) and their
`SoulblobMovementBehaviour` obeys `Mover.Move`. **Rejected:** the Draculamb creature path — its
`FloatingMovementBehaviour` zeroes movement and is force-only, drags in a deep entity graph, and
isn't save-safe without care. (History kept in [../DESIGN.md](../DESIGN.md).)

## ADR-002 — Reuse the game's own critter + mover, don't fake one
Spawn a real `CritterView` and drive its real `Mover`; disable the critter's own (fleeing) behaviour
and set `ForceOnGround = false`. **Rejected:** a hand-rolled procedural bob / a faked `ICharacterView`
(drags in the whole entity/customization/collision graph).

## ADR-001 — Version single-sourced from the csproj
`PluginVersion = ModBuildInfo.Version`, generated from `<Version>` in `DeadReckoning.csproj` by
`GenerateModBuildInfo`. Never hardcode a version in `Plugin.cs`. Shared workspace convention.

_Living doc — add an ADR when a decision is made or reversed._
