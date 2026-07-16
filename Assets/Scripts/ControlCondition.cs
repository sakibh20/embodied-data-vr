using UnityEngine;

/// <summary>
/// Baseline condition: the graph + grid are visible (those are shared and not
/// owned here), and the surrounding environment stays neutral. No cues respond
/// to the walk value.
/// </summary>
public class ControlCondition : ConditionBase
{
    public override ConditionId Id => ConditionId.Control;

    public override void SetNormalizedValue(float normalized)
    {
        // Intentionally empty: the control environment never reacts to the data.
    }
}
