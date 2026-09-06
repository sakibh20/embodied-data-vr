using UnityEngine;

/// <summary>
/// Runs first (see DefaultExecutionOrder) in the study scene. Applies the RunSettings
/// asset: activates exactly one player rig for the chosen RunType, pushes the audio
/// mode session-wide, publishes the resolved head transform via PlayerRig.Head and
/// the settings themselves via StudyConfig.Settings (so SessionUI knows whether to
/// render recall as multiple-choice or a text-entry box), and then starts the
/// session -- no entry scene/button needed.
///
/// Forcing rig exclusivity here also removes a standing footgun: previously you
/// had to remember to manually enable/disable the right GameObjects (Camera vs
/// XR Origin) before pressing Play, and getting that wrong (e.g. leaving both
/// active) was exactly what made WalkValueDriver's old Camera.main-based guess
/// resolve to the wrong transform. Now there is one source of truth.
/// </summary>
[DefaultExecutionOrder(-100)]
public class SessionBootstrap : MonoBehaviour
{
    [SerializeField] private RunSettings settings;

    [Header("Rigs (exactly one is enabled for the chosen RunType)")]
    [Tooltip("The desktop Camera GameObject (DesktopWalker + AudioListener).")]
    [SerializeField] private GameObject desktopCameraObject;
    [Tooltip("\"XR Origin (XR Rig)\" -- used for both Simulator and VR run types; " +
             "the editor-only XR Interaction Simulator supplies the input in " +
             "Simulator mode and is simply absent for a real headset in VR mode.")]
    [SerializeField] private GameObject xrRigObject;
    [Tooltip("\"XR Origin (VR)\" spare/head-only rig. Always kept disabled here.")]
    [SerializeField] private GameObject spareXrRigObject;

    [SerializeField] private AudioModeSelector audioModeSelector;
    [SerializeField] private SessionController session;

    [Tooltip("Start the session automatically once the rig/audio setup is applied. " +
             "Turn off if you want to press the in-scene Start Session button/Enter " +
             "key instead (e.g. quick desktop debugging).")]
    [SerializeField] private bool autoStartSession = true;

    private void Awake()
    {
        if (audioModeSelector == null) audioModeSelector = FindAnyObjectByType<AudioModeSelector>();
        if (session == null) session = FindAnyObjectByType<SessionController>();

        StudyConfig.Settings = settings;

        RunType mode = settings != null ? settings.runType : RunType.Desktop;
        ApplyRunType(mode);

        if (audioModeSelector != null)
            audioModeSelector.SetMode(settings != null
                ? settings.audioMode
                : CueAudioController.AudioMode.Both);
    }

    private void Start()
    {
        if (autoStartSession && session != null)
            session.StartSession();
    }

    private void ApplyRunType(RunType mode)
    {
        bool desktop = mode == RunType.Desktop;

        if (desktopCameraObject != null) desktopCameraObject.SetActive(desktop);
        if (xrRigObject != null) xrRigObject.SetActive(!desktop);
        if (spareXrRigObject != null) spareXrRigObject.SetActive(false);

        PlayerRig.Mode = mode;
        PlayerRig.Head = desktop
            ? (desktopCameraObject != null ? desktopCameraObject.transform : null)
            : ResolveXrHead(xrRigObject);
    }

    private static Transform ResolveXrHead(GameObject rig)
    {
        if (rig == null) return null;
        Camera cam = rig.GetComponentInChildren<Camera>(true);
        return cam != null ? cam.transform : rig.transform;
    }
}
