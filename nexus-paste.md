# Dead Reckoning — Nexus page source (BBCode, current style)

**Nexus page:** _not yet published_ — this is the first-release draft.

The description field is **SCEditor with a BBCode source**, so the block below is the literal
value to set (via `textarea._sceditor.val(...)` + `updateOriginal()`, per
[13-nexus-page-standard.md](../../13-nexus-page-standard.md)). Look/structure follow
[15-page-style.md](../../15-page-style.md) and [14-description-review.md](../../14-description-review.md).

**Traps to remember:** every paragraph below is a single unwrapped line on purpose — do not
re-wrap them or the editor turns each wrap into a `<br>`. Use the toolbar list button for the
`[list]` blocks. `[hr]`/`[line]` is stripped on the live page — the rules are literal `─` runs.
Verify on the live page, never the editor.

## Other fields

| Field | Value |
|---|---|
| Name | `Dead Reckoning` — clean, no subtitle |
| Category | Gameplay — where a navigation/follower aid is browsed for, alongside the tracker it replaces |
| Short description | see tagline below |
| Tags | fixed per-game vocabulary — confirm on the form. Candidates: `quality of life`, `gameplay`, `user interface`, `immersion` |
| Licence | MIT — set Nexus permissions to match (see RELEASING.md) |

## Description source

