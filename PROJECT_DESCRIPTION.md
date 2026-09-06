# Embodied-Data-VR — Project Description

> Living document. Update it whenever the project changes (new scripts, renamed
> objects, changed constants, new conditions, etc.). Companion files:
> `ROADMAP.md` (milestones/status), `HOW_TO_RUN.md` (run steps),
> `XR_MODE_SWITCHING.md` (desktop / simulator / headset).
> Last updated: 2026-09-06 (M36).

---

## 1. What the study is

A within-subjects VR experiment on **embodied data physicalisation**: participants
"walk" a line graph laid flat on the floor (time on the walk axis, value as lateral
offset/height), then reconstruct it from memory. We test whether adding sensory
**cues** (a visual density field + sonified value) and whether those cues are
**semantically representative** vs generic changes how well people remember the data.

### The five conditions (`ConditionId` in `StudyTypes`/`ConditionBase.cs`)

| id | enum name        | scene object              | visual cue | audio cue | audio reacts to walk? | meaning |
|----|------------------|---------------------------|------------|-----------|-----------------------|---------|
| 0  | `Control`        | `Condition_Control`       | none       | none      | —                     | embodied baseline: graph + grid only |
| 1  | `Abstract`       | `Condition_Abstract`      | sphere (`DensityObject`) | beep | yes | generic audio + generic visual |
| 2  | `Representative` | `Condition_Representative`| spark (`SparkObject`)    | electric hum | yes | representative audio + representative visual |
| 3  | `SemanticAudio`  | `Condition_MismatchStatic`| sphere (`DensityObject`) | electric hum | **NO (static)** | "mismatch static": representative audio but it does **not** track the data |
| 4  | `SemanticVisual` | `Condition_MismatchNonRep`| spark (`SparkObject`)    | beep | yes | "mismatch non-rep": representative visual + generic audio |

> Note: the enum names for id 3/4 (`SemanticAudio`/`SemanticVisual`) are historical and
> read oddly vs the object names (`MismatchStatic`/`MismatchNonRep`). They are only
> labels — behaviour is defined by each condition's serialized fields (prefab, clip,
> `respondToValue`). Renaming the enum would be cosmetic but touches serialized data.

### The four phases per trial (`SessionPhase`)

`Walk` → `Distractor` → `Retrace` → `Recall`, repeated for every trial, then `Complete`.

- **Walk** — participant walks the graph; cues play; the graph line/dots are visible.
- **Distractor** — count backwards from a random number (300–999) in 3s (working-memory wipe).
- **Retrace** — graph hidden, cues removed (forced to `Control`); participant re-walks the
  shape from memory; their floor path is sampled.
- **Recall** — 4 multiple-choice questions about the data.

### What is measured (written incrementally to CSV — see §6)

Per trial: condition, dataset, phase durations, distractor start number
(`trials.csv`), the **retrace path** (world x/y/z samples over time,
`retracing_log.csv`), and the **recall questions** with the chosen answer
(`value_questions.csv` → recall score). See §6 and `Analysis/analyze_study.py`.
Test/pilot CSV data can be permanently cleared from the Unity Editor menu
**Tools > Study Data** (see HOW_TO_RUN §7) -- an Editor-only action, not part of
the in-VR runtime UI.

---

## 2. Where things are

