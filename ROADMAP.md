# Embodied-Data-VR — Completion Roadmap

Status snapshot (2026-08-02): The July refactor rewrote nearly every script but the
scene was not fully re-wired. Datasets (6), audio clips, and cue prefabs exist. VR rig
is present. Blocking issues: SessionController null refs, incomplete ConditionManager
list, a possibly-missing 5th condition, and the whole refactor is uncommitted.

We progress one milestone at a time. Check items off as we go.

## Phase A — Stabilize & get it running (software)
- [x] **M0. Secure the work.** Stale `.git/index.lock` cleared; refactor committed (`002f3d2` on `develop`, 52 files). Working tree clean.
- [x] **M1. Confirm clean compile.** Unity console clean — zero errors/warnings after the refactor.
- [x] **M2. Re-wire SessionController.** Assigned `graphManager`, `conditionManager`, and `player` (-> DesktopWalker for now; repoint to XR head at M9). Verified non-null in saved scene.
- [x] **M3. Re-wire ConditionManager.** `conditions` list populated with all 5 (Control, Abstract, Representative, SemanticAudio, SemanticVisual) in id order; null entry removed. All ids unique 0-4.
- [x] **M4. Confirm all 5 conditions exist & are configured.** Verified against modified_plan (authoritative): id0 Control (none); id1 Abstract (sphere+beep, reacts); id2 Representative (spark+electric, reacts); id3 MismatchStatic (sphere+electric, STATIC — audio does not react to walk); id4 MismatchNonRep (spark+beep, reacts). Fixed id3 `respondToValue`->false. Note: code enum names for id3/id4 (SemanticAudio/SemanticVisual) are stale vs the plan — cosmetic only.

## Phase B — End-to-end validation (desktop)
- [x] **M5. Full desktop run-through.** Ran a full session in play mode (5 trials, one per condition) through Walk -> Distractor -> Retrace -> Recall to Complete, zero runtime errors. Verified scene wiring at runtime: SessionController has all refs + 6 datasets + debugKeys on; ExperimentController shortcuts off (no key clash). (2026-08-15)
- [x] **M6. Verify data logging.** Confirmed per-participant JSON at `<persistentData>/StudyData/`. Well-formed: 5 trials in counterbalanced order, each with walk/distractor/retrace durations, randomized distractor start (300-999), retrace path (15 samples @ 0.1s w/ real x/z), and 4 recall questions with recorded answers + verifiable scores. (2026-08-15)
- [x] **M7. Validate recall questions.** Audited all 6 datasets: the original fixed distractors (+3/-4/+6) happened to produce valid, distinct options on the current data, BUT the correct answer sat in a fixed slot per question index (predictable across trials). Rewrote MakeNumericQuestion: (1) correct-answer slot now randomized via a reproducible seed (participantId ^ orderIndex ^ qIndex), (2) distractors scaled to the dataset range (step = round(range*0.15)) and guaranteed distinct + non-negative by display value. Re-audit via reflection on the real code path: 0 duplicate options, slot distribution 5/8/7/4 across 24 questions, reproducible. Known-accepted (not fixed): when a walk ends at its minimum, the "last value" and "lowest value" questions share the same answer (data/design artifact). (2026-08-15)
- [ ] **M8. Graph/grid/tag visual pass.** Value tags legible at constant brightness; grid spacing matches data points.

## Phase C — VR bring-up
- [ ] **M9. Headset test.** Run in Quest 2 / Varjo; confirm room-scale walk maps to PathSampler values correctly.
- [ ] **M10. Cue verification in VR.** Density field, audio pitch/tempo, and per-condition assets behave correctly while walking.
- [ ] **M11. Audio mode selector.** Pitch-only / tempo-only / both selectable from UI and applied session-wide.
- [ ] **M12. Session UI in VR.** Distractor task, retrace prompt, and recall questions usable in-headset.

## Phase D — Study execution
- [ ] **M13. Counterbalancing check.** Verify the Latin-square order rotates correctly across participant IDs.
- [ ] **M14. Pilot (1-2 participants).** Shake out usability/logging issues; fix.
- [ ] **M15. Data collection.** Run the full participant cohort.

## Phase E — Analysis & writing
- [ ] **M16. Analysis pipeline.** Parse JSON logs -> recall scores + retrace accuracy -> per-condition stats.
- [ ] **M17. Statistical tests.** Within-subjects comparison across the 5 conditions.
- [ ] **M18. Thesis write-up.** Results, discussion, figures.
