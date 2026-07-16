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

    private void Update()
    {
        if (!active || player == null || path == null || !path.IsReady) return;

        float normalized = path.NormalizedValueAt(player.position);
        debugNormalizedValue = normalized;

        if (conditionManager != null)
            conditionManager.SetNormalizedValue(normalized);
    }
}
