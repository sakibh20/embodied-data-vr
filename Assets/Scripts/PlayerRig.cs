using UnityEngine;

/// <summary>
/// Session-wide pointer to whichever transform is currently "the participant's
/// head" for the active run mode. Set once by <see cref="SessionBootstrap"/> when
/// it applies the chosen <see cref="RunType"/>, and read LIVE (never cached) by
/// anything that needs the walk position (WalkValueDriver, SessionController).
///
/// Why this exists: the previous approach had each consumer independently guess
/// the head via Camera.main / XRSettings.isDeviceActive, once, in Awake. That broke
/// after the XR Interaction Simulator rig was added, for two reasons: (1) both
/// "XR Origin (XR Rig)" and the spare "XR Origin (VR)" have their inner camera
/// tagged MainCamera, so Camera.main became ambiguous whenever more than one rig
/// happened to be active/enabled; (2) the guess was made exactly once in Awake, so
/// it went stale (kept pointing at the wrong/old transform) if the active rig
/// changed after that — e.g. toggling modes without stopping Play. Both showed up
/// as "the cues react, but with an offset" and "the on-walk activation range is
/// wrong", because WalkValueDriver was reading position from a transform that
/// wasn't actually the one being moved. A single explicit assignment, driven by
/// RunSettings and re-read every frame, removes both failure modes.
/// </summary>
public static class PlayerRig
{
    public static Transform Head;
    public static RunType Mode = RunType.Desktop;
}
