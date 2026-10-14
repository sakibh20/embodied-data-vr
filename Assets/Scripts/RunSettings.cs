using UnityEngine;

/// <summary>Which rig drives the walk for a session.</summary>
// Explicit values: VR keeps its old serialized value (2) after the XR Interaction
// Simulator mode (was 1) was removed in M50, so existing RunSettings assets stay valid.
public enum RunType { Desktop = 0, VR = 2 }

/// <summary>Look of the SEMANTIC visual cue (spark), chosen per run (M55). Applies to every
/// condition whose DensityController has "Use Semantic Visual Variant" ticked
/// (Representative, SemanticVisual).</summary>
public enum SemanticVisualVariant
{
    /// <summary>A: pointy 3D spark star (needle mesh, no ball).</summary>
    SparkStar,
    /// <summary>B: yellow ball with glowing spark streaks around it.</summary>
    SparkOrb,
    /// <summary>C: the pointy star mesh plus a warm glow and the streak rays (no ball).</summary>
    SparkStarGlow,
}

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
    /// <summary>Balanced (Williams) Latin square: 10 rows for 5 conditions, row picked by
    /// participant id ((id-1) mod 10). Balances both position and first-order carryover
    /// (which condition follows which) across every 10 participants. See ROADMAP.md M47.</summary>
    LatinSquare,
}

/// <summary>
/// Init-time configuration for a study run: which sonic dimension carries the data,
/// which player rig to use (desktop keyboard/mouse or a real headset), and how recall questions are answered. Edited
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
    [Tooltip("Desktop = keyboard/mouse (DesktopWalker). VR = the XR rig driven by a " +
             "real connected headset (Quest 2 via Link).")]
    public RunType runType = RunType.Desktop;

    [Header("Recall Phase")]
    [Tooltip("MultipleChoice = pick one of 4 options (current default). TextEntry = " +
             "type the numeric value into a text box instead of choosing.")]
    public RecallInputMode recallInputMode = RecallInputMode.MultipleChoice;

    [Header("Condition Order")]
    [Tooltip("Sequential = fixed order every session (no randomization). Random = all " +
             "5 conditions, Control included, shuffled into any order. " +
             "RandomExceptControlFirst = Control always goes first, the remaining 4 " +
             "are shuffled after it. LatinSquare = balanced Williams square, row = " +
             "(participant id - 1) mod 10 -- recruit in multiples of 10 for full balance. Random modes are seeded from the participant id " +
             "(and block, if trialsPerCondition > 1), so a given participant's order " +
             "is reproducible, not dependent on global random state.")]
    public ConditionOrderMode conditionOrderMode = ConditionOrderMode.Sequential;

    [Header("Semantic visual (M55)")]
    [Tooltip("Which spark look the semantic-visual conditions use. A = pointy star, " +
             "B = yellow ball with spark streaks, C = pointy star + glow + streaks. " +
             "Changing it during Play affects newly spawned sparks.")]
    public SemanticVisualVariant semanticVisualVariant = SemanticVisualVariant.SparkStar;
    public GameObject sparkStarPrefab;
    public GameObject sparkOrbPrefab;
    public GameObject sparkStarGlowPrefab;

    /// <summary>The prefab for the chosen variant (null if not assigned).</summary>
    public GameObject SemanticVisualPrefab
    {
        get
        {
            switch (semanticVisualVariant)
            {
                case SemanticVisualVariant.SparkOrb: return sparkOrbPrefab;
                case SemanticVisualVariant.SparkStarGlow: return sparkStarGlowPrefab;
                default: return sparkStarPrefab;
            }
        }
    }

    [Header("XR (VR)")]
    [Tooltip("Off (study default): thumbstick move/turn, teleport, climb and jump are " +
             "disabled on the XR rig, so the participant can ONLY move by physically " +
             "walking -- keeps the virtual graph locked to the real room. On: the XRI " +
             "starter-rig locomotion is left as authored (debugging only). See ROADMAP.md M49.")]
    public bool allowControllerLocomotion = false;
}
