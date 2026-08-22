# ROADMAP — Dead Reckoning

Phase-by-phase plan. The mod is **released and feature-complete for its core**; remaining work is
polish and the recolour ambition. Prioritized tasks live in [BACKLOG.md](BACKLOG.md).

## ✅ Phase 1 — Floating skull proof (shipped)
Spawn the save-safe soul-blob critter, disable its flee behaviour, drive `Mover.Move`, hover at a
standoff, idle-wander when untracked.

## ✅ Phase 2 — NPC tracking (shipped, v1.0.0)
Native `PickNpcScreen` picker, Relationships/character-screen Track button, live in-room tracking,
cross-room door routing, wall collision, ground clamp, leash, speed scaling.

## ✅ Phase 3 — Places & the map (shipped, v1.0.0)
Track houses/places and free-pin spots from the map; map markers, ping waves, picker highlight;
Seeking HUD; Far Sight scroll coexistence. First Nexus release (mod 144).

## ✅ Phase 4 — Quest tracking (shipped, v1.1.0)
"Seek Quest" button in the Quest Log; follow the current objective (target NPC or gold-token
location); quest objectives HUD; A* route following; flame recolour.

## 🚧 Phase 5 — Precision & polish (next)
- In-house precision: point at the exact `EntityLocation` once inside the target room (today it idles).
- Non-NPC pet tracking (match `CreatureView`/critter entities by guid).
- House-wall collision on the Ground layer (accepted clip today) — revisit if a clean fix appears.

## 💡 Phase 6 — Recolour pass (future)
Full Tastic-Palette-style recolour of the skull mesh + aura/VFX (flame recolour already shipped as a
first step). Clone the skull keeping renderers/materials/VFX reachable.

## Health / maintenance (ongoing)
- **Structural:** decompose the `SkullGuide` God-file (see [BACKLOG.md](BACKLOG.md) P1s) before the
  next large feature lands more logic in it.
- Re-verify layer masks and asset entry points after each game update (masks are read live, so they
  self-adjust, but confirm the mod still loads clean).

_Living doc — advance a phase when it ships._