```bbcode
[size=6][color=#F7D994]💀  Dead Reckoning[/color][/size]
[color=#C7A25B][i]A little skull floats beside you and drifts toward whatever you're looking for — an NPC, a house, a quest, or a spot you pin on the map. Follow it there instead of squinting at an arrow.[/i][/color]
[color=#C7A25B]💀 A skull to follow  ·  🧭 NPCs, quests, houses & pins  ·  🗺️ Leads you around walls  ·  💾 Save-safe[/color]
[color=#7A6A9B]────────────────────────────────────────[/color]
[quote]💾  [color=#F7D994][b]Save-safe.[/b][/color] It spawns one of the game's own critters at runtime and writes nothing to your save — uninstalling leaves no trace.[/quote]

[size=5][color=#F7D994]🧭  What it does[/color][/size]
[color=#D4D4D8]Moonlight Peaks points you toward things with an arrow pinned to the edge of the screen. Dead Reckoning gives you something to follow instead: a little skull — the game's own soul blob — floats at your side and drifts toward whatever you are seeking.

Pick an NPC from the game's own picker, or tap Track on their card in your Relationships. Seek the objective of your active quest, wherever it is. Point it at a house on the map, or drop a free pin anywhere and let it lead you there. It routes through the house room by room and leads you around furniture and walls, not through them — you just walk after the skull.

It is the game's real critter, spawned at runtime, so nothing is ever written to your save. Recolour its flame if you want it to match your vibe.[/color]

[size=5][color=#F7D994]🎬  Videos[/color][/size]
[youtube]ilPYXCqTqmY[/youtube]
[youtube]YeFgi__G8Fs[/youtube]

[size=5][color=#F7D994]✨  Main features[/color][/size]
[list]
[*]A floating skull — the game's own soul blob — hovers at your side and drifts toward what you seek
[*]Seek an NPC from the game's own picker, or tap [b]Track[/b] on any card in your Relationships panel
[*]Seek the objective of your active quest, wherever it happens to be
[*]Seek a house or place from the map — or drop a free pin anywhere and follow it there
[*]Leads you room to room through the house, and around furniture and walls rather than through them
[*]Keeps up when you sprint, and snaps back to your side if it ever falls behind
[*]On-screen text names what it is currently seeking, and the map marks the target
[*]Recolour the skull's flame to any colour — which also stops it varying between spawns
[*]One target at a time; switch to a different NPC, quest, house or pin whenever you like
[*]Quest tracking gets specific — it points at the actual quest NPC, the resource node to gather (the vein, the bush), or the exact recipient, not just the general area
[*]Save-safe: it spawns a runtime critter and writes nothing to your save
[/list]

[size=5][color=#F7D994]🧭  A note on quest tracking[/color][/size]
[color=#D4D4D8]Moonlight Peaks doesn't store a map point for a quest step — the game just writes the objective text and leaves the rest to you. So Dead Reckoning works out where to send you by reading that objective: it'll aim you at the quest's NPC, at the thing you need to gather, or at the place named in the step.

Because it's reading the quest rather than being handed a waypoint, two things can happen. Sometimes a step only tells it the rough area — "somewhere in Moonlit Pines" — and once you're there it can't pin the exact spot, so it stops leading and just floats along beside you instead. That drifting-at-your-side (rather than scouting out in front) is the tell that it's got you as close as the quest lets it. And once in a while a quest is worded in a way that sends the skull to the wrong thing entirely.[/color]
[quote]🐛  [color=#F7D994][b]Sent somewhere wrong?[/b][/color] If the skull leads you to the wrong place for a quest — or won't lead at all when it should — please post the [b]exact quest name[/b] (and the objective text) in the Bugs tab. With the quest named I can teach it that specific step. Example on file: [i]A Croak and a Crest[/i].[/quote]

[size=5][color=#F7D994]📋  Requirements[/color][/size]
[b]Required[/b]
[list]
[*][b]BepInEx 5 (win_x64)[/b], version 5.4.23.5 or newer
[/list]
[b]Recommended companion[/b]
[list]
[*][b]Mod Nook[/b] — my in-game settings menu. Rebind the picker key by pressing it, pick the skull's flame colour from a swatch, and set the hover distance, height and follow feel on sliders — every change applies the moment you close the menu. Nothing here needs it; without it the settings live in a plain config file. https://www.nexusmods.com/moonlightpeaks/mods/127
[*][b]Mod Menu[/b] by Elsiabeth does the same job and is also supported. Mod Nook and Mod Menu can both be installed — each adds its own button and neither interferes with the other.
[/list]
[color=#D4D4D8]PC/Steam only. The Switch and mobile builds cannot load BepInEx.[/color]

[size=5][color=#F7D994]📥  Installation[/color][/size]
[b]🟢 With Vortex[/b]
[color=#D4D4D8]Open the Files tab, click the Vortex button, and enable the mod. Done.[/color]

[b]🔧 Manually[/b]
[list=1]
[*]Install [b]BepInEx 5 (win_x64)[/b] into your Moonlight Peaks folder, if you do not have it already. The BepInEx folder sits beside Moonlight Peaks.exe.
[*]Launch the game once, then quit. This creates the BepInEx/plugins folder.
[*]Download the archive from the Files tab and extract it over your Moonlight Peaks folder, so the files end up at BepInEx/plugins/DeadReckoning/DeadReckoning.dll (a track-icon.png ships beside it — keep them together).
[*]Launch the game.
[/list]
[color=#D4D4D8]To uninstall, delete the BepInEx/plugins/DeadReckoning folder. Your save is untouched, because nothing was ever written to it.[/color]

[size=5][color=#F7D994]🎛️  Configuration[/color][/size]
[color=#D4D4D8]Settings are written to BepInEx/config/com.dirtyredz.moonlightpeaks.deadreckoning.cfg on first launch. By default F6 opens the NPC picker; seek a house or place by double-clicking it on the open map, and clear the current target from the on-screen panel. Everything else — hover distance and height, how eagerly it follows, the flame colour, the on-screen text — has a sensible default.[/color]
[quote]🎛️  [color=#F7D994][b]Nicer in Mod Nook.[/b][/color] Install [url=https://www.nexusmods.com/moonlightpeaks/mods/127]Mod Nook[/url] and you rebind the picker key by pressing it, pick the flame colour from a swatch, and feel the follow tuning out on sliders — all in game, applied the moment you close the menu. Your config file does not change.[/quote]

[size=5][color=#F7D994]🤝  Compatibility[/color][/size]
[color=#D4D4D8]Save-safe and self-contained — it spawns a runtime critter and patches nothing other mods rely on, so it sits happily alongside the rest of your list. If you run Far Sight, Dead Reckoning asks it to hold its scroll-zoom while the NPC picker or Relationships panel is open, so the two stop fighting over the mouse wheel.[/color]

[size=5][color=#F7D994]💜  Shout outs[/color][/size]
[list]
[*][b]Little Chicken Game Company[/b] for the soul blob — this mod barely builds a thing of its own; it borrows one of their critters and asks it to point the way.
[*]The [b]BepInEx[/b] and [b]HarmonyX[/b] teams, without whom none of this scene exists.
[*][b]lockyaw[/b] for On-screen Quest and Character Tracker, the mod this reimagines — studying its quest tracker is where the Track-button-on-a-card idea came from.
[*][b]Elsiabeth[/b] for Mod Menu, which made the case that in-game settings were worth having, and which is why this mod never had to build a settings screen of its own.
[*][b]My Mate[/b], for being my inspiration.
[/list]
```
