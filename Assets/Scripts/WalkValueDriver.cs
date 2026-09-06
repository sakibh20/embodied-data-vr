using UnityEngine;

/// <summary>
/// Bridges the participant's position to the active condition's cues. Reads the
/// player position, asks the PathSampler for the interpolated value there, and
/// pushes it to the condition controller. Input-agnostic: works with the desktop
/// walker now and the XR rig later, as long as 'player' points at the head/camera.
/// </summary>
public class WalkValueDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private PathSampler path;

    [Header("Cue Target")]
    [Tooltip("Routes the walk value to whichever condition is currently active.")]
    [SerializeField] private ConditionManager conditionManager;

    [Header("State")]
    [SerializeField] private bool active = true;

    [Range(0f, 1f)]
    [SerializeField] private float debugNormalizedValue;   // read-only view in inspector

    public void SetActive(bool value) => active = value;

private void Awake()
    {
        // No longer resolves/caches the head here (see Update) -- PlayerRig.Head is
        // set by SessionBootstrap and re-read live every frame, so this component
        // never goes stale if the active rig changes after Awake. 'player' remains
        // only as a last-resort fallback for running this scene without a
        // SessionBootstrap in it (e.g. an older/ad-hoc test scene).
    }

    // Mirror SessionController: use the XR head camera when an XR device is active,
    // otherwise the serialized ref / DesktopWalker. Lets the same scene drive the
    // walk value in VR (real or simulator) and on desktop with no manual re-wiring.
    private Transform ResolvePlayerHead()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive && Camera.main != null)
            return Camera.main.transform;

        if (player != null && player.gameObject.activeInHierarchy)
            return player;

        var walker = FindAnyObjectByType<DesktopWalker>();
        if (walker != null) return walker.transform;

        return Camera.main != null ? Camera.main.transform : player;
    }

private void Update()
    {
        Transform head = PlayerRig.Head != null ? PlayerRig.Head : ResolvePlayerHead();
        if (!active || head == null || path == null || !path.IsReady) return;

        // Cues run only while the participant is on the walk; outside the graph
        // region they are gated off (silent, no density) rather than clamped.
        bool inRegion = path.IsWithinRegion(head.position);

        if (conditionManager != null)
            conditionManager.SetCuesActive(inRegion);

        if (!inRegion) return;

        float normalized = path.NormalizedValueAt(head.position);
        debugNormalizedValue = normalized;

        if (conditionManager != null)
            conditionManager.SetNormalizedValue(normalized);
    }
}
