# Nexus Mod Page — Dead Reckoning

> **Pasting into the upload form? Use [nexus-paste.md](nexus-paste.md), not this file.**
> The copy here is wrapped for reading, and the editor turns every wrap into a `<br>`.
> Page structure/mechanics: [13-nexus-page-standard.md](../../13-nexus-page-standard.md).
> Look: [15-page-style.md](../../15-page-style.md). Review: [14-description-review.md](../../14-description-review.md).

Draft copy for the first Nexus listing. This is a **new mod page**, not an edit — there is no
live page to pull BBCode from yet, so `nexus-paste.md` is the source of truth for the description.

---

## Fields

| Field | Value |
|---|---|
| **Name** | Dead Reckoning |
| **Summary** (short, shows in listings) | A little skull floats beside you and drifts toward whatever you're looking for — an NPC, a house, a quest, or a spot you pin on the map. Follow it there instead of squinting at an arrow. |
| **Category** | Gameplay — where a navigation/follower aid is browsed for, alongside the tracker it replaces |
| **Version** | 1.2.0 |
| **Nexus page** | [mod 144](https://www.nexusmods.com/moonlightpeaks/mods/144) — live since 2026-08-21 |
| **Requirements** | BepInEx 5 (win_x64), 5.4.23.5 or newer — required |
| | [Mod Nook](https://www.nexusmods.com/moonlightpeaks/mods/127) — optional, for in-game settings |
| | Mod Menu — optional, the alternative to Mod Nook |
| **Tags** | fixed per-game vocabulary — confirm on the form. Candidates: quality of life, gameplay, user interface, immersion |
| **Licence** | MIT (see [LICENSE](LICENSE)) |

**Replaces [On-screen Quest and Character Tracker](https://www.nexusmods.com/moonlightpeaks/mods/48)
by lockyaw** — same job, opposite approach: a thing you follow through the world instead of an
arrow at the screen edge. Credit them on the page (already in the shout outs).

---

## Full description

The paste-ready, correctly-styled version is in [nexus-paste.md](nexus-paste.md). The prose there
is final; this file exists for the fields, the gallery plan, and the notes below.

---

## Screenshots

Files live in `screenshots/`. On the upload form the **thumbnail** and the **banner/header image**
are set separately from the gallery — pick them there; the notes below flag the candidates. There
is no fabricated banner: the shots below are the real captures the user provided.

> ⚠️ **Listing thumbnails stretch, they do not crop.** Tiles use `object-fit: fill`, so a thumbnail
> that isn't ~16:9 is squashed (see Coffin Break's NEXUS.md). None of the captures below is a clean
> 16:9 — if the thumbnail looks distorted on the live tile, compose a 16:9 one from the widest shot.
> Gallery/description images (any ratio) just scale, so they are fine as-is.

| # | Shot | File | Ratio | Note |
|---|---|---|---|---|
| banner? | Skull with recoloured flame, wide scene | `01-change-flame-colour.png` | 2.80:1 | widest full scene — best banner candidate |
| 1 | Skull leading you to a location | `02-follow-to-location.png` | 1.03:1 | the core pitch — the skull out in front |
| 2 | Seeking a house/place from the map | `03-house-seeking.png` | 1.30:1 | |
| 3 | A free pin dropped on the map | `04-free-pin.png` | 1.32:1 | |
| 4 | Map marker / target badge | `05-map-icon.png` | 1.00:1 | |
| 5 | The NPC picker | `06-npc-picker.png` | 0.92:1 | |
| 6 | Track button on a Relationships card | `07-relationships-track.png` | 2.08:1 | shows the added Track control |
| 7 | Seeking a selected NPC | `08-selected-npc-seeking.png` | 4.33:1 | very wide — gallery only |
| 8 | NPC tracking in world | `09-npc-tracking.png` | 1.19:1 | |
| 9 | NPC seeking overlay | `10-npc-seeking-overlay.png` | 0.70:1 | on-screen seeking text |
| 10 | Seeking a quest objective | `11-quest-seeking.png` | 1.00:1 | |
| 11 | Quest seeking overlay | `12-quest-seeking-overlay.png` | 1.11:1 | |

**Videos (set aside for this release):** two clips exist in the user's Downloads —
*Dead Reckoning Video Follow your Soul Blob.mp4* and *Dead Reckoning Quest Tracking.mp4*. Nexus's
image gallery does not take video; add them later as YouTube links or trim to GIFs. Not blocking 1.0.0.

---

## Notes before publishing

- **Save-safe is the headline for this community — and it is true here.** The skull is a runtime
  `CritterView`, whose `IsRegisteredInPersistence` is false, so nothing is written to the save. Say
  it plainly; this scene reads for it.
- **Credit lockyaw.** This mod openly reimagines their tracker (#48). The shout out names them and
  the specific thing their mod taught (the Track-button-on-a-card idea).
- **Set Nexus permissions to agree with MIT** (see RELEASING.md) — upload/convert/modify/reuse all
  allowed, credit customary not required.
- **No "created with AI" line** — per the standing decision (15-page-style / 13-standard), mod pages
  don't carry one, and don't add the `AI Media` tag.
- **Verify on the live page after saving**, not in the editor: header version, the file row,
  `?tab=logs` changelog, and that the description rules (`─`) actually rendered.
