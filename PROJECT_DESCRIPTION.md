# Embodied-Data-VR — Project Description

> Living document. Update it whenever the project changes (new scripts, renamed
> objects, changed constants, new conditions, etc.). Companion files:
> `ROADMAP.md` (milestones/status), `HOW_TO_RUN.md` (run steps),
> `XR_MODE_SWITCHING.md` (desktop / simulator / headset).
> Last updated: 2026-08-15.

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

### What is measured (written to the per-participant JSON log)

Per trial: condition, dataset, phase durations, distractor start number, the **retrace
path** (world x/z samples over time), and the **recall questions** with the chosen
answer (→ recall score). See §6 and `Analysis/analyze_study.py`.

---

## 2. Where things are

```
Assets/
  Scenes/Test.unity            ← the study scene (only scene that matters)
  Scripts/                     ← all study code (compiles into Assembly-CSharp)
  ScriptableObjects/…          ← GraphData assets (Dataset_01..06), GraphSettings
  Samples/XR Interaction Toolkit/3.3.1/…   ← Starter Assets rig + XR Interaction Simulator
  XRI/Settings/…               ← XR Device/Interaction Simulator settings
Analysis/
  analyze_study.py             ← log → CSV analysis pipeline (§6)
  datasets.json                ← exported dataset value profiles (for retrace proxy)
ROADMAP.md, HOW_TO_RUN.md, XR_MODE_SWITCHING.md, PROJECT_DESCRIPTION.md
```

### Key GameObjects in `Test.unity`

| Object | Components | Role |
|--------|-----------|------|
| `SessionController` | `SessionController` | Orchestrates the whole session (trials, phases, logging). |
| `ConditionManager` | `ConditionManager`, `AudioModeSelector`, `VolumeController` | Activates exactly one condition; routes the walk value to it; global audio mode + volume. |
| `ExperimentController` | `ExperimentController` | Operator control surface (condition/audio/regenerate). Keyboard shortcuts **off** by default (would clash with SessionController debug keys). |
| `GraphManager` | `GraphManager` | Builds/animates the graph, flat-lays + scales it, configures `PathSampler`. |
| `Grid` | `GridGenerator` | Draws the reference grid (one cross-line per data point + baseline). |
| `WalkSystem` | `WalkValueDriver`, `PathSampler` | Reads the head position → data value → drives cues. |
| `GraphRoot` | — | Parent transform everything (line, dots, grid) is built under. |
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
   → per-trial TrialResult accumulated; on finish → WriteLog() JSON
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

### 4.6 Counterbalancing (`SessionController.BuildTrials`)
- Cyclic Latin square: for participant `p`, `startRow = (p−1) mod 5`; trial order =
  `AllConditions[(startRow + block + k) mod 5]`.
- With `trialsPerCondition = 1` each condition appears once; over any 5 consecutive
  participant IDs each condition lands in each position exactly once.
- Dataset per trial: `datasetPool[(startDs + order) mod poolCount]`, `startDs = (p−1) mod 6`.
- **Study impact:** balances condition order across participants. **Known minor issue:**
  `startRow` and `startDs` both advance by +1/participant, so condition↔dataset pairing is
  partially correlated for consecutive participants; if full decorrelation is needed,
  advance `startDs` by a different stride (a prime).

### 4.7 Retrace capture (`SessionController.Update`)
- During `Retrace`, the head's ground position `(x, z)` is sampled every
  **`retraceSampleInterval = 0.1 s`** into `retracePath`.
- **Study impact / data gap:** the log stores the path but **not** the graph geometry, so
  absolute retrace accuracy can't be recovered from the log alone. `analyze_study.py`
  gives a normalised shape-correlation *proxy* via `datasets.json`. For rigorous accuracy,
  log the per-trial value profile / graph transform in `WriteLog()`.

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
| Which participant | `SessionController.participantId` (drives counterbalancing + log name). |
| Walkable corridor size | `PathSampler.regionMargin`, `regionHalfWidth`. |
| Recall difficulty | distractor `step` factor (0.15) in `MakeNumericQuestion`. |
| Add/rename a condition | Add a `ConditionId`, add a `Condition_*` object with `CueConditionController`, add it to `ConditionManager.conditions` **in id order**, add to `AllConditions` in `SessionController`. |
| Desktop ↔ simulator ↔ headset | See `XR_MODE_SWITCHING.md`. |

---

## 6. Analysis (`Analysis/analyze_study.py`)

- Input: a folder of `participant_*.json` logs (see HOW_TO_RUN for where they land).
- Output: `trials.csv` (one row/trial: recall score & %, phase durations, retrace metrics,
  optional shape-correlation) and `conditions.csv` (per-condition mean recall % + SD).
- `datasets.json` (exported from the GraphData assets) enables the retrace shape-correlation
  proxy; regenerate it if the datasets change.

---

## 7. Known issues / decisions pending

- **M12 walk-phase UI occlusion (design decision):** the study UI panel is head-locked
  ~2 m ahead and blocks the floor graph during Walk. Needs a decision on how to present
  the walk-phase prompt (hide it, world-anchor it, controller-button to end walk, etc.).
- **Retrace accuracy data gap** (§4.7): log the value profile for rigorous scoring.
- **Enum vs object naming** for id 3/4 (§1): cosmetic mismatch.
- **Counterbalancing dataset correlation** (§4.6): optional decorrelation improvement.
- **Hardware sign-off:** M9/M10/M12 are simulator-verified; a real-headset pass remains
  (room-scale tracking feel, in-headset legibility, controller point-and-click).
