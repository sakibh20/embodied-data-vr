using UnityEngine;

/// <summary>
/// Sonifies the walk value for a condition. Two independent dimensions can carry
/// the data (modified_plan): frequency (pitch) and tempo (pulse rate). The active
/// dimension(s) are chosen session-wide by <see cref="AudioModeSelector"/>:
/// pitch only, tempo only, or both.
///
/// - Pitch only  → the clip loops continuously; its pitch rises with the value.
/// - Tempo only  → the clip is retriggered as discrete pulses; the pulse rate
///                  rises with the value; pitch stays neutral.
/// - Both        → pulses whose rate AND pitch both rise with the value.
///
/// Swap <see cref="clip"/> per condition (generic tone / energy hum / electric).
/// Set <see cref="respondToValue"/> = false for the static-electric mismatch
/// condition: the clip then loops at a steady pitch and ignores the walk.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CueAudioController : MonoBehaviour
{
    public enum AudioMode { PitchOnly, TempoOnly, Both }

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Clip this condition plays: generic tone (abstract), energy hum " +
             "(semantic), or static electric. Swap per condition.")]
    [SerializeField] private AudioClip clip;

    [Header("Mode")]
    [Tooltip("Which sonic dimension carries the data. Normally set globally at " +
             "runtime by AudioModeSelector.")]
    [SerializeField] private AudioMode mode = AudioMode.Both;

    [Tooltip("When false the cue ignores the walk value and plays a steady hum " +
             "(the static-electric mismatch condition).")]
    [SerializeField] private bool respondToValue = true;

    [Header("Frequency (pitch) range")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.5f;

    [Header("Tempo (pulses per second) range")]
    [SerializeField] private float minPulsesPerSecond = 1f;
    [SerializeField] private float maxPulsesPerSecond = 8f;

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 5f;

    private float _normalized;   // latest walk value, 0..1
    private float _pitch;        // smoothed current pitch
    private float _pulseTimer;   // counts down to the next pulse
    private bool _muted;         // gated off while the participant is off the walk

    public AudioMode Mode => mode;

    private void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        _pitch = audioSource.pitch;
    }

    // Runs whenever the condition is activated (its GameObject is enabled).
    private void OnEnable() => ConfigureSource();

    private void OnDisable()
    {
        if (audioSource != null) audioSource.Stop();
    }

    /// <summary>Push the current normalized walk value (0..1) from the condition.</summary>
    public void SetNormalizedValue(float value) => _normalized = Mathf.Clamp01(value);

    /// <summary>Switch the sonic dimension (called by AudioModeSelector).</summary>
    public void SetMode(AudioMode newMode)
    {
        mode = newMode;
        if (isActiveAndEnabled) ConfigureSource();
    }

    /// <summary>Silence/resume the cue without changing mode (region gating).</summary>
    public void SetMuted(bool muted)
    {
        if (_muted == muted) return;
        _muted = muted;

        if (muted)
        {
            if (audioSource != null) audioSource.Stop();
        }
        else if (isActiveAndEnabled)
        {
            ConfigureSource();
        }
    }

    // Continuous (looping) playback is used for pitch-only and for the steady hum;
    // tempo/both drive discrete PlayOneShot pulses instead, so the source must not
    // also be looping the clip underneath.
    private void ConfigureSource()
    {
        if (audioSource == null) return;
        if (clip != null) audioSource.clip = clip;

        bool continuous = !respondToValue || mode == AudioMode.PitchOnly;
        audioSource.loop = continuous;

        if (continuous)
        {
            if (clip != null && !audioSource.isPlaying) audioSource.Play();
        }
        else
        {
            audioSource.Stop();
            _pulseTimer = 0f;
        }
    }

    private void Update()
    {
        if (audioSource == null || _muted) return;

        if (!respondToValue)
        {
            // Steady electric hum: hold a neutral pitch, ignore the walk value.
            audioSource.pitch = Mathf.Lerp(audioSource.pitch, 1f, Time.deltaTime * smoothSpeed);
            return;
        }

        float targetPitch = Mathf.Lerp(minPitch, maxPitch, _normalized);

        switch (mode)
        {
            case AudioMode.PitchOnly:
                _pitch = Mathf.Lerp(_pitch, targetPitch, Time.deltaTime * smoothSpeed);
                audioSource.pitch = _pitch;
                break;

            case AudioMode.TempoOnly:
                audioSource.pitch = 1f;
                TickPulse(1f);
                break;

            case AudioMode.Both:
                _pitch = Mathf.Lerp(_pitch, targetPitch, Time.deltaTime * smoothSpeed);
                TickPulse(_pitch);
                break;
        }
    }

    // Emit one pulse when the timer elapses; interval shrinks as the value (and
    // therefore pulses-per-second) rises. Uses Play() (a restart) rather than
    // PlayOneShot so a new pulse cuts off the previous one instead of layering on
    // top of it — otherwise a long clip retriggered fast stacks into noise. Pair
    // this with a SHORT percussive clip for a clean "tick … tick" cue.
    private void TickPulse(float pitch)
    {
        if (clip == null) return;

        float pps = Mathf.Lerp(minPulsesPerSecond, maxPulsesPerSecond, _normalized);
        if (pps <= 0f) return;

        _pulseTimer -= Time.deltaTime;
        if (_pulseTimer <= 0f)
        {
            audioSource.pitch = pitch;
            audioSource.Play();   // restart: one non-overlapping pulse
            _pulseTimer = 1f / pps;
        }
    }
}
