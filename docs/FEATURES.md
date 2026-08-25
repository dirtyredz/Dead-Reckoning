# FEATURES — Dead Reckoning

Capability inventory + status. ✅ shipped · 🚧 partial · 💡 planned (see [ROADMAP.md](ROADMAP.md)).

## Tracking targets

| Feature | Status | Notes |
|---|---|---|
| Track an NPC | ✅ | Native `PickNpcScreen` picker (`PickNpcKey`, default F6), a Track pin on every Relationships/character-screen card, or double-clicking their map badge. |
| Track a house/place | ✅ | Double-click a place badge on the map. Routes across rooms to it, idles on arrival. |
| Free map pin | ✅ | Double-click any empty map spot; resolves the exact in-room world position. |
| Track a quest | ✅ | "Seek Quest" button in the Quest Log; follows the current objective's target NPC or gold-token location. Added in v1.1.0. |
| Track a job | ✅ | "Seek Job" button on a job's Quest Log entry; steers to the job's hand-in NPC (`CompletionNpcConfigAsset`), auto-stops when the job completes or expires. Also fixed the stale green "Seeking Quest" button jobs used to inherit (jobs render via `ShowJobInfo`, which was unpatched). Unreleased. |
| Recipient from dev-name | ✅ | When an objective names its recipient only in its **internal dev-name** (not the visible title or as a requirement) — e.g. shown as "the little pond" but named "Bring a copper bar to Yabbis' pond" — DR scans that name for a real NPC (`FindNpcMentioned`, whole-word) and routes to them, ahead of the vague region. |
| Gather/mine node precision | ✅ | For a "gather/mine `<item>`" objective, once you're in the target region the skull points at the actual harvestable/mineable node in the scene (the copper vein, the bush), not just the region. `QuestNodeLocator`: reads the objective's item from its `InjectionCollection`, scans loaded `Interactable`s for a matching `IHarvestable`/`DestructibleView`. Delivery/turn-in objectives stay region-level (no data link). |
| Single active target | ✅ | One target at a time; a new target replaces the old. |

## The skull

| Feature | Status | Notes |
|---|---|---|
| Save-safe soul-blob spawn | ✅ | Runtime critter, writes nothing to the save; auto-respawns across room changes while tracking. |
| Lead-vs-follow behaviour | ✅ | Leads *ahead* toward a live fix (proportional chase, settles to the standoff). When not seeking it loosely *follows you* via a spring-damper with its own momentum — genuinely lags when you run, then springs to catch up and gently overshoots. It settles on the nearest point of a horizontal sphere around you (so a lagging skull rests behind you, never flies over your head to a fixed spot). Tunable via `IdleFollowSpring` / `IdleFollowDamping`. Lead-toward-target vs. follow-you is the "am I seeking?" tell. |
| A* route following | ✅ | Leads along the walkable path around furniture/walls (`FollowPath`). |
| Cross-room routing | ✅ | Points at the door toward the target's room (BFS over the room graph). |
| Wall collision | ✅ | Spherecast + slide against the obstacle layer (`Collide`). House walls (Ground layer) are a known accepted clip. |
| Ground/bridge clearance | ✅ | Raise-only clamp rides bridges/stairs, never drops into water (`GroundClearance`). |
| Distance leash | ✅ | Snaps back beside you past `MaxLeash`. |
| Speed scales with player | ✅ | Outruns sprinting (e.g. cat form). |
| Flame recolour | ✅ | Recolours the soul-blob flame to a configured hex (`RecolorFlame`/`FlameColor`); also stops per-spawn colour variance. |

## Feedback

| Feature | Status | Notes |
|---|---|---|
| Seeking HUD | ✅ | Screen overlay of what's being sought, with a ✕ stop control; position/size/visibility configurable. |
| Quest objectives HUD | ✅ | Full objectives list mirroring the Quest Log (active vs completed styling). |
| Map free-pin marker | ✅ | Red diamond + white ping waves. |
| Tracked-badge highlight | ✅ | Recoloured badge + ping for a tracked house/NPC; follows an NPC live between their badge and their house. |
| Picker card highlight | ✅ | Tracked card gets the native selection frame cloned purple. |

## Compatibility & config

| Feature | Status | Notes |
|---|---|---|
| Far Sight coexistence | ✅ | Far Sight stands down (no scroll-zoom) over our picker/Relationships panel. |
| Scroll-zoom block | ✅ | Camera doesn't zoom while our menus are open (also without Far Sight). |
| Mod Menu / ModNook sections | ✅ | Config tagged into General / Follow tuning / Diagnostics sections. |
| Custom Track icon | ✅ | `track-icon.png` bundled; override at `BepInEx/config/DeadReckoning/track-icon.png`. |

## Planned / not done

| Feature | Status | Notes |
|---|---|---|
| In-house precision | 🚧 | Gather/mine objectives now point at the exact scene node (`QuestNodeLocator`). Still idles for a tracked house/NPC once inside their room, and for quest delivery/turn-in objectives (no world-position data). |
| Non-NPC pet tracking | 💡 | Pets (cat) don't resolve when their live entity guid mismatches. |
| Skull/aura recolour pass | 💡 | Broader Tastic-Palette-style recolour of the skull + VFX (flame recolour already shipped). |
