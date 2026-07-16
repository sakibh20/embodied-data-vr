using UnityEngine;

/// <summary>
/// Drives the environmental cues for any cue-based condition (Abstract,
/// Representative, or either Mismatch). What separates those conditions is data,
/// not logic: the density visual (sphere vs spark prefab on the DensityController),
/// the audio clip, and whether the audio tracks the walk value — all inspector
/// fields. So one component serves all four; the ConditionId is serialized per
/// instance. Control has no cues and keeps its own ControlCondition.
///
/// Change detection keeps the manual inspector slider usable for editor testing
/// while the walk drives the same field at runtime.
/// </summary>
public class CueConditionController : ConditionBase
{
    [Header("Identity")]
    [Tooltip("Which study condition this cue-set represents. Must be unique across " +
             "the conditions registered on the ConditionManager.")]
    [SerializeField] private ConditionId id = ConditionId.Abstract;

    [Header("Value Control")]
    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    [Header("References")]
    [SerializeField] private DensityController densityController;
    [SerializeField] private CueAudioController audioController;

    private float _lastValue = -1f;
    private bool _cuesActive = true;

    public override ConditionId Id => id;

    private void Awake()
    {
        // Cue controllers normally live on the same GameObject; auto-wire them so
        // scene setup only needs the data fields (id, prefab, clip) filled in.
        if (densityController == null) densityController = GetComponent<DensityController>();
        if (audioController == null) audioController = GetComponent<CueAudioController>();
    }

    private void Update()
    {
        // Cues run only while on the walk; when gated off, drive nothing.
        if (!_cuesActive) return;

        // Apply only when the value changes (covers both the driver and the slider).
        // Audio pulsing is self-driven inside CueAudioController every frame; here we
        // only need to forward value changes.
        if (Mathf.Approximately(_lastValue, normalizedValue)) return;
        Apply(normalizedValue);
    }

    public override void SetNormalizedValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);
    }

    public override void SetCuesActive(bool active)
    {
        if (_cuesActive == active) return;
        _cuesActive = active;

        if (active)
        {
            // Resume: force a re-apply next Update at the current value.
            _lastValue = -1f;
            audioController?.SetMuted(false);
        }
        else
        {
            // Off the walk: clear the visual field and silence the audio.
            densityController?.Clear();
            audioController?.SetMuted(true);
        }
    }

    private void Apply(float value)
    {
        _lastValue = value;
        densityController?.UpdateDensity(value);
        audioController?.SetNormalizedValue(value);
    }
}
