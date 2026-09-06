# Switching between Desktop / VR Simulator / Real headset

The scene (`Assets/Scenes/Experiment.unity`) runs in three modes. Switching is non-destructive
— the same study logic, graph, cues, and logging run in all three. `SessionController`
and `WalkValueDriver` auto-resolve the "player head" (XR head when an XR device is
active or the desktop camera is off; otherwise the DesktopWalker), so you don't rewire
references when you switch.

> **Update (2026-09-06):** these toggles are now applied automatically by
> `SessionBootstrap` based on the `RunSettings` asset
> (`Assets/ScriptableObjects/RunSettings.asset` → `Run Type`), instead of by hand.
> The mechanics below are still accurate background (this is exactly what
> `SessionBootstrap` flips), but in normal use you only need to set `Run Type` on
> that asset — see `HOW_TO_RUN.md` §2. This also fixed a regression where the old
> `Camera.main`-based auto-resolve in `WalkValueDriver`/`SessionController` could
> pick the wrong/stale head transform (walk-value offset + broken activation-range
> gating) — see `ROADMAP.md` M19.

## The toggles

Two GameObjects in the scene + one project setting:

| Mode            | `XR Origin (XR Rig)` | `Camera` (DesktopWalker) | XR Simulator setting | Headset |
|-----------------|----------------------|--------------------------|----------------------|---------|
| Desktop         | disabled             | enabled                  | off                  | —       |
| VR Simulator    | enabled              | disabled                 | on                   | —       |
| Real headset    | enabled              | disabled                 | off                  | connected |

- Rig toggles: select the GameObject in the Hierarchy and tick/untick its active checkbox.
- Simulator setting: `Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`
  → **Automatically Instantiate Simulator Prefab** (currently ON, Editor-only so it is
  stripped from builds). The prefab is `XR Interaction Simulator` (imported from the
  XRI 3.3.1 sample).

**Currently the scene is saved in VR Simulator mode** (XR Rig on, desktop Camera off).

## Simulator → real headset
1. Connect the headset with its OpenXR runtime running.
2. Turn **Automatically Instantiate Simulator Prefab** OFF (so simulated + real input
   don't both feed the rig).
3. Press Play. The same XR rig is driven by the real device.

## Real headset → desktop
1. Enable `Camera`, disable `XR Origin (XR Rig)`.
2. (Simulator setting can stay off.) Press Play — drives with keyboard/mouse.

## Driving the session
- Debug keys (always on in `SessionController`): **Enter** advances each phase;
  **1–4** answer recall questions.
- Mouse: the world-space UI buttons are clickable in the Game view.
- Simulator controls: see the on-screen simulator help (manipulate HMD/controllers
  with keyboard + mouse).

## Known refinement (M12)
The study UI panel is head-locked ~2 m ahead, so it occludes the floor graph during the
Walk phase in-headset. Consider world-anchoring it (or moving it out of the downward
sightline) and wiring controller-ray UI (XRUIInputModule + TrackedDeviceGraphicRaycaster)
for a fully faithful VR interaction. The session is already drivable via debug keys/mouse.
