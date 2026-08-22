# BACKLOG — Dead Reckoning

Prioritized task trough. P0 = do now / blocking · P1 = should do soon · P2 = nice to have / deferred.
Structural items come from the 2026-08-22 full review (componentization + abstraction lenses + Codex);
see [../STRUCTURE.md](../STRUCTURE.md) "Structural debt".

## ✅ Done in the 2026-08-22 review pass (mechanical, compile-verified)
- De-duplicated `FindDeep`/`FindChildDeep` (5 copies across 4 files → one `DRUi.FindDeep`).
- Extracted `HoverScale` out of `RelationshipTrackButton.cs` into `HoverScale.cs`.
- Renamed `QuestTracker.cs` → `QuestTrackButton.cs` (matches its class; the file is button UI, not
  tracking logic).
- Moved `RouteToNearestRoom` → `RoomRouter.DoorTowardFirstReachable` (accurate name; belongs with the
  routing logic).
- Renamed `MapMarkerHighlight.cs` → `PickerCardHighlight.cs` (it highlights NPC picker cards).

## P0
_(none)_

## P1 — decompose the `SkullGuide` God-file

~1580 lines, ~9 responsibilities. Do these as **separate focused passes**, in this order (each
shrinks the next one's diff). All are backlog, not drive-by — they change behaviour-adjacent state.

1. **Target model → `TrackingSelection` + `ResolvedDestination`.** Replace the 9 parallel fields
   (`tracked`, `trackedRooms`, `pinRoom`/`pinRoomPos`/`hasPin`, `trackedName`, `trackedQuestNode`/
   `trackedQuestData`, + route cache) whose "clear the other kinds" invariant is hand-repeated in **7**
   sites (`SetTracked`, `SetTrackedRooms`, `SetPin`, `TrackQuest`, `OnNpcPicked`, `ClearTarget`, and
   twice inside `RefreshQuestTarget`). Keep the *selection* (what the user picked, incl. display name)
   separate from the *resolved destination* (recomputed for quests) — a single broad `TrackTarget`
   with all-nullable payloads would just relocate the invariant. Quest state stays orthogonal (it
   *produces* an NPC/place destination). Fold the route cache onto the target so invalidation is
   automatic. **This bug shape already cost one regression (stale free-pin shadowed an NPC).**
2. **Quest resolution → `QuestObjectiveResolver` + `QuestHudFormatter`.** Move `QuestObjectives`,
   `RefreshQuestTarget`, `LastGoldToken`, `StripTags`, `FindNpcByName`, `ResolveQuestLocation`,
   `FindLocationRooms`, `MarkerNames`, `NormalizeName`, `SameRoomSet`, `BuildQuestHud`. Have the
   resolver return an objective snapshot + a `ResolvedDestination`; keep HUD formatting separate. (It
   reads persistence/scene/time and parses rich text — treat it as a real subsystem, not pure helpers.)
3. **NPC picker UI → `NpcPickerController`** and **map input → `MapTrackingInput`.** Move the hotkey/
   picker construction/stop-button/input-blocking block and the double-click/hit-test/free-pin block
   (~350–400 lines). Both should *issue tracking commands*, not mutate target fields directly.
4. **Feedback orchestration → `TrackingFeedbackPresenter`.** Move `UpdateTrackedHighlights`,
   `TrackedNpcRoom`, `IsTrackingRooms`, `UpdateMapPin`, `MapMarkerOverlay`, `UpdateHud` — the per-frame
   scanning of markers/picker cards and HUD/pin presentation. Feed it an immutable tracking snapshot.
5. **Target resolution → `TargetResolver`.** The room-coordinate / live-NPC / door-routing / route-
   cache logic in `TrackedWorldPos` (+ `PinWorld`; routing already delegates to
   `RoomRouter.DoorTowardFirstReachable`) so `Steer` consumes only a `Point`/`Arrived`/`Unavailable`
   result.

After those cuts `SkullGuide` becomes what its name promises: skull lifecycle + update orchestration
+ the steering pipeline.

## P2 — smaller structural items

- **`DRMarkerTint` parallel lists → one binding type** (`List<TintBinding>` holding `Image` + base
  `Color` + optional `UIColorable`), removing another positional invariant. Cheap.
- **`PingWaves` shared animator** — the ripple math in `MapPin` and `DRMarkerTint` is identical.
  Extract a narrow helper *only if* a third consumer appears; don't merge the two highlight systems
  (`PickerCardHighlight`/`MapMarkerTint` solve genuinely different problems).
- **Narrow facade + `Changed` event** to replace the `SkullGuide.Instance` service-locator the
  injected buttons depend on (kills `RefreshAll`'s scene-scan). Do after the target model lands.
- **Reconcile the `ColorLibrary` base-colour read** duplicated in `PickerCardHighlight`/`MapMarkerTint`
  — extract only after checking the zero-alpha guard difference between them is intentional.
- **Compatibility/input ownership** — move lazy Far Sight discovery + relationship-panel polling out
  of `SkullGuide` into a plugin-owned compatibility component; camera coexistence isn't skull steering.
- **Diagnostics probes** — when extracting subsystems, keep each probe beside its subsystem; only a
  generic transform/UI-tree walker is worth sharing (avoid a `DrProbes` grab-bag).

## P1/P2 — gameplay (from DESIGN.md, deferred)

- **[P1] In-house precision** — once inside the target room, point at the exact `EntityLocation`
  instead of idling ("arrived").
- **[P2] Non-NPC pet tracking** — pets (cat) don't resolve when their live entity guid mismatches;
  match `CreatureView`/critter entities.
- **[P2] Highlight the tracked NPC inside the picker list** — would need a `PickNpcListWidget` patch.

## Known issues / accepted limitations

- House interior walls (Ground layer) are clipped through — accepted; see [GOTCHAS.md](GOTCHAS.md) /
  ADR-004. Revisit only if a clean fix appears.
- Free-pin in-room position depends on the player's persisted `RoomPosition` being current (fragile).
- The `find-existing-or-build` button lifecycle differs across the three button files (deep vs shallow
  `Find`, per-click vs scene-wide vs attach-only refresh). Investigate whether the differences are
  load-bearing before unifying — don't force a shared helper mechanically.

_Living doc — pull from here into ROADMAP phases; add rows as work is deferred._
