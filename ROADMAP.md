# Embodied-Data-VR — Completion Roadmap

Status snapshot (2026-08-02): The July refactor rewrote nearly every script but the
scene was not fully re-wired. Datasets (6), audio clips, and cue prefabs exist. VR rig
is present. Blocking issues: SessionController null refs, incomplete ConditionManager
list, a possibly-missing 5th condition, and the whole refactor is uncommitted.

We progress one milestone at a time. Check items off as we go.

## Phase A — Stabilize & get it running (software)
- [ ] **M0. Secure the work.** Remove stale `.git/index.lock`; commit the current refactor so nothing is lost.
- [ ] **M1. Confirm clean compile.** No errors/warnings in the Unity console after the refactor.
- [ ] **M2. Re-wire SessionController.** Assign `graphManager`, `conditionManager`, `player` (currently null).
- [ ] **M3. Re-wire ConditionManager.** Populate the `conditions` list with all conditions; remove the null entry.
- [ ] **M4. Confirm all 5 conditions exist & are configured.** Control, Abstract, Representative, SemanticAudio, SemanticVisual — each with the right density prefab (sphere vs spark), audio clip, and `respondToValue` flag. Resolve the "MismatchNonRep" naming vs the 5-condition enum.

## Phase B — End-to-end validation (desktop)
- [ ] **M5. Full desktop run-through.** Drive a session via debug keys: Walk -> Distractor -> Retrace -> Recall across all trials.
- [ ] **M6. Verify data logging.** Confirm the per-participant JSON writes with correct retrace path, recall answers, and scores.
- [ ] **M7. Validate recall questions.** Check the generated multiple-choice values/distractors are sensible and correctly scored.
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
