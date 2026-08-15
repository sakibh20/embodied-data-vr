# How to Run — Embodied-Data-VR

> Step-by-step guide to open, run, and collect data. See `PROJECT_DESCRIPTION.md` for
> how it works, `XR_MODE_SWITCHING.md` for mode details. Last updated: 2026-08-15.

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
2. Open the scene **`Assets/Scenes/Test.unity`** (it should open by default).
3. Wait for compile/import to finish (no errors in the Console).

The scene is currently saved in **VR Simulator** mode (XR rig on, desktop camera off).
Use whichever mode below you need.

---

## 2. Choose a mode

| Mode | `XR Origin (XR Rig)` | `Camera` (desktop) | Simulator setting | Headset |
|------|----------------------|--------------------|-------------------|---------|
| **A. Desktop** | disabled | enabled | off | — |
| **B. Simulator (mock VR)** | enabled | disabled | on | — |
| **C. Real headset** | enabled | disabled | off | connected |

- Rig on/off: select the GameObject in the Hierarchy, tick/untick its active checkbox (top-left of Inspector).
- Simulator setting: `Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset` →
  **Automatically Instantiate Simulator Prefab** (Editor-only; stripped from builds).

You do **not** need to rewire any camera/player references — `SessionController` and
`WalkValueDriver` auto-resolve the head for the active mode.

---

## 3. Run a session

### A. Desktop
1. Set mode A (above). Press **Play**.
2. The world-space study UI appears. Drive it with:
   - **Mouse** — click the on-screen buttons, **or**
   - **Debug keys** — **Enter** advances each phase (Idle→Walk→Distractor→Retrace→Recall→next);
     **1–4** answer recall questions.
3. Move the player during Walk/Retrace with the **DesktopWalker** camera (WASD + mouse look)
   so the walk value is driven and the retrace path is captured.
4. On the Idle screen, pick the **audio mode** (Pitch / Tempo / Both) before Start.

### B. Simulator (mock VR, no headset)
1. Set mode B. Press **Play** — the **XR Interaction Simulator** auto-spawns (on-screen help shows the controls).
2. Simulate movement/looking with keyboard + mouse (per the simulator help overlay) to
   "walk" the graph; the XR head drives the walk value exactly like a real headset.
3. Interact with the UI either by pointing a **simulated controller ray** at the buttons
   (the canvas has an XR raycaster) or with the **debug keys** (Enter / 1–4).

### C. Real headset
1. Connect the headset, start its OpenXR runtime.
2. Set mode C (turn the simulator setting **off** so simulated + real input don't conflict).
3. Press **Play** (or build & deploy). Physically walk the graph; use the controller ray
   for the UI.

> Tip: the debug keys (Enter / 1–4) work in every mode, so you can always drive the flow
> from the keyboard if needed.

---

## 4. Set the participant

Before each participant, set **`SessionController.participantId`** in the Inspector
(1, 2, 3, …). This drives both the counterbalanced order and the log filename.
Optionally set **`trialsPerCondition`** (1 or 2).

---

## 5. Where the data goes

On session finish, a JSON log is written to Unity's persistent data path:

```
<persistentDataPath>/StudyData/participant_<NN>_<yyyyMMdd_HHmmss>.json
```

On Windows this is typically:
```
C:\Users\<you>\AppData\LocalLow\DefaultCompany\Thesis\StudyData\
```
(The exact folder is printed to the Console: `[Session] Log written: …`.)

Copy each participant's JSON into one folder for analysis.

---

## 6. Analyse the data

```bash
cd Analysis
python analyze_study.py <folder_with_participant_json> --datasets datasets.json
```

Outputs (in the same folder, or pass `--out <dir>`):
- `trials.csv` — one row per trial: recall score & %, phase durations, retrace metrics,
  and a retrace shape-correlation proxy (when `--datasets` is given).
- `conditions.csv` — per-condition mean recall % + SD.

A summary is also printed to the console.

### Regenerate `datasets.json` (only if the GraphData assets change)
In Unity, enter Play (or edit) mode and run this via any editor script / the console:
it reads `SessionController.datasetPool` and writes `Analysis/datasets.json`
(name → value list). See `PROJECT_DESCRIPTION.md` §6. If you change dataset values,
regenerate so the retrace proxy stays correct.

---

## 7. Troubleshooting

- **UI not clickable with the controller ray:** ensure mode B/C (XR rig active). The debug
  keys always work as a fallback.
- **No cues / silent:** cues only play while the head is within the walk corridor
  (`PathSampler` region). Make sure you're on the graph. Control condition has no cues by design.
- **Nothing happens on Play in VR mode:** confirm exactly one XR Origin is active and the
  desktop `Camera` is disabled; check the Console for OpenXR messages.
- **Two audio listeners warning:** only one camera (desktop OR XR) should be active.
- **Simulator didn't spawn:** check the simulator setting is on and the prefab is assigned
  in `XRDeviceSimulatorSettings`.
