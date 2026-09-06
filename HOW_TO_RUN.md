# How to Run — Embodied-Data-VR

> Step-by-step guide to open, run, and collect data. See `PROJECT_DESCRIPTION.md` for
> how it works, `XR_MODE_SWITCHING.md` for mode details. Last updated: 2026-09-06.

---

## 0. Prerequisites

- **Unity 6000.3.10f1** (Unity 6.x) — open via Unity Hub.
- Packages are already in the project manifest (XR Interaction Toolkit 3.3.1, OpenXR 1.16,
  Input System, URP). Unity restores them on first open.
- For real headset runs: a Quest 2 / Varjo (or any OpenXR device) with its runtime
  (Quest Link / SteamVR / OpenXR) installed.
- For analysis: Python 3.

---

## 1. Open the project

1. Unity Hub → **Add** → select this project folder → open with **6000.3.10f1**.
2. Open the scene **`Assets/Scenes/Experiment.unity`** and press **Play**.
   `SessionBootstrap` applies your `RunSettings` (see §2) and starts the session
   automatically — no extra setup needed.
3. Wait for compile/import to finish (no errors in the Console).

---

## 2. Choose a mode

Mode is no longer picked by manually enabling/disabling GameObjects before pressing
Play. It's set on the **`RunSettings`** asset
(`Assets/ScriptableObjects/RunSettings.asset`) — select it in the Project window and
set **Run Type** in the Inspector:

| Run Type | What it drives | Simulator setting | Headset |
|----------|-----------------|--------------------|---------|
| **Desktop** | `Camera` (DesktopWalker) | off | — |
| **Simulator** | `XR Origin (XR Rig)` + XR Interaction Simulator (mock VR, no headset) | on | — |
| **VR** | `XR Origin (XR Rig)` + a real connected headset | off | connected |

`SessionBootstrap` (on the `Bootstrap` object in `Experiment.unity`) reads this at
startup and activates exactly one rig for you — `Camera` for Desktop, `XR Origin (XR
Rig)` for Simulator/VR — and the spare `XR Origin (VR)` is always kept disabled. It also
applies **Audio Mode** from the same asset. For Simulator mode, the
**Automatically Instantiate Simulator Prefab** setting
(`Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`) still needs to be on
(Editor-only; stripped from builds).

The same asset also has **Recall Phase → Recall Input Mode**: `MultipleChoice` (default — pick one of 4 options) or `TextEntry` (type the numeric value into a box instead). Switch it any time before pressing Play; `SessionUI` reads it from `RunSettings` via `StudyConfig` at startup and renders the recall phase accordingly.

You do **not** need to rewire any camera/player references, and you no longer need to
remember to toggle GameObjects before pressing Play — `PlayerRig.Head` is set explicitly
by `SessionBootstrap` from `RunSettings` and read live every frame by
`WalkValueDriver`/`SessionController`. (The previous auto-resolve via `Camera.main` /
`XRSettings.isDeviceActive` was fragile — see `ROADMAP.md` M19 — and has been replaced.)

---

## 3. Run a session

Each of these assumes `RunSettings.runType` is already set to match (see §2) before
you press Play in `Experiment.unity`.

### Desktop
1. Press **Play** (with `RunSettings.runType = Desktop`).
2. The world-space study UI appears. Drive it with:
   - **Mouse** — click the on-screen buttons, **or**
   - **Debug keys** — **Enter** advances each phase (Walk→Distractor→Retrace→Recall→next);
     **1–4** answer recall questions.
3. Move the player during Walk/Retrace with the **DesktopWalker** camera (WASD + mouse look)
   so the walk value is driven and the retrace path is captured.
4. **Audio mode** comes from `RunSettings`; it can still be changed live from the
   in-session UI (Pitch / Tempo / Both) if needed.

### Simulator (mock VR, no headset)
1. Press **Play** (with `RunSettings.runType = Simulator`) — the **XR Interaction
   Simulator** auto-spawns (on-screen help shows the controls).
2. Simulate movement/looking with keyboard + mouse (per the simulator help overlay) to
   "walk" the graph; the XR head drives the walk value exactly like a real headset.
3. Interact with the UI either by pointing a **simulated controller ray** at the buttons
   (the canvas has an XR raycaster) or with the **debug keys** (Enter / 1–4).