```
Assets/
  Scenes/Experiment.unity      ← the study scene. Open it and press Play — SessionBootstrap
                                   applies RunSettings and stops at the Idle screen (confirm/
                                   adjust participant ID + audio mode, then Start Session) —
                                   see `autoStartSession` on `SessionBootstrap` (M34) to go
                                   back to auto-starting immediately for quick desktop debugging
  Scripts/                     ← all study code (compiles into Assembly-CSharp)
  ScriptableObjects/RunSettings.asset  ← audioMode + runType (Desktop/Simulator/VR),
                                   read by SessionBootstrap at startup
  Data/…                       ← GraphData assets (Dataset_01..06), GraphSettings
  Samples/XR Interaction Toolkit/3.3.1/…   ← Starter Assets rig + XR Interaction Simulator
  XRI/Settings/…               ← XR Device/Interaction Simulator settings
Analysis/
  analyze_study.py             ← raw CSV -> analysis CSV pipeline (§6), reads every
                                   participant subfolder under StudyData/
  analysis_gui.py               ← point-and-click front end for analyze_study.py (§6, M35):
                                   pick a folder, see the participants in it, Analyze All /
                                   Analyze Selected
  datasets.json                 ← exported dataset value profiles (for retrace proxy);
                                   analyze_study.py defaults to this file automatically (M35)
ROADMAP.md, HOW_TO_RUN.md, XR_MODE_SWITCHING.md, PROJECT_DESCRIPTION.md
```

### Key GameObjects in `Experiment.unity`

| Object | Components | Role |
|--------|-----------|------|
| `SessionController` | `SessionController` | Orchestrates the whole session (trials, phases, logging). |
| `ConditionManager` | `ConditionManager`, `AudioModeSelector`, `VolumeController` | Activates exactly one condition; routes the walk value to it; global audio mode + volume. |
| `ExperimentController` | `ExperimentController` | Operator control surface (condition/audio/regenerate). Keyboard shortcuts **off** by default (would clash with SessionController debug keys). |
| `GraphManager` | `GraphManager` | Builds/animates the graph, flat-lays + scales it, configures `PathSampler`. |
| `Grid` | `GridGenerator` | Draws the reference grid (one cross-line per data point + baseline). |
| `WalkSystem` | `WalkValueDriver`, `PathSampler` | Reads the head position → data value → drives cues. |
| `GraphRoot` | — | Parent transform everything (line, dots, grid) is built under. |
| `Bootstrap` | `SessionBootstrap` | Runs first (`DefaultExecutionOrder(-100)`). Reads `RunSettings`, activates exactly one player rig, sets `PlayerRig.Head`, applies the audio mode, and (only if `autoStartSession` is checked -- off by default since M34) starts the session immediately; otherwise it stops at Idle, same as returning there after a session completes. |
| `Condition_*` (5) | `ControlCondition` **or** `CueConditionController` (+ `DensityController`, `CueAudioController`) | One cue-set per condition. |
| `ExperimentArea` | `ExperimentArea` | Defines the room centre + walk-forward axis + random-point field. |
| `SessionUI` | `SessionUI` | Self-building world-space study UI (phase prompts + buttons). |
| `Camera` | `Camera`, `DesktopWalker`, `AudioListener` | Desktop player. Disabled in VR mode. |
| `XR Origin (XR Rig)` | full XRI Starter-Assets rig | VR player (head + controllers + interactors). Enabled in VR mode. |
| `XR Origin (VR)` | minimal XROrigin (head only) | Spare/head-only rig; kept disabled. |

---

## 3. What controls what (data flow)

### Walk-value pipeline (drives the cues)
```
head position (DesktopWalker cam OR XR head)
   → PathSampler.NormalizedValueAt(pos)      // 0..1 across dataset min..max
   → WalkValueDriver (every frame)           // also gates cues to the walk region
   → ConditionManager.SetNormalizedValue()   // forwards to the active condition
   → CueConditionController.Apply(v)
        → DensityController.UpdateDensity(v)  // visual field density
        → CueAudioController.SetNormalizedValue(v)  // pitch/tempo
```
`SessionController` and `WalkValueDriver` **auto-resolve** which transform is "the head":
the XR head camera when an XR device is active (real headset) or when the desktop camera
is disabled (simulator); otherwise the `DesktopWalker`. So the same scene runs in all
three modes with no rewiring (see `XR_MODE_SWITCHING.md`).

