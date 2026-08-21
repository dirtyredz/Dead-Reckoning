# Dead Reckoning

A floating soul-blob skull that hovers near you and always drifts toward whatever you are
tracking — an NPC, a house or place, or a pin you drop on the map. You physically follow it
instead of reading an icon pinned to the screen edge.

**Status:** 🚧 **In progress — not yet published on Nexus.** Builds clean and auto-deploys to
`BepInEx/plugins/MoonlightPeaksMods/DeadReckoning`. A replacement for lockyaw's
[On-screen Quest and Character Tracker](https://www.nexusmods.com/moonlightpeaks/mods/48) (#48).
This README gets a Nexus link once the mod ships.

Full design, current state, and the open TODO list live in [DESIGN.md](DESIGN.md) — read its
`SESSION HAND-OFF` section first.

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
    ├── MapPin.cs / MapMarkerHighlight.cs / MapMarkerTint.cs  <- map pin + badge visuals
    ├── TrackHud.cs / RelationshipTrackButton.cs              <- tracking HUD + card button
    └── CameraScrollPatch.cs                                  <- Far Sight scroll coexistence
```
