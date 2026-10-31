# Embodied-Data-VR — Project Description

> Living document describing the system **as it is now**. Development history (why
> things are the way they are, bugs found and fixed) is in `ROADMAP.md`, milestone by
> milestone (Mxx numbers below point there). Companion files: `HOW_TO_RUN.md` (running
> sessions, building for the Quest, copying data), `XR_MODE_SWITCHING.md` (desktop vs
> headset), `GRID_SETUP.md` (floor grid).
> Last updated: 2026-10-31 (through M69; default 3 trials per condition, extra
> session_info columns).

Study title used with participants: **"Audio and visual cues for remembering data in
virtual reality"** (Tampere University MSc thesis). Unity 6000.3.10f1, URP, OpenXR,
runs standalone on a **Meta Quest 2** (Android build, M63); the same scene also runs on
the desktop for development.

---

## 1. What the study is

A within-subjects VR experiment on **embodied data**: participants physically walk a
line graph laid flat on the floor (time = the walk direction, value = sideways position
on the floor), then walk the shape again from memory and answer questions about the
values. The independent variable is the **cue condition**: whether sound and objects
around the participant change with the value under their feet, and whether those cues
are **semantically related to the data** (electricity: an electric zap sound, yellow
spark objects) or **generic** (a beep, plain spheres).

### 1.1 Conditions (`ConditionId`, `ConditionBase.cs`; scene objects `Condition_*`)

| id | name | visual cue | audio cue | audio_cue / visual_cue (CSV) | supervisor code |
|----|------|-----------|-----------|------------------------------|-----------------|
| 0 | `Control` | none | none | none / none | c5 |
| 1 | `Abstract` | plain spheres | beep | plain / plain | c2 |
| 2 | `Representative` | yellow sparks | electric zap | semantic / semantic | c1 |
| 3 | `SemanticAudio` | plain spheres | electric zap | semantic / plain | c3 |
| 4 | `SemanticVisual` | yellow sparks | beep | plain / semantic | c4 |
| 5 | `Tutorial` | blue electric orbs | earlier electric sound | — | practice only |

Conditions 1–4 form a 2×2 (audio semantic/plain × visual semantic/plain) plus the
Control baseline (M47). All four cue conditions follow the data in the same way; only
the sound clip and the object look differ. `Tutorial` is used only in the second
practice trial and never appears in the experiment order (M60).

### 1.2 The data

- 30 real datasets from the UCI "Individual household electric power consumption" data
  (daily mean energy use, kWh; days with more than 60 min missing removed), selected by
  the supervisor: `Sources/selected_30_datasets.csv`, 30 days each, dataset ids 1–43
  with gaps. Imported as `Assets/Data/UCI/Energy_dNN.asset` (NN = CSV id), point labels
  "Day 1" … "Day 30", unit kWh (M60).
- **Tutorial:** Energy_d43 (practice 1, Control) and Energy_d37 (practice 2, Tutorial
  cues), the same for every participant.
- **Experiment sets (M69):** 25 of the other 28 datasets, grouped into **5 fixed sets of
  5** (`RunSettings.datasetSets`), matched on mean, SD, range, maximum, minimum, end
  value, mean day-to-day change and number of turns (set means differ by at most
  ~1 kWh on each). With 3 trials per condition a participant walks 3 of the 5 datasets
  in each set (15 in all); with 5 trials all 25. Not used: d04 (unusually wide
  range), d06 and d19 (unusually low values / flat). The full 28 stay in
  `SessionController.datasetPool` for the old rotation mode.

---

## 2. Session flow (what a participant goes through)

Before the headset: the participant reads the **information sheet** and **privacy
notice** on paper (`../Privacy Notice/`). Then, in VR:

1. **Start screen (researcher)** — participant number (auto-suggested, − / +; red
   warning if that number already has data), **Settings**, **Start Session**, and a
   one-line summary of the active settings. Start Session re-anchors the experiment
   area to where the participant is standing and facing (§3.1).
2. **Consent** — "Consent to take part in the study", the study name, five statements
   (read the sheet and notice, 18+, voluntary, data labelled only with a code and not
   removable afterwards, use in thesis/publications/later research), a tick box
   "I agree to take part in this study." and **Continue**. Unticked → "I do not
   consent", a thank-you message, back to the start screen (M61).
