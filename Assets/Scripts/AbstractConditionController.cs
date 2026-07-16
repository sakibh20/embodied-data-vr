using UnityEngine;

/// <summary>
/// Condition 2 (Abstract): generic cross-modal cues — sphere density and abstract
/// audio tempo/pitch scale with the data value. Change detection keeps the manual
/// inspector slider usable for testing while the walk drives it at runtime.
/// </summary>
public class AbstractConditionController : ConditionBase
{
    [Header("Value Control")]
    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    [Header("References")]
    [SerializeField] private DensityController densityController;
    [SerializeField] private AudioTempoController audioController;

    private float _lastValue = -1f;

    public override ConditionId Id => ConditionId.Abstract;

    private void Update()
    {
        // Apply only when the value changes (covers both the driver and the slider).
        if (Mathf.Approximately(_lastValue, normalizedValue)) return;
        Apply(normalizedValue);
    }

    public override void SetNormalizedValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);
    }

    private void Apply(float value)
    {
        _lastValue = value;
        densityController?.UpdateDensity(value);
        audioController?.UpdateTempo(value);
    }
}
