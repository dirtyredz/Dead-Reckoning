# Releasing Dead Reckoning

Repo-wide rules live at the root; this file only covers what is specific to this mod.

- Versioning and archive layout: [12-versioning-and-release.md](../../12-versioning-and-release.md)
- Nexus page structure / style / review: [13](../../13-nexus-page-standard.md),
  [15](../../15-page-style.md), [14](../../14-description-review.md)
- Save safety: [11-mod-data-and-saves.md](../../11-mod-data-and-saves.md)

Short version on numbering: the version is for players, not a build counter. Bump it only when
publishing, one CHANGELOG entry per release. First published version is **1.0.0**.

## Build a release

```powershell
powershell -File pack.ps1
```

Produces `dist/DeadReckoning-<version>.zip`, reading the version from the csproj so the archive
can never disagree with the DLL; `Plugin.cs` derives that same version at build time via
`ModBuildInfo.Version`.

**This mod ships two files, not one.** The archive contains both `DeadReckoning.dll` and
`track-icon.png` under `BepInEx/plugins/DeadReckoning/` — the icon is loaded at runtime for the
Relationships "Track" button (`DRIcons` / `RelationshipTrackButton`). `pack.ps1` copies both and
fails if the icon is missing. If a build ever drops the icon, the button silently falls back to
text rather than breaking.

There is no test project: every code path reads Unity and live game state — the soul-blob critter,
Harmony patches, the map widgets, A* pathfinding. The checklist below carries the weight instead.

## Pre-release checklist

Root checklist first: [12-versioning-and-release.md](../../12-versioning-and-release.md).
Then the items specific to this mod:

### The one that matters

- [ ] **The skull spawns, follows, and seeks a real target — and the save is untouched.** Track an
      NPC, a quest, a house and a free pin in one session; confirm the skull leads you to each and
      that the save file's modification time has not moved (it spawns a runtime critter and writes
      nothing). Everything else is detail; this is the mod.

### Seeking each target type

- [ ] NPC via the game picker (default **F6**), and via the **Track** button on a Relationships card
- [ ] The active quest's objective
- [ ] A house/place by **double-clicking** it on the open map
- [ ] A free pin dropped on the map
- [ ] Switching between targets while one is active — only one is ever sought at a time
- [ ] Clearing the current target from the on-screen panel dismisses the skull

### Steering and routing

- [ ] Leads room-to-room through the house rather than pushing at a wall (`RoomRouter`)
- [ ] Follows the walkable route around furniture (`FollowPath = true`), and the direct line with it off
- [ ] Keeps up in cat-form sprint; the leash snaps it back if it falls behind
- [ ] Does not clip through house walls (Obstacle-layer collision)

### Coexistence and housekeeping

- [ ] With Far Sight installed, its scroll-zoom stands down while the picker / Relationships panel
      is open; without Far Sight, no warning is logged
- [ ] `VerboseLogging` defaults to `false`
- [ ] The settings section shows as **General** in Mod Nook / Mod Menu, not a dev label
- [ ] `<Version>` in the csproj is the release number (`Plugin.cs` derives it via `ModBuildInfo.Version`)
- [ ] CHANGELOG has one entry for this version
- [ ] Fresh install: delete `BepInEx/config/com.dirtyredz.moonlightpeaks.deadreckoning.cfg`,
      launch, confirm sensible defaults are written
- [ ] Screenshots show the current build
- [ ] Archive extracted onto a clean install and verified in game — **both** the DLL and
      `track-icon.png` land in `BepInEx/plugins/DeadReckoning/`

## Verifying save safety

The skull is a `CritterView` spawned via `CritterView.SpawnCritter`, and
`CritterView.IsRegisteredInPersistence` is `false` — the critter subsystem builds a runtime entity
and writes nothing to the save. So the check is behavioural: note the save file's modification time
before a seeking session and confirm it has not changed after.

```powershell
$save = "$env:USERPROFILE\AppData\LocalLow\Little Chicken Game Company\Moonlight Peaks\<steam-id>\Saves\<save-guid>\GameData.json"
(Get-Item $save).LastWriteTime
```

## Licence

**MIT** — see [LICENSE](LICENSE). Permissive: anyone may use, modify and redistribute, provided the
copyright notice is kept. Set the Nexus permissions to agree with it, or the page and the licence
contradict each other:

| Nexus permission | Set to |
|---|---|
| Upload to other sites | Allowed |
| Convert to other games | Allowed |
| Modify and release | Allowed |
| Use assets in own files | Allowed |
| Include in mod packs / collections | Allowed |

Credit is customary rather than required under MIT.

## Editing note

Do not round-trip these files through `Get-Content -Raw | Set-Content` in PowerShell — it
re-encodes non-ASCII characters and has corrupted em-dashes in this repo twice. The description
copy in `nexus-paste.md` is full of real em dashes and `─` rule characters; edit it with tools that
preserve UTF-8. `pack.ps1` reads the csproj with `Get-Content` but never writes it back, which is safe.
