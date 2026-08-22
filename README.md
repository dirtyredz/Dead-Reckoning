# Dead Reckoning

A floating soul-blob skull that hovers near you and always drifts toward whatever you are
tracking — an NPC, a house or place, or a pin you drop on the map. You physically follow it
instead of reading an icon pinned to the screen edge.

**Status:** ✅ **Released — [Nexus mod 144](https://www.nexusmods.com/moonlightpeaks/mods/144),
current version v1.1.0** (first published 2026-08-21 at v1.0.0; the page's Version field was bumped
to 1.1.0 on 2026-08-22 to match the file). Builds clean and auto-deploys to
`BepInEx/plugins/MoonlightPeaksMods/DeadReckoning`. A replacement for lockyaw's
[On-screen Quest and Character Tracker](https://www.nexusmods.com/moonlightpeaks/mods/48) (#48).

Release material: [CHANGELOG.md](CHANGELOG.md), [RELEASING.md](RELEASING.md), the Nexus page draft
([NEXUS.md](NEXUS.md) + paste-ready [nexus-paste.md](nexus-paste.md)), and `screenshots/`. Build the
archive with `powershell -File pack.ps1` → `dist/DeadReckoning-1.0.0.zip`.

Full design history and background live in [DESIGN.md](DESIGN.md).

For general modding setup see the root docs — especially
[03-dev-environment.md](../../03-dev-environment.md),
[04-first-mod-walkthrough.md](../../04-first-mod-walkthrough.md), and
[09-exploring-the-assembly.md](../../09-exploring-the-assembly.md).
This file covers only what's specific to this mod.

## What it does

- **Spawns a skull soul-blob** by cloning the game's own critter, with its flee behaviour
  disabled and steering driven by `Mover.Move`.
- **Tracks a single active target:** an NPC (native picker or a Track button on each
  Relationships card), a house/place, or a free pin dropped from the map.
- **Routes across rooms** (`RoomRouter`), steers along an on-screen line, respects wall
  collision, keeps a distance leash, and scales speed with the player.
- **Save-safe** — spawns a runtime critter and writes nothing to the save.

## Layout

```
mods/DeadReckoning/
├── README.md          <- this file
├── DESIGN.md          <- design notes, session hand-off, open TODOs
├── Directory.Build.props
└── src/               <- flat plugin source (netstandard2.1)
    ├── Plugin.cs      <- BepInEx entry point
    ├── SkullGuide.cs  <- the soul-blob skull and its steering
    ├── RoomRouter.cs  <- cross-room routing
    ├── MapPin.cs / PickerCardHighlight.cs / MapMarkerTint.cs <- map pin + badge/card visuals
    ├── TrackHud.cs / RelationshipTrackButton.cs              <- tracking HUD + card button
    └── CameraScrollPatch.cs                                  <- Far Sight scroll coexistence
```
