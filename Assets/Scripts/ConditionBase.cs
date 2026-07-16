using UnityEngine;

/// <summary>
/// The five study conditions (see modified_plan.txt).
/// </summary>
public enum ConditionId
{
    Control,                 // 1. embodied baseline: graph + grid, neutral environment
    Abstract,                // 2. generic cross-modal cues (sphere density + abstract audio)
    Representative,          // 3. semantic cues (sparks + energy audio)
    MismatchStaticAudio,     // 4. static electric audio, mismatched visuals
    MismatchNonRepAudio      // 5. aligned visuals, non-representative audio
}

/// <summary>
/// One environment-cue set for a single condition. The graph + grid are shared
/// across all conditions; a ConditionBase only owns the surrounding cues.
/// ConditionManager activates exactly one at a time and feeds it the walk value.
/// </summary>
public abstract class ConditionBase : MonoBehaviour
{
    public abstract ConditionId Id { get; }

    /// <summary>Turn this condition's cues on. Default toggles the GameObject.</summary>
    public virtual void Activate()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    /// <summary>Turn this condition's cues off. Default toggles the GameObject.</summary>
    public virtual void Deactivate()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>Drive the cues from the current normalized walk value (0..1).</summary>
    public abstract void SetNormalizedValue(float normalized);
}