### Session pipeline
```
SessionController.StartSession()
   → BuildTrials()                 // counterbalanced condition×dataset list (§5)
   → NextTrial(): GraphManager.SetData + GenerateGraph + ConditionManager.SetCondition
   → phases advance via UI buttons OR debug keys (Enter / 1–4)
   → CSV rows appended as they happen: retracing_log.csv (end of Retrace),
     value_questions.csv (each answered question), trials.csv (last question answered)
```

### Audio mode (session-wide)
`AudioModeSelector` (on `ConditionManager`) pushes the chosen mode
(Pitch-only / Tempo-only / Both) to **every** `CueAudioController`, including inactive
ones, so the choice is consistent across all conditions and persists across switches.
Selectable at runtime from the `SessionUI` start screen.

---

## 4. Math & calculations (with study impact)

All constants below are the current serialized/asset values. **Bold** = most likely to
affect study results.

### 4.1 Graph geometry (`GraphManager`, `GraphSettings`)
- Local point position: `localPos = (i * spacing, value * heightScale, 0)`
  with **`spacing = 0.5`**, **`heightScale = 0.2`**.
- After building vertical, the graph is flat-laid (rotate to the area's forward axis,
  then +90° about X so it lies on the floor).
- Uniform resize so the walk spans **`targetWalkLength = 3` m**:
  `s = targetWalkLength / walkExtentLocal`, applied about `GraphRoot`, then re-centred on
  `ExperimentArea.Center`. Uniform scale preserves shape, so the value mapping is
  unchanged. `scaleTweenDuration = 0.6 s`.
- **Study impact:** `targetWalkLength` sets how far participants physically walk;
  `heightScale`/`spacing` set the visual aspect ratio of the (flat) shape. Changing
  `spacing`/`heightScale` changes how "steep" the curve looks and the grid density.

### 4.2 Value sampling (`PathSampler`)
- Progress along path: `d = dot(worldPos − start, dir)`.
- Continuous raw value: `t = d / segment; RawValueAt = lerp(values[i], values[i+1], t−i)`
  (`segment` = world distance between adjacent scaled points).
- Normalised: `NormalizedValueAt = InverseLerp(min, max, RawValueAt)` → **0..1 per dataset**.
- On-walk region gate: within `±regionMargin` (**0.5 m**) of the ends along the path AND
  within `regionHalfWidth` (**2 m**) laterally. Outside → cues gated off (silent, no density).
- **Study impact:** normalisation is **per-dataset**, so cue intensity always spans the
  full 0..1 range regardless of a dataset's absolute values — cross-condition cue ranges
  are comparable. `regionHalfWidth`/`regionMargin` define the walkable corridor.

### 4.3 Visual density (`DensityController`)
- `count = round(lerp(minCount, maxCount, norm))` with **`minCount = 5`, `maxCount = 100`**.
- Objects scattered in `ExperimentArea.RandomPoint()`, random size in `[0.1, 0.3]`.
- **Study impact:** the density→value gain (5..100) is identical across the 4 cue
  conditions, so the *visual* cue strength is matched; only the **prefab** (sphere vs
  spark) differs by condition.

### 4.4 Audio sonification (`CueAudioController`)
- **Pitch** = `lerp(minPitch, maxPitch, norm)` = `lerp(0.8, 1.5, norm)`, smoothed toward
  target at `smoothSpeed = 5` (exponential, ~0.2 s time-constant).
- **Tempo** = pulses/sec `pps = lerp(minPPS, maxPPS, norm)` = `lerp(1, 8, norm)`;
  pulse interval `= 1 / pps`; each pulse restarts the clip (non-overlapping).
- **Modes** (`AudioMode`, set session-wide): `PitchOnly` (continuous loop, pitch varies),
  `TempoOnly` (pulses, pitch held at 1), `Both` (pulses whose rate + pitch vary).
- **Static** (`respondToValue = false`, only `Condition_MismatchStatic`): steady hum at
  pitch ≈ 1, ignores the walk — this is the deliberate audio↔data mismatch.
