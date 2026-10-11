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
    [Tooltip("\"XR Origin (XR Rig)\" -- the VR rig, driven by the real headset.")]
    [SerializeField] private GameObject xrRigObject;
    [Tooltip("\"XR Origin (VR)\" spare/head-only rig. Always kept disabled here.")]
    [SerializeField] private GameObject spareXrRigObject;

    [SerializeField] private AudioModeSelector audioModeSelector;
    [SerializeField] private SessionController session;

    [Tooltip("Start the session automatically once the rig/audio setup is applied, " +
             "skipping the Idle screen. Off by default so every fresh Play (not just " +
             "the screen shown after \"Next Participant\") gives the researcher a " +
             "chance to confirm/adjust the participant ID and audio mode before the " +
             "first trial begins -- see ROADMAP.md M33.")]
    [SerializeField] private bool autoStartSession = false;

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

        if (!desktop && xrRigObject != null)
        {
            ForceFloorTracking(xrRigObject);
            if (settings == null || !settings.allowControllerLocomotion)
                DisableControllerLocomotion(xrRigObject);
        }

        PlayerRig.Mode = mode;
        PlayerRig.Head = desktop
            ? (desktopCameraObject != null ? desktopCameraObject.transform : null)
            : ResolveXrHead(xrRigObject);
    }

    // Real walking needs head height measured from the real floor (Quest Guardian
    // floor). The rig was saved as "Not Specified", which lets the runtime pick a
    // device-relative origin plus a hard-coded camera Y offset -- graph, cues and the
    // logged head_y would then depend on where the headset was when tracking started.
    private static void ForceFloorTracking(GameObject rig)
    {
        var origin = rig.GetComponent<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null)
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
    }

    // The XRI starter rig ships with thumbstick move, snap/continuous turn, teleport,
    // climb, jump and gravity enabled. Any of them would slide/rotate the virtual room
    // under the participant, so the floor graph would no longer sit where they are
    // physically walking. Disable every LocomotionProvider, stop the controller input
    // manager from entering teleport mode, and hide the teleport rays.
    private static void DisableControllerLocomotion(GameObject rig)
    {
        foreach (var p in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
            p.enabled = false;

        // ControllerInputActionManager is an XRI *sample* script, so it is reached by
        // name/reflection to avoid a hard dependency on the sample assembly.
        // smoothMotionEnabled = true turns the teleport-mode input actions OFF (and the
        // smooth-move provider it would hand over to is disabled above).
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.GetType().Name == "ControllerInputActionManager")
            {
                var prop = mb.GetType().GetProperty("smoothMotionEnabled");
                if (prop != null && prop.CanWrite) prop.SetValue(mb, true);
            }
            if (mb is UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor &&
                mb.gameObject.name.Contains("Teleport"))
                mb.gameObject.SetActive(false);
        }
    }

    private static Transform ResolveXrHead(GameObject rig)
    {
        if (rig == null) return null;
        Camera cam = rig.GetComponentInChildren<Camera>(true);
        return cam != null ? cam.transform : rig.transform;
    }
}
