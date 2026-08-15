using UnityEngine;

/// <summary>
/// Session-wide selector for which sonic dimension carries the data (modified_plan:
/// "from ui, we should be able to choose for tempo only, pitch only and both").
/// Applies the choice to every CueAudioController so all conditions stay consistent
/// within a run. Wire three UI buttons to SetTempoOnly / SetPitchOnly / SetBoth,
/// or call SetMode from code.
/// </summary>
public class AudioModeSelector : MonoBehaviour
{
    [SerializeField] private CueAudioController.AudioMode mode = CueAudioController.AudioMode.Both;

    [Tooltip("Targets to control. Leave empty to auto-find every CueAudioController " +
             "in the scene (including inactive condition objects).")]
    [SerializeField] private CueAudioController[] controllers;

    private void Start() => Apply();

    /// <summary>Current session-wide audio mode.</summary>
    public CueAudioController.AudioMode Mode => mode;

    public void SetMode(CueAudioController.AudioMode newMode)
    {
        mode = newMode;
        Apply();
    }

    // Button-friendly wrappers (UnityEvents can't bind enum args in the inspector).
    public void SetTempoOnly() => SetMode(CueAudioController.AudioMode.TempoOnly);
    public void SetPitchOnly() => SetMode(CueAudioController.AudioMode.PitchOnly);
    public void SetBoth() => SetMode(CueAudioController.AudioMode.Both);

    private void Apply()
    {
        CueAudioController[] targets =
            (controllers != null && controllers.Length > 0)
                ? controllers
                : FindObjectsByType<CueAudioController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (CueAudioController c in targets)
            if (c != null) c.SetMode(mode);
    }
}