- **Study impact:** pitch/tempo ranges are matched across reacting conditions, so audio
  cue *intensity* is comparable; the manipulations are the **clip** (beep vs electric) and
  whether it reacts (`respondToValue`). Changing the ranges changes cue salience for all.

### 4.5 Recall questions (`SessionController.MakeNumericQuestion`)
- Four numeric answers: highest, lowest, last value, (max−min) difference.
- Distractors: offsets of `±k · step` where **`step = max(1, round(range · 0.15))`**
  (`range` = dataset max−min); 3 distinct, non-negative options by display value.
- **Correct-answer slot is randomised** with a reproducible seed
  `participantId ^ (orderIndex+1) ^ (qIndex+1)` → unpredictable to the participant but
  deterministic for analysis. (Previously it was a fixed diagonal — fixed in M7.)
- **Study impact:** removes positional-guessing confound; distractors scale with each
  dataset's range so difficulty is comparable across datasets.

### 4.6 Condition order & counterbalancing (`SessionController.BuildTrials`)
- Condition order is chosen by **`RunSettings.conditionOrderMode`** (M29), one of:
  - `Sequential` — the same fixed order every session: Control, Abstract, Representative,
    SemanticAudio, SemanticVisual. Identical for every participant — **provides no
    order-effect counterbalancing on its own**.
  - `Random` — all 5 conditions (Control included) shuffled into a fully random order.
  - `RandomExceptControlFirst` — Control is forced into position 0; the remaining 4 are
    shuffled after it, so every participant sees Control first.
  The two random modes are seeded from `participantId` (and the block index, if
  `trialsPerCondition > 1`) via `BuildConditionOrder`/`ConditionOrderSeed` — reproducible
  per participant, not dependent on Unity's global random state, same pattern as the
  recall answer-slot seed in §4.5.
- Dataset per trial is independent of condition order: `datasetPool[(startDs + order) mod
  poolCount]`, `startDs = (p−1) mod 6`, where `order` is just the flat 0-based trial index
  — whichever condition `BuildConditionOrder` places at that index gets paired with it.
- **Study impact:** `Sequential` is simplest but the whole cohort sees the same order
  (a real design decision — pick this only if you have another reason to trust internal
  validity, e.g. a short pilot). `RandomExceptControlFirst` establishes Control as a
  consistent baseline/acclimation trial while still varying the other 4. Neither random
  mode is a formal Latin square, so with a small cohort, order could by chance skew
  toward one condition landing later more often than a true Latin square would prevent
  — for guaranteed per-position balance across participants, a Latin-square rotation
  (as used before M29) would need to be reintroduced as an additional mode.
- **Known minor issue (pre-existing, still applies to dataset assignment):** `startDs`
  advances by a fixed +1/participant, so dataset assignment is the same sequence
  regardless of condition order; if full decorrelation from `participantId` is needed,
  advance `startDs` by a different stride (a prime).

### 4.7 Retrace capture (`SessionController.Update`)
- **When it happens:** once per trial, between `Distractor` and `Recall` — the phase
  order per trial is always `Walk -> Distractor -> Retrace -> Recall`. Retrace is on
  that same trial's own dataset/graph: the line/dots are hidden and the condition is
  forced to `Control` (no cues) so it's tested purely from memory, then the participant
  walks the same physical corridor again trying to reproduce the shape they just walked
  in `Walk`. It ends automatically (`CheckAutoEndOfPhase`) once they walk from the start
  of the corridor to the far end again — same auto-end mechanism as `Walk` (M27).
  Since nothing else tells the participant to walk back to the start first, `SessionUI`
  now shows that instruction on screen for a few seconds at the start of Retrace before
  hiding itself again (M36) — see §7's UI note below.
