# Desktop vs headset

> Last updated: 2026-10-25. The study runs **standalone on the Quest 2** (Android
> build, M63); PC/Link and Desktop modes are for development in the editor.

The scene (`Assets/Scenes/Experiment.unity`) runs in two modes with the same study
logic, graph, cues and logging.

## How the mode is chosen

- **On the Quest (Android build):** always VR — `SessionBootstrap` forces it.
- **In the editor:** set **Run Type** on `Assets/ScriptableObjects/RunSettings.asset`,
  then press Play.

| Run Type | Active rig | Head used by the study | Extra |
|----------|------------|------------------------|-------|
| Desktop | `Camera` (DesktopWalker, WASD + mouse) | desktop camera | — |
| VR | `XR Origin (XR Rig)` | XR `Main Camera` | Floor tracking forced; thumbstick move/turn/teleport disabled unless `Allow Controller Locomotion` is ticked (M49) |

`SessionBootstrap` activates exactly one rig and sets `PlayerRig.Head`; the spare
`XR Origin (VR)` is always disabled. Nothing needs re-wiring by hand. (The XR
Interaction Simulator mode was removed in M50.)

## Quest 2 in the editor (Link, Windows only)

1. Meta Quest Link app → Settings → General → Quest Link as the **active OpenXR
   runtime**.
2. Connect the headset (cable or Air Link), enter Link.
3. Run Type = **VR**, press Play.

macOS has no Link Play mode: build and install the APK instead (`HOW_TO_RUN.md` §1).

## Interaction

- Headset: controller ray + trigger for the panel; hold the left menu button 1.5 s for
  the researcher menu. Recall answers are typed on the panel's number pad.
- Desktop: mouse for the panel, Esc for the researcher menu, debug keys (Enter = next
  phase, 1–4 = recall choices, 1–5 / Enter / Backspace in the questionnaire, R on the
  start screen = re-anchor the area).
