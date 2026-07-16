using UnityEngine;

/// <summary>
/// Single place to control overall loudness. Drives the global AudioListener
/// volume, so it scales every sound in the scene (all condition cues) at once.
/// Adjust in the inspector (live via OnValidate) or from a UI slider via
/// SetVolume(0..1).
/// </summary>
public class VolumeController : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    public float MasterVolume => masterVolume;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    /// <summary>Set master volume 0..1 (bind a UI Slider's onValueChanged here).</summary>
    public void SetVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        Apply();
    }

    private void Apply() => AudioListener.volume = masterVolume;
}
