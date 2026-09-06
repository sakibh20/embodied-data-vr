using UnityEngine;

/// <summary>Which rig drives the walk for a session.</summary>
public enum RunType { Desktop, Simulator, VR }

/// <summary>How the recall (memory-test) phase collects an answer.</summary>
public enum RecallInputMode { MultipleChoice, TextEntry }

/// <summary>
/// How the 5 conditions are ordered into trials within a session (see
/// SessionController.BuildTrials). Replaces relying solely on the built-in
/// participant-rotated Latin square for order control -- pick this explicitly instead.
/// </summary>
public enum ConditionOrderMode
{
    /// <summary>Same fixed order every session: Control, Abstract, Representative, SemanticAudio, SemanticVisual.</summary>
    Sequential,
    /// <summary>All 5 conditions, Control included, shuffled into a fully random order.</summary>
    Random,
    /// <summary>Control always goes first; the remaining 4 conditions are shuffled randomly after it.</summary>
    RandomExceptControlFirst,
}

/// <summary>
/// Init-time configuration for a study run: which sonic dimension carries the data,
/// which player rig to use (desktop keyboard/mouse, the editor-only XR Interaction
/// Simulator, or a real headset), and how recall questions are answered. Edited
/// directly on the asset; read once at startup by <see cref="SessionBootstrap"/> in
/// the study scene, which also publishes it to <see cref="StudyConfig"/> for the UI.
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

    [Header("Recall Phase")]
    [Tooltip("MultipleChoice = pick one of 4 options (current default). TextEntry = " +
             "type the numeric value into a text box instead of choosing.")]
    public RecallInputMode recallInputMode = RecallInputMode.MultipleChoice;

    [Header("Condition Order")]
    [Tooltip("Sequential = fixed order every session (no randomization). Random = all " +
             "5 conditions, Control included, shuffled into any order. " +
             "RandomExceptControlFirst = Control always goes first, the remaining 4 " +
             "are shuffled after it. Random modes are seeded from the participant id " +
             "(and block, if trialsPerCondition > 1), so a given participant's order " +
             "is reproducible, not dependent on global random state.")]
    public ConditionOrderMode conditionOrderMode = ConditionOrderMode.Sequential;
}
