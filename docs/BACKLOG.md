# BACKLOG — Dead Reckoning

Prioritized task trough. P0 = do now / blocking · P1 = should do soon · P2 = nice to have / deferred.
Structural items come from the 2026-08-22 full review (see [../STRUCTURE.md](../STRUCTURE.md) debt).

## P0
_(none)_

## P1 — structural (decompose the God-file)

`SkullGuide.cs` (~1600 lines, ~9 responsibilities) is the dominant debt. Extract in this order — each
seam is low-risk because the logic is already grouped and mostly self-contained:

- **Quest-objective resolution → `QuestObjectiveResolver`.** Move `QuestObjectives`, `RefreshQuestTarget`,
  `LastGoldToken`, `StripTags`, `FindNpcByName`, `ResolveQuestLocation`, `FindLocationRooms`,
  `MarkerNames`, `NormalizeName`, `SameRoomSet`, `BuildQuestHud`, `DumpQuest` out of `SkullGuide`.
  ~250 lines, almost all pure/static — the cleanest cut. `SkullGuide` keeps only "here's the resolved
  target".
- **Diagnostics/probe dumps → `DrProbes`.** `DumpBlob`, `DumpWidget`, `WalkPicker`, `DumpPickerCard`,
  `ProbeNearbyLayers`, `ColorHex`, and the `*Probed` one-shot flags. All VerboseLogging-gated, zero
  gameplay coupling.
- **Target model → a `TrackTarget` value type.** Replace the six parallel fields (`tracked`,
  `trackedRooms`, `pinRoom`/`pinRoomPos`/`hasPin`, `trackedQuestNode`/`trackedQuestData`) with one
  type whose setters enforce the single-active-target invariant *structurally*, killing the
  hand-maintained "clear the other three" footgun that has already caused one bug.

## P1 — shared UI seam

- **Extract `HoverScale` out of `RelationshipTrackButton.cs`.** It's a general UI behaviour used by
  four files (Relationships button, Quest button, `TrackHud`, `SkullGuide`'s stop button); it doesn't
  belong inside the Relationships button file.
- **De-duplicate `FindDeep`/`FindChildDeep`.** The same recursive deep-child transform search is
  copy-pasted in `SkullGuide`, `MapMarkerHighlight`, `MapMarkerTint`, `RelationshipTrackButton`
  (and a shallow `Find` variant in `QuestTracker`). Give it one home (a `DRUi` static alongside
  `DRIcons`, or fold into `DRIcons`).

## P2 — smaller structural / naming

- **Rename `QuestTracker.cs` → `QuestTrackButton.cs`.** It contains the Quest Log *button* UI, not the
  quest tracking logic (which is in `SkullGuide`) — the name misdirects.
- **Flame recolour → `FlameRecolor` helper** (from `SkullGuide`: `CacheFlame`, `ApplyFlameColor`,
  `ParseHtml`, the `flameMats`/`flareParticles` state). Self-contained ~60 lines; extract when the
  recolour pass (Phase 6) grows.
- **NPC picker UI → `NpcPickerController`.** `OpenNpcPicker`, `AttachStopButton`, `ClosePicker`,
  `SetPickerBlock`, `OnNpcPicked`, `BuildNpcRoster`, `DetachPicker`. Larger cut (touches shared
  target state) — do after the `TrackTarget` extraction lands.
- **Consider a shared base/util for `MapMarkerHighlight` + `MapMarkerTint`** — they share the ping
  waves, `FindDeep`, and `ColorLibrary` base-colour patterns. They target different widget shapes, so
  only extract the genuinely shared bits (ping, base-colour read); don't force a common abstraction.

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

_Living doc — pull from here into ROADMAP phases; add rows as work is deferred._
