using UnityEngine;

/// <summary>
/// Sonifies the walk value for a condition. Two independent dimensions can carry
/// the data (modified_plan): frequency (pitch) and tempo (pulse rate). The active
/// dimension(s) are chosen session-wide by <see cref="AudioModeSelector"/>:
///
/// - Pitch only  -> a clip loops continuously; its pitch rises with the value.
/// - Tempo only  -> the clip is retriggered as discrete pulses; the pulse RATE rises
///                  with the value. The clip itself always plays at normal speed
///                  (pitch exactly 1) -- tempo never speeds the sound up. (M53)
/// - Both        -> pulses whose rate AND pitch both rise with the value.
///
/// M53 -- click-free playback: Unity starts/stops an AudioSource instantly, which
/// pops. Every voice now has a <see cref="DeclickGain"/> filter that ramps its gain
/// per sample (fadeMilliseconds), so starts, stops, mutes, pulse cut-offs and the
/// natural end of a clip are all short fades. Pulses alternate between voices: a new
/// pulse starts on a fresh voice while the previous one fades out, so pulses never
/// pile up and never get chopped mid-waveform.
///
/// Clips: <see cref="clip"/> is the PULSE clip (one short sound). <see cref="loopClip"/>
/// (optional) is used for continuous playback (Pitch only / static); if empty, clip is
/// looped. Set <see cref="respondToValue"/> = false to make it a steady loop that
/// ignores the walk (no study condition uses this since M47).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CueAudioController : MonoBehaviour
{
    public enum AudioMode { PitchOnly, TempoOnly, Both }

    [Header("Audio")]
    [Tooltip("Template for the runtime voices (volume, spatial blend, mixer group). " +
             "It is not played itself.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Pulse clip: one short sound, retriggered for tempo. Swap per condition " +
             "(generic beep / electric spark).")]
    [SerializeField] private AudioClip clip;
    [Tooltip("Optional seamless loop for continuous playback (Pitch only). Empty = loop 'clip'.")]
    [SerializeField] private AudioClip loopClip;

    [Header("Mode")]
    [Tooltip("Which sonic dimension carries the data. Normally set globally at " +
             "runtime by AudioModeSelector.")]
    [SerializeField] private AudioMode mode = AudioMode.Both;

    [Tooltip("When false the cue ignores the walk value and plays a steady loop " +
             "(not used by any condition since M47).")]
    [SerializeField] private bool respondToValue = true;

    [Header("Frequency (pitch) range")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.5f;

    [Header("Tempo (pulses per second) range")]
    [SerializeField] private float minPulsesPerSecond = 1f;
    [SerializeField] private float maxPulsesPerSecond = 8f;

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 5f;

    [Header("De-click (M53)")]
    [Tooltip("Fade length for every start/stop/mute/cut-off. 5-15 ms removes pops " +
             "without softening the attack noticeably.")]
    [SerializeField, Range(1f, 50f)] private float fadeMilliseconds = 8f;

    // One shared pool for loop and pulse playback. A new sound always takes a voice
    // that is genuinely silent (stopped, or finished its fade-out) while the previous
    // sound fades out on its own voice -- so Stop() is never called on an audible voice.
    [Header("Live readout (Play mode, read-only)")]
    [Tooltip("Current target repeat rate (pulses per second) from the walk value.")]
    [SerializeField] private float livePulsesPerSecond;
    [Tooltip("Measured time between the last two pulses (seconds) = 1 / tempo.")]
    [SerializeField] private float liveMeasuredInterval;
    [Tooltip("Playback speed of the clip (AudioSource.pitch) used for the last pulse. " +
             "1.00 = normal speed and pitch. In Tempo Only this must always read 1.00.")]
    [SerializeField] private float livePlaybackSpeed = 1f;
    [Tooltip("Number of pulses played since this condition was activated.")]
    [SerializeField] private int livePulseCount;
    private float _lastPulseTime = -1f;

    private const int VoiceCount = 5;

    private Voice[] _voices;
    private Voice _loop;          // voice currently carrying the continuous loop (or null)

    private float _normalized;   // latest walk value, 0..1
    private float _pitch = 1f;   // smoothed current pitch
    private float _pulseTimer;   // counts down to the next pulse
    private bool _muted = true;  // gated off while the participant is off the walk -- starts true (see OnEnable)

    public AudioMode Mode => mode;

    private class Voice
    {
        public AudioSource Source;
        public DeclickGain Gain;
        public float StopAt = -1f;   // unscaled time to Stop() after a fade-out
    }

    private float FadeSeconds => fadeMilliseconds / 1000f;

    private AudioClip _pulseClip;   // de-clicked runtime copies (see Declicked)
    private AudioClip _loopClip;

    private void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        BuildVoices();
        _pulseClip = Declicked(clip, 0.002f, FadeSeconds);
        _loopClip = Declicked(loopClip != null ? loopClip : clip, 0.002f, 0.003f);
    }

    // A clip that starts or ends on a non-zero sample pops whenever it starts/ends,
    // and an end-of-clip fade triggered from Update can be late when a frame hitches.
    // So each clip gets a runtime copy with short raised-cosine fades baked into the
    // sample data: every play now begins and ends on silence regardless of frame
    // timing. Cached per source clip so all conditions share one copy. (M53)
    private static readonly System.Collections.Generic.Dictionary<string, AudioClip> s_declicked =
        new System.Collections.Generic.Dictionary<string, AudioClip>();

    private static AudioClip Declicked(AudioClip c, float fadeIn, float fadeOut)
    {
        if (c == null) return null;
        string key = c.GetInstanceID() + "_" + fadeIn + "_" + fadeOut;
        if (s_declicked.TryGetValue(key, out var cached) && cached != null) return cached;

        if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
        int ch = c.channels, frames = c.samples;
        var data = new float[frames * ch];
        if (frames == 0 || !c.GetData(data, 0))
        {
            Debug.LogWarning($"[CueAudio] Could not read '{c.name}' to de-click it " +
                             "(set its Load Type to Decompress On Load). Using it as-is.");
            return c;
        }

        int fi = Mathf.Clamp(Mathf.RoundToInt(fadeIn * c.frequency), 1, frames / 2);
        int fo = Mathf.Clamp(Mathf.RoundToInt(fadeOut * c.frequency), 1, frames / 2);
        for (int f = 0; f < fi; f++)
        {
            float g = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * f / fi);
            for (int k = 0; k < ch; k++) data[f * ch + k] *= g;
        }
        for (int f = 0; f < fo; f++)
        {
            float g = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * f / fo);   // 0 at the last frame
            int idx = frames - 1 - f;
            for (int k = 0; k < ch; k++) data[idx * ch + k] *= g;
        }

        var copy = AudioClip.Create(c.name + "_declicked", frames, ch, c.frequency, false);
        copy.SetData(data, 0);
        s_declicked[key] = copy;
        return copy;
    }

    private void BuildVoices()
    {
        // The serialized AudioSource stays only as a settings template.
        audioSource.playOnAwake = false;
        audioSource.Stop();
        audioSource.enabled = false;

        _voices = new Voice[VoiceCount];
        for (int i = 0; i < VoiceCount; i++) _voices[i] = CreateVoice("Voice" + i);
    }

    // A voice that is silent now: not playing, or its fade-out has completed. If every
    // voice is busy (should not happen with 5), take the one whose fade began earliest.
    private Voice AcquireSilentVoice()
    {
        float now = Time.unscaledTime;
        Voice best = null;
        foreach (var v in _voices)
        {
            if (!v.Source.isPlaying) return v;
            if (v.StopAt >= 0f && now >= v.StopAt) return v;
            if (v.StopAt >= 0f && (best == null || v.StopAt < best.StopAt)) best = v;
        }
        return best ?? _voices[0];
    }

    private void PlayOn(Voice v, AudioClip c, bool loop, float pitch)
    {
        v.Source.Stop();             // silent by construction (see AcquireSilentVoice)
        v.Source.clip = c;
        v.Source.loop = loop;
        v.Source.pitch = pitch;
        v.StopAt = -1f;
        v.Gain.StartFromSilence();
        v.Source.Play();
    }

    private Voice CreateVoice(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.volume = audioSource.volume;
        src.spatialBlend = audioSource.spatialBlend;
        src.priority = audioSource.priority;
        src.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        src.dopplerLevel = audioSource.dopplerLevel;
        src.rolloffMode = audioSource.rolloffMode;
        src.minDistance = audioSource.minDistance;
        src.maxDistance = audioSource.maxDistance;
        var gain = go.AddComponent<DeclickGain>();
        gain.Configure(FadeSeconds);
        gain.FadeTo(0f);
        return new Voice { Source = src, Gain = gain };
    }

    // Runs whenever the condition is activated (its GameObject is enabled). Always
    // starts muted: activating a condition is not the same as the participant being
    // on the walk with a ready PathSampler. Region gating (WalkValueDriver ->
    // ConditionManager.SetCuesActive -> SetMuted(false)) turns it on. See ROADMAP.md M38.
    private void OnEnable()
    {
        _muted = true;
        _pulseTimer = 0f;
        livePulseCount = 0;
        _lastPulseTime = -1f;
        if (_voices != null) StopAllNow();
    }

    private void OnDisable()
    {
        if (_voices != null) StopAllNow();
    }

    /// <summary>Push the current normalized walk value (0..1) from the condition.</summary>
    public void SetNormalizedValue(float value) => _normalized = Mathf.Clamp01(value);

    /// <summary>Switch the sonic dimension (called by AudioModeSelector).</summary>
    public void SetMode(AudioMode newMode)
    {
        if (mode == newMode) return;
        mode = newMode;
        if (isActiveAndEnabled && !_muted) Restart();
    }

    /// <summary>Silence/resume the cue without changing mode (region gating). Fades, never cuts.</summary>
    public void SetMuted(bool muted)
    {
        if (_muted == muted) return;
        _muted = muted;

        if (muted) FadeOutAll();
        else if (isActiveAndEnabled) Restart();
    }

    /// <summary>True while any voice is still audible (used to delay deactivation until faded).</summary>
    public bool IsAudible
    {
        get
        {
            if (_voices == null) return false;
            foreach (var v in _voices) if (v.Source.isPlaying) return true;
            return false;
        }
    }

    /// <summary>Seconds a fade-out takes (plus one audio buffer of slack).</summary>
    public float FadeOutSeconds => FadeSeconds + 0.03f;

    private bool Continuous => !respondToValue || mode == AudioMode.PitchOnly;

    private void Restart()
    {
        FadeOutAll();
        _pulseTimer = 0f;   // first pulse fires on the next Update
        if (Continuous) StartLoop();
    }

    private void StartLoop()
    {
        AudioClip c = _loopClip;
        if (c == null) return;
        if (_loop != null && _loop.Source.isPlaying) FadeOut(_loop);   // crossfade, never cut
        _loop = AcquireSilentVoice();
        PlayOn(_loop, c, true, respondToValue ? _pitch : 1f);
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        ServiceStops(now);

        if (_muted) return;

        if (!respondToValue)
        {
            // Steady loop: hold a neutral pitch, ignore the walk value.
            if (_loop != null) _loop.Source.pitch = Mathf.Lerp(_loop.Source.pitch, 1f, Time.deltaTime * smoothSpeed);
            return;
        }

        float targetPitch = Mathf.Lerp(minPitch, maxPitch, _normalized);
        _pitch = Mathf.Lerp(_pitch, targetPitch, Time.deltaTime * smoothSpeed);

        switch (mode)
        {
            case AudioMode.PitchOnly:
                if (_loop == null || !_loop.Source.isPlaying || _loop.Gain.Target <= 0f) StartLoop();
                if (_loop != null) _loop.Source.pitch = _pitch;
                break;

            case AudioMode.TempoOnly:
                TickPulse(1f);          // normal speed: tempo changes, pitch does not
                break;

            case AudioMode.Both:
                TickPulse(_pitch);
                break;
        }

        FadeBeforeClipEnds();
    }

    // Emit one pulse when the timer elapses; interval shrinks as the value (and
    // therefore pulses-per-second) rises.
    private void TickPulse(float pitch)
    {
        if (_pulseClip == null) return;

        float pps = Mathf.Lerp(minPulsesPerSecond, maxPulsesPerSecond, _normalized);
        livePulsesPerSecond = pps;
        if (pps <= 0f) return;

        _pulseTimer -= Time.deltaTime;
        if (_pulseTimer > 0f) return;
        // Carry the overshoot into the next interval (+=, not =) so the AVERAGE tempo is
        // exact; resetting to the full interval made every gap one partial frame too long
        // (measured: 4.26/s instead of 4.50/s). After a long hitch, don't fire a burst of
        // catch-up pulses -- just restart the rhythm. (M57)
        _pulseTimer += 1f / pps;
        if (_pulseTimer < 0f) _pulseTimer = 1f / pps;

        // Previous sound(s) fade out in fadeMilliseconds instead of being chopped;
        // the new pulse starts on a voice that is already silent.
        foreach (var v in _voices)
            if (v.Source.isPlaying && v.Gain.Target > 0f) FadeOut(v);
        _loop = null;

        PlayOn(AcquireSilentVoice(), _pulseClip, false, pitch);

        float now = Time.time;
        if (_lastPulseTime >= 0f) liveMeasuredInterval = now - _lastPulseTime;
        _lastPulseTime = now;
        livePlaybackSpeed = pitch;
        livePulseCount++;
    }

    // A non-looping clip that simply runs out would stop abruptly at its last sample;
    // start the fade just before its end so it lands on silence. (Clips prepared for
    // the study also have their own baked fades; this protects any other clip.)
    private void FadeBeforeClipEnds()
    {
        float lookahead = FadeSeconds + Time.deltaTime * 2f;
        foreach (var v in _voices)
        {
            var s = v.Source;
            if (!s.isPlaying || s.loop || s.clip == null || v.Gain.Target <= 0f) continue;
            float remaining = (s.clip.length - s.time) / Mathf.Max(0.01f, Mathf.Abs(s.pitch));
            if (remaining <= lookahead) FadeOut(v);
        }
    }

    private void FadeOut(Voice v)
    {
        v.Gain.FadeTo(0f);
        if (v.StopAt < 0f) v.StopAt = Time.unscaledTime + FadeOutSeconds;
    }

    private void FadeOutAll()
    {
        if (_voices == null) return;
        foreach (var v in _voices) if (v.Source.isPlaying) FadeOut(v);
        _loop = null;
    }

    private void ServiceStops(float now)
    {
        if (_voices == null) return;
        foreach (var v in _voices) ServiceStop(v, now);
    }

    private static void ServiceStop(Voice v, float now)
    {
        if (v.StopAt >= 0f && now >= v.StopAt)
        {
            v.Source.Stop();
            v.StopAt = -1f;
        }
    }

    private void StopAllNow()
    {
        foreach (var v in _voices) { v.Source.Stop(); v.Gain.FadeTo(0f); v.StopAt = -1f; }
        _loop = null;
    }
}
