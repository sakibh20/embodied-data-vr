using UnityEngine;

/// <summary>
/// The five study conditions (see modified_plan.txt).
/// </summary>
// A 2×2 over whether each modality's cue is representative (semantic, "electric")
// vs generic, plus a no-cue control. In conditions 2–5 BOTH cues track the data
// (density + audio tempo/pitch rise with value); only the asset's meaning changes.
public enum ConditionId
{
    Control,          // 1. embodied baseline: graph + grid, neutral environment; no cues
    Abstract,         // 2. generic audio + generic visual (beep + sphere)
    Representative,   // 3. representative audio + representative visual (electric hum + spark)
    SemanticAudio,    // 4. representative audio, generic visual (electric hum + sphere)
    SemanticVisual    // 5. representative visual, generic audio (spark + beep)
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

    /// <summary>
    /// Gate the cues on/off without switching condition: cues run only while the
    /// participant is on the walk (see PathSampler.IsWithinRegion). Default no-op
    /// (Control has nothing to gate).
    /// </summary>
    public virtual void SetCuesActive(bool active) { }
}