- **M33 bug (fixed):** that auto-end check originally only tested "has the head passed
  the far end," which could be true from the *previous* phase's end position (nothing
  resets the participant physically between phases) — so Retrace could auto-complete
  almost instantly, before the participant ever walked anywhere. Confirmed against real
  session data: `retrace_seconds` was stuck at exactly the grace period (~0.5s) for every
  trial across two full sessions, with `retracing_log.csv` showing 5-6 samples per trial
  and a frozen head position. Fixed by requiring the participant be back near the start
  at some point during the *current* Retrace before the far-end check can complete it —
  see M33 in ROADMAP.md.
- During `Retrace`, the head's ground position `(x, z)` is sampled every
  **`retraceSampleInterval = 0.1 s`** into `retracePath`.
- **Study impact / data gap:** `retracing_log.csv` stores the path but **not** the graph
  geometry, so absolute retrace accuracy can't be recovered from the log alone.
  `analyze_study.py` gives a normalised shape-correlation *proxy* via `datasets.json`
  (`retrace_shape_r` per trial in `analysis_trials.csv`), and (M32) a PNG plot per trial
  under `<out_dir>/plots/` showing the original data's profile against the participant's
  actual retrace, titled with that same correlation value. For rigorous accuracy, log the
  per-trial value profile / graph transform in `SessionController`'s CSV-writing methods.

---

## 5. "I want to change X" — quick guide

