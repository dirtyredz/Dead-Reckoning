---
id: dk-d58fe15f
type: task
created: 2026-10-05
status: todo
since: 2026-10-05
area:
priority: P1
rank: w
parent:
fixes: []
blocked_by: []
relates: []
---
# SkullGuide: NPC picker UI to NpcPickerController and map input to MapTrackingInput

From docs/BACKLOG.md (P1 — decompose the `SkullGuide` God-file)

3. **NPC picker UI → `NpcPickerController`** and **map input → `MapTrackingInput`.** Move the hotkey/
   picker construction/stop-button/input-blocking block and the double-click/hit-test/free-pin block
   (~350–400 lines). Both should *issue tracking commands*, not mutate target fields directly.
