using UnityEngine;

/// <summary>
/// Sample-accurate gain ramp for one AudioSource (M53). Unity starts/stops an
/// AudioSource instantly, which cuts the waveform mid-cycle and produces an audible
/// "pop". This filter runs on the audio thread and moves the gain toward its target
/// a little every SAMPLE (not every frame), so starts, stops, mutes and pulse
/// cut-offs all become short fades instead of jumps. Lives on the same GameObject as
/// the AudioSource it shapes (that is how Unity applies OnAudioFilterRead).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class DeclickGain : MonoBehaviour
{
    private volatile float _target;      // 0..1, written on the main thread
    private volatile bool _snapToZero;   // start the next buffer from silence
    private float _gain;                 // audio-thread state
    private float _step = 1f / 400f;     // gain change per sample

    /// <summary>Ramp length in seconds for a full 0 -> 1 (or 1 -> 0) change.</summary>
    public void Configure(float fadeSeconds)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        _step = 1f / Mathf.Max(1f, fadeSeconds * rate);
    }

    /// <summary>Fade toward this gain (0 = silent, 1 = full).</summary>
    public void FadeTo(float target) => _target = Mathf.Clamp01(target);

    /// <summary>Call right before AudioSource.Play(): the new sound ramps in from 0.</summary>
    public void StartFromSilence()
    {
        _snapToZero = true;
        _target = 1f;
    }

    public float Target => _target;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_snapToZero) { _gain = 0f; _snapToZero = false; }

        float g = _gain, target = _target, step = _step;
        for (int i = 0; i < data.Length; i += channels)
        {
            if (g < target) g = Mathf.Min(target, g + step);
            else if (g > target) g = Mathf.Max(target, g - step);
            for (int c = 0; c < channels; c++) data[i + c] *= g;
        }
        _gain = g;
    }
}