| Goal | Where |
|------|-------|
| Different data | Edit/replace the `Dataset_01..06` GraphData assets, or change `SessionController.datasetPool`. Re-export `Analysis/datasets.json` (see HOW_TO_RUN). |
| Longer/shorter physical walk | `GraphManager.targetWalkLength` (metres). |
| More/less visual density | `DensityController.minCount/maxCount` (per condition, but keep them equal across conditions to stay matched). |
| Audio pitch/tempo salience | `CueAudioController.minPitch/maxPitch`, `minPulsesPerSecond/maxPulsesPerSecond`. |
| Default audio mode | `AudioModeSelector.mode` (or pick at runtime on the start screen). |
| 2 trials per condition | `SessionController.trialsPerCondition = 2`. |
| Which participant | `SessionController.participantId` (drives condition-order/dataset seeding + the `participant_id` column, formatted `p01`/`p02`/…); auto-suggested at startup and adjustable on the Idle screen (M28), and re-suggested automatically when returning from Complete to Idle for the next participant (M31, via `ReturnToIdle()` / the Complete screen's "Next Participant" button). |
| Condition order (Sequential/Random/Random-except-Control-first) | `RunSettings.conditionOrderMode` (§4.6, M29). |
| Walkable corridor size | `PathSampler.regionMargin`, `regionHalfWidth`. |
| Recall difficulty | distractor `step` factor (0.15) in `MakeNumericQuestion`. |
| Add/rename a condition | Add a `ConditionId`, add a `Condition_*` object with `CueConditionController`, add it to `ConditionManager.conditions` **in id order**, add to `AllConditions` in `SessionController`. |
| Desktop ↔ simulator ↔ headset | See `XR_MODE_SWITCHING.md`. |
| Skip the Idle confirm screen (auto-start like before M34) | Check `autoStartSession` on the `Bootstrap` GameObject's `SessionBootstrap` component. |
| Run analysis without the command line | `python Analysis/analysis_gui.py` (M35) — see §6. |

---

## 6. Analysis (`Analysis/analyze_study.py`, `Analysis/analysis_gui.py`)

- Input: the `StudyData` folder itself — each participant's own subfolder
  (`StudyData/p01/trials.csv`, `StudyData/p02/trials.csv`, ... — see M28) is read and
  concatenated; a flat `StudyData/trials.csv` etc. is also read directly if present,
  for backward compatibility with data collected before per-participant folders
  existed. No JSON parsing anymore.
- Two ways to run it (M35): the CLI (`python analyze_study.py <StudyData folder>`,
  optionally `--participant p03` for just one), or the GUI (`python analysis_gui.py`) —
  pick the folder, see every participant it finds, then **Analyze All** or select one
  and **Analyze Selected Participant**. Both call the same `run_analysis()` function in
  `analyze_study.py`, so they can never drift apart / give different numbers.
- Output (default `StudyData/analysis/` for all participants, `StudyData/analysis/<id>/`
  for a single-participant run — so re-running one participant never overwrites the
  all-participants output — overridable with `--out` / "Choose Output..."):
  `analysis_trials.csv` (every raw trial column plus recall %, retrace path metrics,
  optional shape-correlation), `analysis_conditions.csv` (per-condition mean recall % +
  SD), `analysis_participant_order.csv` (M30, which condition order each participant
  saw), and (M35) `summary.csv` — the same headline numbers printed to the console,
  saved as a plain one-column CSV so a run's result is a file you can open, not just a
  terminal printout.
- `datasets.json` (exported from the GraphData assets) enables the retrace shape-correlation
  proxy and the PNG plots; regenerate it if the datasets change. Since M35 it's picked up
  automatically when it sits next to the script — `--datasets` only needed to point
  elsewhere.
- Not implemented: the data notes doc's 5th file, `questionnaires.csv` (TLX etc.) — no
  questionnaire UI exists in the project yet.
- (M32) Optional per-trial retrace-vs-original PNG plots under `<out_dir>/plots/`,
  generated automatically when a datasets file is available and matplotlib is installed
  (`--no-plots` to skip). Same resampled/normalized basis as `retrace_shape_r`.

---

## 7. Known issues / decisions pending

- ~~**M12 walk-phase UI occlusion**~~ RESOLVED (M27): the panel is now hidden entirely
  during Walk and Retrace (`SessionUI.Render`), and those two phases auto-end when the
  participant walks past the far end of the graph corridor (`SessionController.
  CheckAutoEndOfPhase`) instead of waiting for a "Done" button. The panel is visible
  only for Idle / Distractor / Recall / Complete, i.e. only when a real interaction
  (button, MCQ, text entry) is expected. See M27 in ROADMAP.md.
- ~~**M27 false-completion risk**~~ RESOLVED (M33): Walk/Retrace auto-end now requires
  a genuine start-to-end pass (see §4.7) instead of just "currently past the far end,"
  which could be left over from a previous phase.
- ~~**Idle screen skipped on first Play**~~ RESOLVED (M34): `SessionBootstrap.
  autoStartSession` now defaults off, so every fresh Play stops at Idle for the
  researcher to confirm the participant ID/audio mode, same as after a session
  completes.
- ~~**No instruction to walk back to start before Retrace**~~ RESOLVED (M36): `SessionUI`
  briefly shows "Walk back to the start, then retrace the shape you just walked --
  from memory." at the start of Retrace, then hides as before. Confirmed (not a bug):
  no cue feedback is active during Retrace regardless of the participant's position --
  `EndDistractor()` forces the `Control` condition first, and `Control`'s cues are
  no-ops.
- ~~**Feedback could start before the graph visually settled**~~ RESOLVED (M36): found
  and fixed two compounding bugs in `GraphManager` -- `IsGenerating` was clearing ~2.2s
  before the align/resize tween actually finished, and `PathSampler` was never reset
  between trials, so it stayed "ready" with the previous trial's stale geometry through
  that whole window. `WalkValueDriver`'s cue gate (`PathSampler.IsReady`) could therefore
  fire against stale data while the new graph was still visibly flipping/resizing.
  `PathSampler.Clear()` now runs at the start of every generation, and `IsGenerating`
  only clears once the sampler is actually reconfigured against the final geometry.
- **Retrace accuracy data gap** (§4.7): log the value profile for rigorous scoring.
- **Enum vs object naming** for id 3/4 (§1): cosmetic mismatch.
- **Counterbalancing dataset correlation** (§4.6): optional decorrelation improvement.
- **Hardware sign-off:** M9/M10/M12 are simulator-verified; a real-headset pass remains
  (room-scale tracking feel, in-headset legibility, controller point-and-click).
