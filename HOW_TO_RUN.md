# How to Run — Embodied-Data-VR

> Practical guide: build and install on the Quest, run a session, copy and analyse the
> data. How the system works is in `PROJECT_DESCRIPTION.md`; history in `ROADMAP.md`.
> Last updated: 2026-10-27 (through M69).

---

## 0. Prerequisites

- **Unity 6000.3.10f1** with **Android Build Support** (OpenJDK, Android SDK & NDK).
  Packages (XR Interaction Toolkit 3.3.1, OpenXR, Input System, URP) restore on first
  open.
- **Meta Quest 2** with **Developer Mode** on (the headset owner's Meta account) and
  USB debugging allowed. The study runs **standalone on the headset** — no PC or cable
  during sessions (M63).
- Python 3 for the analysis (`tkinter` for the GUI, `matplotlib` optional for plots).

---

## 1. Build and install on the Quest

1. Open the project, scene `Assets/Scenes/Experiment.unity` (the only scene in the
   build). Active build target: **Android**.
2. **File → Build Profiles → Android → Build** to `Builds/Quest/EmbodiedDataViz.apk`.
   (App id `com.sakibhasan.embodieddataviz`, IL2CPP, ARM64, OpenXR + Meta Quest
   feature.) On device the app always runs in VR mode.
3. Connect the Quest by USB, put it on and accept "Allow USB debugging".
4. Install:
   - Windows: double-click `Tools/install_quest_build.bat`
   - macOS: `bash Tools/install_quest_build.sh`

   Both pick the headset by model name (a phone plugged in at the same time is ignored)
   and install with `-r`, which **keeps the study data already on the headset**. Never
   uninstall the app before copying the data off — uninstalling deletes it.
5. In the headset: **Library → Unknown Sources → Thesis**.

---

## 2. Before each session

- Set up the room boundary (Guardian) covering the walk area. With the defaults the
  footprint is about **4 m wide × 6 m long** (Start marker 0.6 m + gap 0.4 m + 4 m graph
  zone + gap + End marker), plus space to stand around it.
- Open the app, check **Settings** (§4) — walk length, area width, trials per condition
  (5), condition order (Latin square), recall answers (number pad), audio (tempo only),
  spark look (C), tutorial on, dataset order (Latin sq. sets). Settings are saved on the headset and kept between
  launches.
- Have the paper **information sheet** and **privacy notice** ready, and the laptop with
  the Microsoft Form (four open-ended questions) for the end.

---

## 3. Run a session

1. Participant reads the information sheet and privacy notice on paper and can ask
   questions.
2. Put the headset on them. On the **start screen** check the participant number
   (auto-suggested as the next free one; − / + to change; a red warning means that
   number already has data).
3. Stand the participant on the spot where the walk should start, **facing the walk
   direction**, and press **Start Session** — the whole layout (Start marker, graph,
   End marker, cue field) is anchored to that pose.
4. From here the participant mostly drives it themselves (controller ray + trigger):
   consent → 2 background questions → welcome text → 2 practice trials → "Tutorial
   complete" → 25 trials (5 conditions × 5, recall after every trial, questionnaire
   after each condition's 5th trial) → end screen.
5. Each trial: walk from the green **Start** to the red **End** marker looking at the
   values → counting task (count out loud; press Done when told to stop) → walk back to
   Start and walk the shape again from memory → the traced shape is shown for 5 s → four
   recall questions on the number pad → walk back to Start → **Start Next Trial**.
6. At the end: "remove the headset", then give the laptop with the Microsoft Form.

### Researcher menu (any time)

Hold the **left controller's menu button for 1.5 s** (Esc in the editor):

- **Continue** — close the menu.
- **Skip this step** — Walk, counting, Retrace, review, instructions, between trials (not
  recall or questionnaires).
- **Re-centre area here** — between trials, re-anchors the layout to the current pose.
- **End session** — confirm; keeps the data so far and returns to the start screen with
  the next participant number. Also on the red **End session** button in the panel's
  top-right corner.
- **Settings** (when no session runs), **Close app**.

All of these are logged in `session_events.csv`.

### Running in the editor (development)

Set **Run Type** on `Assets/ScriptableObjects/RunSettings.asset` (Desktop or VR) and
press Play — see `XR_MODE_SWITCHING.md`. Desktop: WASD + mouse to walk, click the
panel. Debug keys: **Enter** advances a phase, **1–4** answer recall (4-choice mode),
**1–5 / Enter / Backspace** in the questionnaire, **R** on the start screen re-anchors
the area.

---

## 4. Settings (start screen → Settings)

| Setting | Default | Notes |
|---------|---------|-------|
| Walk length | 3 m | 2–15 m; area depth follows (walk + 1 m) |
| Area width | 4 m | 2–10 m; the graph is narrowed to fit if needed |
| Marker gap | 0.4 m | silent gap between the graph zone and each marker |
| Trials / condition | 5 | 1–10 |
| Condition order | Latin square | Fixed / Random / Control first / Latin square |
| Recall answers | Number pad | or 4 choices |
| Audio cue | Tempo only | pitch / tempo / both |
| Spark look | C: star + glow | A / B / C |
| Tutorial | On | |
| Dataset order | Latin sq. sets | datasets in 5 fixed sets, Latin-square rotated across conditions; or Rotate (old pool rotation) |

Changes are saved immediately and shown on the floor straight away: the old graph is
cleared and the Start/End markers move to the new layout (the Start spot stays where it
is). **Reset to defaults** restores the values from `RunSettings.asset` and the scene.
The **Study data** row shows how many participants/trials are on the device;
**Delete all data…** wipes them after a red confirm — copy the data first (§5).

---

## 5. Copy the data off the Quest

Connect by USB (headset on, USB debugging allowed), then:

- Windows: `Tools/pull_quest_data.bat`
- macOS: `bash Tools/pull_quest_data.sh`

This copies `/sdcard/Android/data/com.sakibhasan.embodieddataviz/files/StudyData` to
`QuestData/<date-time>/` in the project (git-ignored — participant data is never
committed). Copy after **every** session day.

Data written per participant (`StudyData/pNN/`): `session_info`, `consent`,
`background`, `trials`, `value_questions`, `walking_log`, `retracing_log`,
`graph_points`, `questionnaires`, `session_events` (all `.csv`), plus `practice_*.csv`
for the tutorial. Columns are listed in `PROJECT_DESCRIPTION.md` §6. Every row has a
`session_id`, so two runs under the same number stay separable.

In the editor the data goes to
`C:\Users\<you>\AppData\LocalLow\DefaultCompany\Thesis\StudyData\` (Windows); the Unity
menu **Tools → Study Data** shows a summary, opens the folder, or clears it (permanent).

---

## 6. Analyse

GUI:

```bash
cd Analysis
python analysis_gui.py
```

Choose the `StudyData` folder (the one containing `p01`, `p02`, …), then **Analyze All**
or select one participant. Command line:

```bash
python analyze_study.py <StudyData folder>
python analyze_study.py <StudyData folder> --participant p03
```

Outputs go to `<StudyData>/analysis/` (`--out` to change): `analysis_trials.csv`,
`analysis_conditions.csv`, `analysis_questionnaires.csv`,
`analysis_participant_condition.csv` (one row per participant × condition — the input
for the repeated-measures statistics), `analysis_participant_order.csv`, `summary.csv`,
and per-trial retrace plots (`--no-plots` to skip). Retrace accuracy is computed against
each trial's real graph from `graph_points.csv`.

---

## 7. Before real data collection

- Clear test data from the headset (Settings → Delete all data…, after copying anything
  you need) and from the editor (Tools → Study Data → Clear Study Data…).
- Check the settings on the headset are the study values (§4).
- Run a pilot (1–2 people) to check the session fits the time slot.

---

## 8. Troubleshooting

- **"No Quest found" in the install/pull script:** headset on, USB debugging accepted in
  the headset, Developer Mode on; try another cable/port.
- **Installed on the wrong device:** the scripts only use a device whose model is Quest;
  if one isn't listed, nothing is installed.
- **No cues:** cues only play in Walk/Retrace, after stepping on the Start marker, and
  only inside the graph zone. Control has no cues by design.
- **Panel out of view:** it re-centres when you look away for about a second; the
  researcher menu works from anywhere.
- **Stuck in a step:** researcher menu → Skip this step, or End session.