### VR (real headset)
1. Connect the headset, start its OpenXR runtime.
2. Set `RunSettings.runType = VR` (turn the simulator setting **off** so simulated +
   real input don't conflict).
3. Press **Play** (or build & deploy). Physically walk the graph; use the controller ray
   for the UI.

> Tip: the debug keys (Enter / 1–4) work in every mode, so you can always drive the flow
> from the keyboard if needed.

---

## 4. Set the participant

The participant ID is now auto-suggested (M28): on start, `SessionController` scans
`StudyData/` for the highest existing participant folder and defaults to one past it, and
the Idle screen shows it (`Participant: p02`) with **-`/`+`** buttons so you can see and
adjust it before clicking Start Session (no need to touch the Inspector). It drives both
the counterbalanced order and the `participant_id` column written into every CSV row
(formatted as `p01`, `p02`, …). If you do need to force a specific value, the Inspector
field (`SessionController.participantId`) still works too — it's just overwritten by the
auto-suggestion at startup, so set it after entering Play, or adjust it with the Idle
screen's `-`/`+` buttons instead. Optionally set **`trialsPerCondition`** (1 or 2).

Condition order is set on the **`RunSettings`** asset via **`conditionOrderMode`** (M29):
`Sequential` (same fixed order every session), `Random` (all 5 conditions shuffled
freely), or `RandomExceptControlFirst` (Control always first, the other 4 shuffled).
Random modes are seeded per participant, so re-running the same participant id
reproduces the same order. See `PROJECT_DESCRIPTION.md` §4.6 for the trade-offs between
the three modes (in particular: `Sequential` alone does not vary order across
participants).

---

## 5. Where the data goes

Data is written as **CSV**, incrementally (a row is appended the moment it happens, not
batched to the end of the session — so a crash mid-session loses at most the current
trial). As of M28, **each participant gets their own subfolder** — self-contained, so you
can copy/back up/delete one participant's data independently of everyone else's:

```
<persistentDataPath>/StudyData/p01/trials.csv           one row per trial
<persistentDataPath>/StudyData/p01/value_questions.csv  one row per recall question
<persistentDataPath>/StudyData/p01/retracing_log.csv    one row per retrace sample
<persistentDataPath>/StudyData/p02/trials.csv           (next participant, same 3 files)
...
```

On Windows this is typically:
```
C:\Users\<you>\AppData\LocalLow\DefaultCompany\Thesis\StudyData\p01\
```

Columns follow the schema in the sources-folder data notes doc
(`AudioVisualEmbodiment_DataNotes.docx`): `participant_id, condition, audio_cue, visual_cue,
presentation_order, dataset_id, ...` plus per-file specifics (phase durations and recall
score in `trials.csv`; question text/correct answer/response/is_correct/answer_mode in
`value_questions.csv`; timestamp + head_x/y/z in `retracing_log.csv`).

Point the analysis script (§6) at the **`StudyData`** folder itself (not a single
participant's subfolder) — it walks every participant subfolder automatically and
aggregates them, so there's still nothing to copy or merge by hand. (Data collected
before M28 as flat files directly in `StudyData/` is also still picked up, for
backward compatibility.)

---

## 6. Analyse the data

```bash
cd Analysis
python analyze_study.py <path_to_StudyData_folder> --datasets datasets.json
```

The script reads `trials.csv` and `retracing_log.csv` from every participant subfolder
under that folder and concatenates them (no more JSON parsing). Outputs go to
`<StudyData_folder>/analysis/` by
default (pass `--out <dir>` to change it) — deliberately a subfolder, so the script's own
outputs never collide with Unity's raw `trials.csv` sitting next to it:
- `analysis_trials.csv` — every raw `trials.csv` column, plus recall %, retrace path
  length/duration/spread, and a retrace shape-correlation proxy (when `--datasets` is given).
- `analysis_conditions.csv` — per-condition mean recall % + SD, mean retrace length.

A summary is also printed to the console.

### Regenerate `datasets.json` (only if the GraphData assets change)
In Unity, enter Play (or edit) mode and run this via any editor script / the console:
it reads `SessionController.datasetPool` and writes `Analysis/datasets.json`
(name → value list). See `PROJECT_DESCRIPTION.md` §6. If you change dataset values,
regenerate so the retrace proxy stays correct.

---

## 7. Clearing test data (Unity menu)

This is an Editor-only action (an operator/researcher step between sessions, not
something participants see) under the Unity menu bar: **Tools > Study Data**.

- **Clear Study Data...** — shows exactly what's currently recorded (a row count per
  CSV file, per participant folder) and asks for confirmation before deleting
  `trials.csv`, `value_questions.csv`, and `retracing_log.csv` from every participant
  folder under `StudyData/` (see §5) — including any leftover pre-M28 flat files sitting
  directly in `StudyData/`. Use this to wipe out pilot/test runs before starting real data
  collection — **this is permanent**. Greyed out automatically when there's no data to
  clear. Works whether or not you're in Play mode.
- **Show Summary** — same row-count listing, without deleting anything.
- **Open Data Folder** — reveals the `StudyData/` folder in Explorer.

---

## 8. Troubleshooting

- **UI not clickable with the controller ray:** ensure mode B/C (XR rig active). The debug
  keys always work as a fallback.
- **No cues / silent:** cues only play while the head is within the walk corridor
  (`PathSampler` region). Make sure you're on the graph. Control condition has no cues by design.
- **Nothing happens on Play in VR mode:** confirm exactly one XR Origin is active and the
  desktop `Camera` is disabled; check the Console for OpenXR messages.
- **Two audio listeners warning:** only one camera (desktop OR XR) should be active.
- **Simulator didn't spawn:** check the simulator setting is on and the prefab is assigned
  in `XRDeviceSimulatorSettings`.

---

## 9. Customizing the in-session UI

`SessionUI` no longer builds its canvas in code — it instantiates three prefabs under `Assets/Prefabs/UI/`: **`SessionCanvas.prefab`** (the panel: title, body, and a scrollable button/answer area — restyle colors/fonts/spacing here, it's a normal prefab), **`SessionOptionButton.prefab`** (one phase-action/MCQ-option button), and **`SessionAnswerInput.prefab`** (the text-entry recall answer box). Edit any of them directly in the Unity Editor; `SessionUI` only sets their text and click callbacks at runtime, so layout/look changes need no code changes.
