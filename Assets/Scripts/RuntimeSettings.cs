using System;
using UnityEngine;

/// <summary>
/// M64: study settings the researcher can change INSIDE the app (Idle screen ->
/// Settings), needed now that the study runs as a standalone Quest build with no
/// Unity editor. Defaults come from the RunSettings asset + scene (walk length on
/// GraphManager, area size on ExperimentArea); changes are saved on the device
/// (PlayerPrefs) and re-applied every launch until "Reset to defaults".
/// SessionBootstrap works on a runtime COPY of RunSettings, so changing a value in
/// Play mode never edits the asset on disk.
/// </summary>
[Serializable]
public class RuntimeValues
{
    public float walkLength = 3f;          // m, graph length along the walk
    public float areaWidth = 4f;           // m, width of the area / markers / cue field
    public float markerGap = 0.4f;         // m, safe gap graph <-> Start/End marker
    public int trialsPerCondition = 1;
    public ConditionOrderMode conditionOrder = ConditionOrderMode.LatinSquare;
    public RecallInputMode recallInput = RecallInputMode.TextEntry;
    public CueAudioController.AudioMode audioMode = CueAudioController.AudioMode.TempoOnly;
    public SemanticVisualVariant sparkVariant = SemanticVisualVariant.SparkStar;
    public bool tutorial = true;

    public RuntimeValues Clone() => (RuntimeValues)MemberwiseClone();
}

public static class RuntimeSettings
{
    private const string PrefsKey = "EmbodiedDataVR.RuntimeSettings.v1";
    // The graph zone is this much shorter than the area depth (0.5 m each end),
    // as in the original layout (walk 3 m, depth 4 m).
    public const float DepthMargin = 1f;

    public static RuntimeValues Current { get; private set; }
    public static RuntimeValues Defaults { get; private set; }
    public static bool HasSavedChanges => PlayerPrefs.HasKey(PrefsKey);

    private static RunSettings _rs;
    private static GraphManager _graph;
    private static ExperimentArea _area;
    private static AudioModeSelector _audio;

    /// <summary>Called once by SessionBootstrap: capture defaults, load saved values, apply.</summary>
    public static void Init(RunSettings rs, GraphManager graph, ExperimentArea area, AudioModeSelector audio)
    {
        _rs = rs; _graph = graph; _area = area; _audio = audio;
        Defaults = new RuntimeValues
        {
            walkLength = graph != null ? graph.TargetWalkLength : 3f,
            areaWidth = area != null ? area.Width : 4f,
            markerGap = area != null ? area.MarkerGap : 0.4f,
            trialsPerCondition = rs != null ? rs.trialsPerCondition : 1,
            conditionOrder = rs != null ? rs.conditionOrderMode : ConditionOrderMode.LatinSquare,
            recallInput = rs != null ? rs.recallInputMode : RecallInputMode.TextEntry,
            audioMode = rs != null ? rs.audioMode : CueAudioController.AudioMode.TempoOnly,
            sparkVariant = rs != null ? rs.semanticVisualVariant : SemanticVisualVariant.SparkStar,
            tutorial = rs == null || rs.tutorialEnabled,
        };
        Current = Defaults.Clone();
        if (PlayerPrefs.HasKey(PrefsKey))
        {
            try { JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(PrefsKey), Current); }
            catch (Exception e) { Debug.LogWarning($"[RuntimeSettings] Ignoring saved settings: {e.Message}"); Current = Defaults.Clone(); }
        }
        Apply();
    }

    /// <summary>Push Current into the runtime RunSettings copy, the graph, the area and the audio.</summary>
    public static void Apply()
    {
        var v = Current;
        if (v == null) return;
        if (_rs != null)
        {
            _rs.trialsPerCondition = Mathf.Clamp(v.trialsPerCondition, 1, 10);
            _rs.conditionOrderMode = v.conditionOrder;
            _rs.recallInputMode = v.recallInput;
            _rs.audioMode = v.audioMode;
            _rs.semanticVisualVariant = v.sparkVariant;
            _rs.tutorialEnabled = v.tutorial;
        }
        if (_graph != null) _graph.TargetWalkLength = v.walkLength;
        if (_area != null)
        {
            _area.SetSize(new Vector2(v.areaWidth, v.walkLength + DepthMargin));
            _area.SetMarkerGap(v.markerGap);
        }
        if (_audio != null) _audio.SetMode(v.audioMode);
    }

    /// <summary>Apply and save on this device.</summary>
    public static void Save()
    {
        Apply();
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(Current));
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        Current = Defaults.Clone();
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
        Apply();
    }

    // ---- stepping helpers for the in-app settings screen ----
    public static float Step(float value, float step, float min, float max, int dir) =>
        Mathf.Clamp(Mathf.Round((value + dir * step) / step) * step, min, max);

    public static T Cycle<T>(T value, int dir) where T : Enum
    {
        var vals = (T[])Enum.GetValues(typeof(T));
        int i = Array.IndexOf(vals, value);
        return vals[((i + dir) % vals.Length + vals.Length) % vals.Length];
    }
}