3. **Background questions** ("About you") — VR experience (at least every day / once a
   week / once a month / once a year / once in the last 5 years / never) and age range
   (18–24, 25–29, …, 50–54, 55–60).
4. **Welcome / instructions** screen (text on `RunSettings.instructionsText`) →
   "Start the tutorial".
5. **Tutorial: 2 practice trials** (full trial flow, not scored, no questionnaire):
   Practice 1/2 = Control on d43, Practice 2/2 = Tutorial cues on d37. Then
   "Tutorial complete — You have now completed the tutorial. The experiment starts
   next." (M60).
6. **Experiment: 15 trials in 5 blocks** — one block per condition, 3 trials each, back
   to back (`trialsPerCondition = 3` in `RunSettings.asset` since 2026-10-31; it was 5,
   M59/M61, i.e. 25 trials). Block order from a balanced Latin square (§5).
   - After **every** trial: recall questions (§2.2).
   - After the **last trial of each condition** (3rd by default): the post-condition questionnaire
     (intro screen "About this part of the study", then 16 items, §2.3).
7. **End screen** — "Thank you! You can now remove the headset. The researcher will
   give you a laptop for four short open-ended questions." The four open-ended
   questions are a Microsoft Form, outside the app.

### 2.1 One trial (`SessionPhase`)

`Walk → Distractor → Retrace → RetraceReview → Recall → (Questionnaire) → TrialComplete`

| Phase | What happens | Panel |
|-------|--------------|-------|
| **Walk** | The graph (dots, line, value labels, grid) and the green Start / red End floor markers are shown. Cues and movement logging start once the participant stands on the **Start marker**; the phase ends when they reach the **End marker**. | hidden |
| **Distractor** | "Count out loud backwards from *N* in steps of 3, until told to stop." (*N* random, 300–998); researcher/participant presses Done. Cues off. | shown |
| **Retrace** | Graph line, dots and labels hidden (grid and markers stay). A short instruction says to walk back to the green Start marker and walk the shape again from memory. Logging and the trial's cues re-arm on the Start marker; ends at the End marker. Cues stay on as in Walk (M53) and follow where the participant stands, so they add nothing the participant isn't already doing. | brief instruction, then hidden |
| **RetraceReview** | The path just walked is drawn on the floor as a dot+line graph in the same style (same point count) for 5 s, then cleared (M44; no panel since M59). | hidden |
| **Recall** | Four questions (§2.2), one per screen. Not skippable. | shown |
| **Questionnaire** | Only after a condition's last trial (§2.3). | shown |
| **TrialComplete** | "Walk back to the Start marker"; once the participant is on it, a **Start Next Trial** button appears. The next graph is only built after that (M39). | shown |

Cues run **only** in Walk and Retrace (`SessionController.SetPhase` →
`WalkValueDriver.SetActive`), and only after the participant has stood on the Start
marker in that phase (M56), so walking back to the start is neither cued nor logged.

### 2.2 Recall questions (`SessionController.BuildQuestions`)

1. "What was the highest value you encountered?" (`max_value`)
2. "What was the lowest value you encountered?" (`min_value`)
3. "What was the value at the end of the walk?" (`end_value`)
4. "What was the difference between the highest and lowest points?" (`range_value`)

- **Answer mode** (setting, default **number pad**): typed on an on-panel keypad (0–9,
  ".", Delete; M58). Logged with `abs_error` = |answer − correct| and
  `error_pct_of_range` = abs_error / (dataset max − min) × 100 (M62). `is_correct` =
  equal at one decimal.
- Alternative mode: **4 choices** — the correct value plus three distractors at
  ±k·step, step = max(1, round(0.15 × range)), all ≥ 0; the correct slot is random but
  reproducible (seeded by participant, trial and question).

### 2.3 Post-condition questionnaire (`PostConditionQuestionnaire.asset`, M58)

One item per screen; Back / Next / Finish; Next is disabled until answered.

