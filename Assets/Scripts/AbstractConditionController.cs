using UnityEngine;

public class AbstractConditionController : MonoBehaviour
{
    [Header("Value Control")]
    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    [Header("References")]
    [SerializeField] private DensityController densityController;
    [SerializeField] private AudioTempoController audioController;

    private float _lastValue = -1f;

    private void Update()
    {
        // Only update when value changes (efficient)
        if (Mathf.Approximately(_lastValue, normalizedValue)) return;

        _lastValue = normalizedValue;

        densityController?.UpdateDensity(normalizedValue);
        audioController?.UpdateTempo(normalizedValue);
    }

    // Optional: for external control later (from graph walking)
    public void SetValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);
    }
}