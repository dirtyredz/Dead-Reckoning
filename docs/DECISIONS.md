# DECISIONS — Dead Reckoning

Design/architecture decisions and their rationale, newest first. Rejected alternatives kept so we
don't re-litigate them.

## ADR-010 — Additive-only Harmony integration
All game-integration patches are **Postfixes that only add UI** (`RelationshipDailyActivitiesWidget.
Setup`, `QuestScreen.ShowQuestInfo`). The existing tracking path is never mutated by button code;
buttons call `SkullGuide.Instance` mirror methods (`ToggleTrack`, `ToggleQuest`). Keeps the working
F-key/picker flow intact and makes the mod resilient to game updates. **Rejected:** transpilers /
prefixes into the game's own flow.

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