| Items | Instrument | Scale |
|-------|-----------|-------|
| SPES-SL1–SL4 | Spatial Presence Experience Scale, self-location | 1–5, "I do not agree at all" … "I fully agree" |
| SPES-PA1, PA2, PA5, PA8 | SPES, possible actions ("dots" wording) | 1–5 |
| CUE-V "I noticed the surroundings changing as I walked." | cue check | 1–5 |
| CUE-A "I noticed the sound changing as I walked." | cue check | 1–5 |
| TLX-1–6 (Mental, Physical, Temporal, Performance, Effort, Frustration) | NASA Raw TLX | 0–100 in steps of 5 (21 segments), "Very low" … "Very high"; Performance "Perfect" … "Failure" |

Intro screen text: "You have finished this part of the study. Please answer the
following questions about the walks you just completed. Point at your answer and press
the trigger."

---

## 3. The virtual setup

### 3.1 Experiment area and floor layout (`ExperimentArea`)

Along the walk: **Start marker | gap | graph zone | gap | End marker** (M56).

- Graph zone: width = **area width** (default 4 m), depth = **walk length + 1 m**
  (default 3 + 1 = 4 m).
- Gaps: **0.2 m** (marker gap setting; 0.4 m before 2026-10-27) — silent transition buffers, outside the cue
  region.
- Start / End markers: green and red rectangles with a "Start"/"End" label lying on
  the floor, as wide as the area and **0.6 m** deep (`EndpointMarkers.cs`).
- Total footprint along the walk with defaults: 4 + 2 × (0.2 + 0.6) = **5.6 m**.
- On **Start Session** the area is re-anchored to the participant (M51): its walk
  direction = the way they face (yaw only), positioned so they stand in the middle of
  the Start marker. Everything (graph, grid, markers, cue field) is laid out from it.
  The researcher menu can re-centre it between trials.
- When the walk length / width / gap are changed in Settings, the Start spot stays
  where it is and the area grows or shrinks forward from it; the Start/End markers for
  the new layout are shown on the floor immediately (M68).

### 3.2 The graph (`GraphManager`, `GraphSettings`, `DataPoint`, `GridGenerator`)

- Built directly in its final pose lying on the floor, in one frame (no build
  animation; M53).
