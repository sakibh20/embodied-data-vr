# Switching between Desktop / Real headset

The scene (`Assets/Scenes/Experiment.unity`) runs in two modes. The same study logic,
graph, cues and logging run in both. (A third "XR Interaction Simulator" mode existed
until M50 and was removed -- its sample folder went to the Recycle Bin, and the XRI
simulator setting is off with no prefab assigned.)

## How to switch
Set **Run Type** on `Assets/ScriptableObjects/RunSettings.asset`, then press Play.
`SessionBootstrap` does everything else:

| Run Type | Active rig | Head used by the study | Extra |
|----------|------------|------------------------|-------|
| Desktop  | `Camera` (DesktopWalker, WASD + mouse) | desktop camera | -- |
| VR       | `XR Origin (XR Rig)` | XR `Main Camera` | Floor tracking forced; thumbstick move/turn/teleport/climb/jump/gravity disabled unless `Allow Controller Locomotion` is ticked (M49) |

The spare `XR Origin (VR)` is always kept disabled. Nothing needs re-wiring by hand.

## Quest 2 (Link) checklist
1. Meta Quest Link app on the PC → Settings → General → set Quest Link as the
   **active OpenXR runtime**.
2. Connect the headset (cable or Air Link) and enter Link.
3. Run Type = **VR**, press Play. Physically walk; use the controller ray for the UI.
4. Room-scale boundary with roughly 4 × 5 m clear (4 × 4 m ExperimentArea + Start/End
   markers just outside each end). Smaller room → reduce ExperimentArea size and
   GraphManager `targetWalkLength`.

## Driving the session
- Debug keys work in both modes: **Enter** advances each phase; **1–4** answer recall
  (MultipleChoice mode).
- Recall `TextEntry` in VR: the participant can't type in-headset -- the researcher
  types the spoken answer on the PC keyboard, or switch to MultipleChoice.
