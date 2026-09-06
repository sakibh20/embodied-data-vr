using UnityEngine;

/// <summary>Which rig drives the walk for a session.</summary>
public enum RunType { Desktop, Simulator, VR }

/// <summary>
/// Init-time configuration for a study run: which sonic dimension carries the data,
/// and which player rig to use (desktop keyboard/mouse, the editor-only XR
/// Interaction Simulator, or a real headset). Chosen on the Init screen (for now,
/// edited directly on the asset — the Init screen itself is just a Start button
/// until that UI exists); read once at startup by <see cref="SessionBootstrap"/>
/// in the study scene.
/// </summary>
[CreateAssetMenu(fileName = "RunSettings", menuName = "Embodied Data VR/Run Settings")]
public class RunSettings : ScriptableObject
{
    [Header("Audio")]
    [Tooltip("Which sonic dimension carries the data this run: pitch only, tempo " +
             "only, or both. Applied session-wide via AudioModeSelector.")]
    public CueAudioController.AudioMode audioMode = CueAudioController.AudioMode.Both;

    [Header("Run Type")]
    [Tooltip("Desktop = keyboard/mouse (DesktopWalker). Simulator = XR Interaction " +
             "Simulator driving the XR rig in-editor, no headset needed. VR = the " +
             "same XR rig driven by a real connected headset.")]
    public RunType runType = RunType.Desktop;
}