- Point *i* is placed at local (i × spacing, value × heightScale) with spacing 0.5 and
  heightScale 0.2, then the whole graph is scaled uniformly so the **first-to-last point
  distance equals the walk length** (default **3 m**; 30 points → points 10.3 cm apart)
  and centred on the area (centre of the value range on the area's centre line).
- **Sideways fit (M68):** if the data's value range would come out wider than the area
  width minus 0.4 m each side, the value axis is narrowed to fit. With the default 3 m
  walk and 4 m area the 28+2 datasets spread 0.75–2.6 m sideways, so none is narrowed;
  the widest dataset starts being narrowed above a ≈3.7 m walk (because the uniform
  scale widens the graph along with the walk).
- **Value labels (M67):** each point shows "Day *n* : *value* kWh" in bold white on a
  solid dark plate, 2 cm above the floor and drawn on top of the line. Label height is
  set in world units from the point spacing: 0.7 × spacing, clamped to 5–12 cm (≈7 cm on
  a 3 m walk). Each label sits beside its dot on the side where the line is least in
  the way (outside peaks and dips).
- **Grid:** one grey cross line per point across the data's value range (plus padding)
  and a baseline at value 0 (`GridGenerator`). The grid stays visible in Retrace as a
  pacing guide.

### 3.3 From position to cue value (`PathSampler`, `WalkValueDriver`)

```
head position (XR camera on the Quest, desktop camera in the editor)
  → PathSampler.NormalizedPositionValueAt(pos)   // where the participant STANDS on the value axis
  → WalkValueDriver (every frame; gates cues to the walk region)
  → ConditionManager → active condition
       → DensityController.UpdateDensity(v)      // number of objects
       → CueAudioController.SetNormalizedValue(v) // pulse rate
```

- **Standing-position feedback (M46):** the value axis is the floor direction from the
  lowest-value dot to the highest-value dot (walk component removed); the cue value is
  where the participant stands along it, from 0 (on the minimum line) to 1 (on the
  maximum line), clamped. Head height is ignored. Standing on a dot gives exactly that
  dot's value.
- **Per-dataset normalisation:** 0..1 always spans that dataset's own min–max, so cue
  intensity covers the full range in every trial.
- **Region gate:** cues are on only inside the graph zone along the walk (± 0.5 m past
  the first/last point) and within the corridor sideways (half-width = max(area width/2,
  half the value band); M68 ties it to the area width). Outside → silent, no objects.

### 3.4 Visual cue (`DensityController`, shared `GraphSettings` "Cue Field")

- Number of objects = round(lerp(**5, 100**, v)); random size 0.1–0.3 m; objects scale
  in/out (0.3 s) rather than pop, float gently, no shadows, no colliders.
- Spread in a 3D field: area footprint + **1.5 m** padding on each side, height
  **0.3–2.8 m**; inside the walk zone objects only appear above **2.1 m**, so nothing
  floats at body or face height on the walk (M48).
- Looks: **plain** = soft pearl sphere (`SoftOrb`, no glow or colour temperature);
  **semantic** = yellow spark, variant chosen in settings — default **C "star + glow"**
  (pointy star mesh with a warm glow and spark rays; A = pointy star only, B = yellow
  ball with streaks; M54/M55). Tutorial = blue electric orb (`ElectricOrb`).
- Same count range and size for every cue condition; only the object look differs.

### 3.5 Audio cue (`CueAudioController`, `AudioModeSelector`)

- **Study setting: tempo only** (M57): pulses per second = lerp(**1, 8**, v); one pulse
  every 1/pps s; every pulse plays the clip unchanged (playback speed 1.00 — tempo never
  changes pitch). Measured rate within 0.4 % of the set rate.
- Clips: semantic = Freesound #512471 "Electric zap" (CC0) cut to a 110 ms pulse
  (`spark_zap_pulse.wav`); plain = `beep_matched.wav`; tutorial =
  `electric3_matched.wav`. Loudness-matched (−10 LUFS) so neither sound is louder
  (M53).
- Click-free playback: voice pool, per-sample fade ramps (8 ms), baked fades.
- Other modes (settings): pitch only (continuous loop, pitch 0.8–1.5) and both.

### 3.6 Panel and interaction (`SessionUI`)

- World-space panel **2.7 m** in front of the participant (yaw only), re-centred when
  they look more than 55° away for 0.8 s. Hidden during Walk, Retrace and
  RetraceReview.
- Point with the controller ray and pull the trigger. Answer buttons are blue;
  call-to-action buttons (Continue, Next, Start Next Trial, …) green; Back/Cancel grey;
  destructive red (M66).
- No controller locomotion: the participant can only move by walking (M49).

---

## 4. Researcher controls and settings

### 4.1 Researcher menu (M66)

Hold the **left controller's menu button for 1.5 s** (Esc in the editor) at any time:
participant / phase / trial, Continue, **Skip this step** (Walk, counting, Retrace,
review, instructions, between trials — not recall or questionnaires), **Re-centre area
here** (between trials), **End session** (confirm; data so far is kept; back to the
start screen with the next number), Settings (no session running), Close app. A red
**End session** button is also pinned to the panel's top-right corner whenever the
panel is visible during a session. Every such action is logged in
`session_events.csv`.

### 4.2 In-app settings (M64; stored on the device, applied at launch)

| Setting | Default | Range |
|---------|---------|-------|
| Walk length | 3 m | 2–15 m (area depth follows: walk + 1 m) |
| Area width | 4 m | 2–10 m |
| Marker gap | 0.2 m | 0.1–1.5 m |
| Trials per condition | 3 | 1–10 |
| Condition order | Latin square | Fixed / Random / Control first / Latin square |
| Recall answers | number pad | number pad / 4 choices |
| Audio cue | tempo only | pitch / tempo / both |
| Spark look | C: star + glow | A / B / C |
| Tutorial | on | on / off |
| Dataset order | Latin-square sets | Latin-square sets / rotate |

Plus **Study data**: participant and trial counts on the device and **Delete all
data…** (red confirm screen; copy the data to the PC first). Defaults come from
`RunSettings.asset` and the scene; the app works on a runtime copy, so the asset is
never changed. Every value actually used is written to `session_info.csv`.

---

## 5. Order and counterbalancing (`SessionController.BuildTrials`)

- **Condition order:** balanced (Williams) **Latin square**, 10 rows for 5 conditions,
  row = (participant − 1) mod 10. Over every 10 participants each condition appears
  twice in each position and each condition follows every other exactly twice (M47).
  One order per participant; trials are **blocked** (all trials of a condition back
  to back, M59).
- **Datasets — Latin square too (M69, supervisor 2026-10-27):** condition *i* (in the
  order Control, Abstract, Representative, SemanticAudio, SemanticVisual) gets set
  S((i + p − 1) mod 5 + 1) for participant *p*, and within the set the five datasets
  start at position (p − 1) mod 5. Result, verified for participants 1–10: over every
  5 participants each set meets each condition once and each dataset takes each trial
  slot (1st–5th in its block) once (with 5 trials per condition); with the 10-row
  condition square, over every 10 participants each set is also in each block position
  twice. With 3 trials per condition (default since 2026-10-31) each participant gets 3
  consecutive datasets of the set; over every 5 participants each dataset is used 3
  times, once in each of the slots 1st–3rd.

  | Set | Datasets |
  |-----|----------|
  | S1 | d05, d10, d11, d24, d25 |
  | S2 | d01, d09, d14, d28, d34 |
  | S3 | d13, d18, d22, d23, d26 |
  | S4 | d03, d08, d15, d35, d36 |
  | S5 | d02, d07, d12, d16, d33 |

  Setting "Dataset order" = *Rotate* restores the previous pool rotation (pairing not
  balanced).
- The tutorial trials always come first and are excluded from the order.

---

## 6. Data recorded

Written incrementally as CSV to `<persistentDataPath>/StudyData/pNN/` (one folder per
participant; on the Quest `/sdcard/Android/data/com.sakibhasan.embodieddataviz/files/StudyData`).
Every row carries `session_id` (participant + local start time) so a reused number can
never silently mix two runs (M62). Positions are in the **area frame**: x = sideways,
z = along the walk, relative to the area centre; y = height.

| File | One row per | Main columns |
|------|-------------|--------------|
| `session_info.csv` | session | run type, headset, Unity version, condition order mode + Latin-square row + order, trials per condition, practice/experiment counts, dataset pool size + order, audio mode, recall mode, spark variant, area width/depth, marker gap, sample interval, walk length, dataset order mode, condition→set (e.g. "Control=S1 Abstract=S2 …"), app_version, device_model, tutorial_enabled (last three added 2026-10-31) |
| `consent.csv` | consent answer | consent_version, response ("I consent" / "I do not consent"), timestamp, local time |
| `background.csv` | background question | item_id, item_text, response (option text) |
| `trials.csv` | trial | condition, audio_cue, visual_cue, presentation_order, trial_in_condition, dataset_id, start/end time, walk/distractor/retrace seconds, distractor start number, recall_score/total, walk/retrace sample counts, dataset_set (S1–S5) |
| `value_questions.csv` | recall question | question type/text, correct answer, response, abs_error, error_pct_of_range, is_correct, answer_mode |
| `walking_log.csv` | movement sample in Walk (10 Hz, from the Start marker) | t_seconds, head_x/y/z, head_yaw_deg (relative to the walk direction), head_pitch_deg, cues_on, cue_value |
| `retracing_log.csv` | movement sample in Retrace (same format) | as above |
| `graph_points.csv` | graph point per trial | dot_index, day_label, value, x, z — the ground truth the retrace is compared with |
| `questionnaires.csv` | questionnaire item | instrument, subscale, item_id, item_text, scale, response |
| `session_events.csv` | researcher/session event | event (session_started, skip_step, recentre_area, session_ended_early, session_complete), phase, detail |

Notes for analysis (from the editor check of p01, 2026-10-31):

- **Saved settings override the defaults.** Values changed in the in-app Settings are
  stored on the device and used at every launch, so a device can run with values other
  than `RunSettings.asset`. `session_info.csv` records the values actually used; check
  it per participant (Reset to defaults on the Settings screen restores the asset).
- `trial_in_condition` counts from 0; `presentation_order` from 1. Because the dataset
  inside a set is `(trial_in_condition + participant − 1) mod 5`, the first 3 trials of
  a 5-trial session have the same conditions and datasets as a 3-trial session for the
  same participant number (the questionnaire was still answered after 5 walks).
- `cues_on` = 1 means "inside the cue region", also in Control (where nothing plays);
  `cue_value` is logged there too. Group by `condition`, not `cues_on`.
- `retrace_seconds` includes walking back to the Start marker before logging starts;
  use the retracing log's `t_seconds` for the logged retrace time.
- Head height is constant in desktop runs (camera height); only headset runs have a
  real `head_y`.
- The test location is **not** logged; note it per participant (several headsets or
  sites, see `claude/Supervisor_decisions.md`).

Practice trials go to `practice_trials.csv`, `practice_value_questions.csv`,
`practice_walking_log.csv`, `practice_retracing_log.csv`, `practice_graph_points.csv`,
so analysis never mixes them in. Copy data off the Quest with
`Tools/pull_quest_data.bat` / `.sh` (HOW_TO_RUN).

---

## 7. Analysis (`Analysis/analyze_study.py`, `Analysis/analysis_gui.py`)

Reads every participant folder (CLI or the point-and-click GUI, same code). Outputs to
`StudyData/analysis/`:

- `analysis_trials.csv` — every trial with recall % and recall abs/percentage error,
  and retrace accuracy against the real graph (`retrace_r_vs_graph`, `retrace_rmse_m`,
  `retrace_rmse_value`, from `graph_points.csv`).
- `analysis_conditions.csv` — per-condition means and SDs.
- `analysis_questionnaires.csv` — per participant × condition: SPES self-location,
  possible actions and overall means, cue checks, TLX items and Raw TLX (mean of 6).
- `analysis_participant_condition.csv` — one row per participant × condition (trials
  averaged) joined with the questionnaire scores: the input for the repeated-measures
  statistics.
- `analysis_participant_order.csv`, `summary.csv`, and optional per-trial plots of the
  retrace against the real graph.

---

## 8. Where things are

```
Assets/
  Scenes/Experiment.unity          the only scene (also the only scene in the build)
  Scripts/                         study code
    SessionController.cs           session/trial flow, questions, all CSV writing
    SessionUI.cs, SessionCanvasView.cs, SessionAnswerInput.cs   the panel
    GraphManager.cs, DataPoint.cs, GridGenerator.cs, EndpointMarkers.cs   floor graph
    ExperimentArea.cs, PathSampler.cs, WalkValueDriver.cs   layout + position → value
    ConditionManager.cs, CueConditionController.cs, DensityController.cs,
    CueAudioController.cs, AudioModeSelector.cs   conditions and cues
    RunSettings.cs, RuntimeSettings.cs, SessionBootstrap.cs   settings + start-up
    QuestionnaireDefinition.cs, ConsentDefinition.cs   questionnaire/consent assets
    Dev/                           test helpers (simulated participant, UI screenshots)
  ScriptableObjects/               RunSettings, ConsentForm, BackgroundQuestions,
                                   PostConditionQuestionnaire
  Data/UCI/Energy_dNN.asset        the 30 datasets
  Scripts/GraphSettings.asset      graph spacing/heightScale/dot size + cue field
Analysis/                          analyze_study.py, analysis_gui.py, datasets.json,
                                   make_cue_clips.py (builds the matched audio clips)
Tools/                             install_quest_build / pull_quest_data (.bat + .sh)
```

---

## 9. Open items

- **Headset check** of the floor value labels (M67) and the settings/area changes (M68)
  on the Quest.
- **Pilot** (1–2 people): session length against the 1.5 h slot, walk length/area size
  for the hall (Atalpa, 30.10–8.11.2026).
- The value-0 baseline of the grid is drawn at value 0, which can lie outside the area
  for data far from 0.
- No resume after a crash: an interrupted session is ended and the participant
  restarted under a new number (data so far is kept, `session_events.csv` shows it).
- Clear the test participant folders from the editor/headset data before real
  collection.
- **Headset taken off mid-session:** the Quest can reset its tracking origin when it
  sleeps and wakes, so the anchored area no longer lines up (seen 2026-10-31; fix
  planned in a separate commit). Until then: re-centre between trials from the
  researcher menu and note it.
- Information sheet still says 25 walks in five sets and "a VR lab at Tampere
  University"; to be updated for 3 trials per condition and Atalpa.
