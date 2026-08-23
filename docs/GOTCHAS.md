# GOTCHAS — Dead Reckoning

Non-obvious traps, footguns, and environment quirks. Read before touching steering, the target model,
or the UI patches.

## Steering & the critter

- **`ForceOnGround` defaults `true`.** `CharacterMover` snaps to terrain height each frame; the game's
  own fly states turn it off. We disabled the critter's behaviour, so we must set
  `Mover.ForceOnGround = false` on spawn ourselves — otherwise the skull hugs the ground and drops
  into rivers/pits.
- **Soul-blob mover obeys `Mover.Move`; the Draculamb's does NOT.** `SoulblobMovementBehaviour`
  applies fed-in movement to the transform. The Draculamb's `FloatingMovementBehaviour.UpdateMovement`
  zeroes movement on line 1 and is force-only — a different (rejected) path. Don't copy Draculamb
  steering assumptions here.
- **`NavPosition` is per-room offset space, not world.** Never steer directly at an off-room NPC's
  `NavPosition` — it aims nowhere. Route to the **door** via `RoomRouter` instead.
- **House walls are on the Ground layer [9], not Obstacle.** Wall-avoid uses `ObstacleLayerMask` only
  (walls/buildings that block flyers); house *interior* walls on the Ground layer are a **known,
  accepted clip**. Adding Ground to the mask caused a floor-first `SphereCast` bug that was worse.
  See [DECISIONS.md](DECISIONS.md) ADR-004.
- **Layer masks are read live** from `AssetLibrary` (cached after first read), so a game update that
  renumbers layers self-adjusts. Don't hardcode layer indices.
- **The skull mesh floats above the mover point.** `SkullVisualLift` (measured from renderer bounds)
  is subtracted from the steer point so the visible mesh lands on the sightline — not the mover.
- **The soul-blob is a room object.** It's destroyed on room unload (`active` goes Unity-null); the
  `Update` loop auto-respawns it (throttled) while `wantActive`. `ResolveSources` re-finds the room's
  `SoulblobSpawner`/`GridSelector` each time.

## The target model

- **Single-active-target invariant is hand-maintained.** There are now **five** parallel target
  fields (`tracked` NPC / `trackedRooms` place / `hasPin` free-pin / `trackedNode` gather-mine node /
  `trackedQuestNode` quest) and setting one MUST clear the others. The mutation sites are **not just
  the setters** (`SetTracked`, `SetTrackedRooms`, `SetPin`, the NPC-pick handler, `TrackQuest`,
  `ClearTarget`) — the highest-risk ones are the **four inline apply-branches in `RefreshQuestTarget`**
  that write the fields directly without going through a setter. STRUCTURE.md's debt section counts
  the current sites (~9); keep them in sync. Miss one and a stale target shadows the new one (this
  exact bug once broke NPC tracking after a free-pin). Backlogged for extraction into a `TrackTarget`
  type — the single most valuable fix here.
- **Match NPCs by identity, not reference.** A quest-resolved `NpcConfigAsset` can be a different
  instance than the picker/map widget's — use `SameNpc` (compares `Entity.SerializedGuid`), not `==`.
- **Free-pin in-room position assumes the player's persisted `RoomPosition` is current.** `PinWorld`
  computes the pin relative to the player's room position; fragile if that persisted value lags.
- **Quest resolution is heuristic — order and matching matter.** `RefreshQuestTarget` resolves an
  objective in a deliberate priority: NPC-requirement → gold-token NPC → dev-name NPC
  (`FindNpcMentioned`) → gather/mine node → gold-token place. The **gold token is the game's curated
  target, so it must be tried before the free-form dev-name** — a dev-name can mention a non-target NPC
  in passing, and letting it pre-empt the visible gold-token character would mis-route. If you reorder
  these, keep gold-token-NPC ahead of the dev-name scan.
- **Name-matching is word-level, not substring, on purpose.** `FindNpcMentioned` matches whole-word
  runs (so "Ed" can't match "Echoes" and multi-word names like "Old Man Jenkins" still resolve), and
  `QuestNodeLocator.Matches` compares item-name **word sets** (so "Coal" doesn't match "Charcoal").
  Don't "simplify" either back to `string.Contains` — that reintroduces cross-family false positives.

## UI / Harmony

- **Far Sight loads AFTER us**, so `AccessTools.TypeByName("FarSightPlugin")` is null in our `Awake`.
  The coexistence patch is attached lazily from `SkullGuide`'s first `Update`
  (`TryPatchFarSight`, one-shot). Confirm via the "Far Sight detected…" log line.
- **`Input` in `SkullGuide`/`CameraScrollPatch` is the game's global class**, not `UnityEngine.Input`
  — `Input.MousePosition` / `Input.MouseScrollDelta`. `UnityEngine.Input` is used explicitly where
  needed (`UnityEngine.Input.GetMouseButtonDown`).
- **TMP has no closing `</alpha>` tag.** Reset alpha with `<alpha=#FF>` instead (used in the quest HUD
  strike-through styling).
- **The Seeking HUD owns its own material instance** for the Gelica font — writing an outline onto the
  shared material would outline every Gelica text in the game.
- **Scroll-zoom has two paths.** The game reads scroll via `Input.MouseScrollDelta` (patched) *and*
  `GameCamera.ProcessPlayerCameraToggle` (patched); Far Sight has its *own* third path (the reflection
  patch). All three are gated on `WorldScrollBlock.Blocked`.

## Config & release

- **Changing a code default does NOT rewrite an existing `.cfg`.** e.g. bumping `MaxLeash`'s default
  won't change a user's saved value — they must set it in the mod menu. Call this out in release notes.
- **`pack.ps1` and `Directory.Build.props` are workspace-synced canonicals** — never edit them in this
  mod; they're regenerated by `../../tools/sync-mod-files.ps1`. Version bumps go in the csproj only.
- **`VerboseLogging` gates all diagnostic probes** (`DR-*` dumps). Keep it OFF by default for release.
