using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps a world position to the data value at that point along the walk path.
/// Input-agnostic: it only knows the path's start, forward direction, point spacing
/// and the raw values. GraphManager configures it once the graph is aligned.
/// </summary>
public class PathSampler : MonoBehaviour
{
    [Header("Region")]
    [Tooltip("Slack (metres) allowed before the first point and after the last " +
             "before the participant counts as off the walk.")]
    [SerializeField] private float regionMargin = 0.5f;
    [Tooltip("Half-width (metres) of the walkable corridor either side of the path " +
             "centre line. Beyond this the participant is off the graph.")]
    [SerializeField] private float regionHalfWidth = 2f;

    private Vector3 _start;          // world position of data point 0 (time origin)
    private Vector3 _dir;            // unit forward along the path (walk/time axis), on the ground
    private float _segment;          // world distance between adjacent points (= GraphSettings.spacing)
    private List<float> _values = new List<float>();
    private float _min, _max;
    private bool _ready;

    // Value axis (M46): the graph lies flat, so the data VALUE is a lateral floor
    // offset perpendicular to the walk axis. Derived purely from the final dot
    // positions (not from GraphManager's rotation maths), so it stays correct for
    // any resize / re-centre / ExperimentArea orientation and for whichever side the
    // value happens to grow toward.
    private Vector3 _valueDir;       // unit, on the ground, perpendicular to _dir, pointing toward HIGHER values
    private float _latMin, _latMax;  // lateral coordinate (from _start along _valueDir) of the min / max value lines
    private bool _hasValueAxis;

    public bool IsReady => _ready;
    public int PointCount => _values.Count;
    public float TotalLength => Mathf.Max(0, PointCount - 1) * _segment;

    /// <summary>Half-width of the walkable corridor (see regionHalfWidth) -- exposed so
    /// GraphManager/EndpointMarkers can size the floor Start/End markers to match the
    /// actual walkable zone rather than an arbitrary constant. See ROADMAP.md M40.</summary>
    public float RegionHalfWidth => regionHalfWidth;

    /// <summary>Slack margin beyond each end still counted as "on the walk" (see
    /// regionMargin) -- exposed for the same reason as RegionHalfWidth. See ROADMAP.md M40.</summary>
    public float RegionMargin => regionMargin;

    /// <summary>True when a lateral value axis could be derived (dot positions were
    /// supplied and the dataset isn't flat). When false, standing-position feedback
    /// falls back to the old value-at-progress mapping. See ROADMAP.md M46.</summary>
    public bool HasValueAxis => _ready && _hasValueAxis;

    /// <summary>Unit ground-plane direction in which the data value increases.</summary>
    public Vector3 ValueAxis => _valueDir;

    /// <summary>Provide the path geometry. Call after the graph is fully aligned.
    /// <paramref name="pointPositions"/> (optional, world positions of the dots in the
    /// same order as <paramref name="values"/>) lets the sampler derive the lateral
    /// value axis used for standing-position feedback (M46).</summary>
    public void Configure(Vector3 start, Vector3 forward, float segmentLength, IList<float> values,
                          IList<Vector3> pointPositions = null)
    {
        _start = start;
        _dir = forward;
        _dir.y = 0f;   // walk axis lives on the ground
        _dir = _dir.sqrMagnitude > 0f ? _dir.normalized : Vector3.forward;
        _segment = segmentLength;
        _values = new List<float>(values);
        _hasValueAxis = false;

        _ready = _values.Count > 0 && _segment > 0f;
        if (!_ready) return;

        int iMin = 0, iMax = 0;
        _min = _max = _values[0];
        for (int i = 1; i < _values.Count; i++)
        {
            if (_values[i] < _min) { _min = _values[i]; iMin = i; }
            if (_values[i] > _max) { _max = _values[i]; iMax = i; }
        }

        if (pointPositions != null && pointPositions.Count == _values.Count && iMin != iMax)
            ConfigureValueAxis(pointPositions[iMin], pointPositions[iMax]);
    }

    // The value axis is the ground-plane direction from the lowest-value dot to the
    // highest-value dot, with its walk-axis component removed (those two dots also sit
    // at different points in time). Its two lines -- lateral offsets of the min and the
    // max dot -- are the 0 and 1 ends of the standing-position feedback.
    private void ConfigureValueAxis(Vector3 minPos, Vector3 maxPos)
    {
        Vector3 v = maxPos - minPos;
        v.y = 0f;
        v -= Vector3.Dot(v, _dir) * _dir;
        if (v.sqrMagnitude < 1e-8f) return;   // degenerate (e.g. no lateral spread) -> fallback

        _valueDir = v.normalized;
        _latMin = Vector3.Dot(minPos - _start, _valueDir);
        _latMax = Vector3.Dot(maxPos - _start, _valueDir);
        _hasValueAxis = _latMax - _latMin > 1e-4f;
    }

    public void Clear() => _ready = false;

    /// <summary>Signed distance the position has progressed along the path forward axis.</summary>
    public float DistanceAlong(Vector3 worldPos) => Vector3.Dot(worldPos - _start, _dir);

    /// <summary>
    /// True when the position is within the walkable graph corridor: between the
    /// ends (plus margin) along the path, and within the lateral half-width of the
    /// centre line. Height is ignored. Used to gate cues so they only run while the
    /// participant is actually on the walk.
    /// </summary>
    public bool IsWithinRegion(Vector3 worldPos)
    {
        if (!_ready) return false;

        float d = DistanceAlong(worldPos);
        if (d < -regionMargin || d > TotalLength + regionMargin) return false;

        // With a value axis, the corridor is centred on the middle of the graph's
        // value band (min line .. max line) instead of on dot 0's own line -- so it
        // covers both sides of the graph evenly, which matters now that standing
        // sideways IS the feedback (M46). regionHalfWidth keeps its meaning (metres
        // either side of the corridor centre line), but is never allowed to be
        // narrower than the band itself, so the whole graph is always inside.
        if (_hasValueAxis)
        {
            float mid = 0.5f * (_latMin + _latMax);
            float halfBand = 0.5f * (_latMax - _latMin);
            float lat = LateralOffset(worldPos) - mid;
            return Mathf.Abs(lat) <= Mathf.Max(regionHalfWidth, halfBand);
        }

        // Lateral offset from the centre line (clamp the foot point to the segment).
        Vector3 foot = _start + _dir * Mathf.Clamp(d, 0f, TotalLength);
        Vector3 lateral = worldPos - foot;
        lateral.y = 0f;
        return lateral.magnitude <= regionHalfWidth;
    }

    /// <summary>Normalized progress along the whole walk, 0..1.</summary>
    public float Progress01(Vector3 worldPos)
        => TotalLength > 0f ? Mathf.Clamp01(DistanceAlong(worldPos) / TotalLength) : 0f;

    /// <summary>Continuously interpolated raw data value at this position (clamped at the ends).</summary>
    public float RawValueAt(Vector3 worldPos)
    {
        if (!_ready) return 0f;

        float d = Mathf.Clamp(DistanceAlong(worldPos), 0f, TotalLength);
        float t = d / _segment;                 // fractional point index
        int i = Mathf.FloorToInt(t);
        if (i >= PointCount - 1) return _values[PointCount - 1];
        return Mathf.Lerp(_values[i], _values[i + 1], t - i);
    }

    /// <summary>Value normalized to 0..1 across the dataset's min..max, for driving cues.
    /// This is the graph's value at the participant's PROGRESS along the walk (where
    /// they stand sideways is ignored). Kept as the legacy feedback source.</summary>
    public float NormalizedValueAt(Vector3 worldPos)
    {
        if (!_ready || Mathf.Approximately(_min, _max)) return 0f;
        return Mathf.InverseLerp(_min, _max, RawValueAt(worldPos));
    }

    // ---- Standing-position feedback (M46) ----

    /// <summary>Signed ground-plane offset of the position along the value axis,
    /// measured from dot 0's walk line. Height is ignored.</summary>
    public float LateralOffset(Vector3 worldPos)
    {
        Vector3 p = worldPos - _start;
        p.y = 0f;
        return Vector3.Dot(p, _valueDir);
    }

    /// <summary>
    /// The value the participant is STANDING on: their lateral floor position mapped
    /// onto the graph's value axis, normalized 0..1 between the dataset's min line and
    /// max line (clamped beyond them). Same per-dataset 0..1 range as
    /// NormalizedValueAt, so cue gains stay matched across conditions/datasets.
    /// Falls back to NormalizedValueAt when no value axis is available.
    /// </summary>
    public float NormalizedPositionValueAt(Vector3 worldPos)
    {
        if (!_ready) return 0f;
        if (!_hasValueAxis) return NormalizedValueAt(worldPos);
        return Mathf.InverseLerp(_latMin, _latMax, LateralOffset(worldPos));
    }

    /// <summary>NormalizedPositionValueAt expressed in the dataset's own units.</summary>
    public float RawPositionValueAt(Vector3 worldPos)
        => Mathf.Lerp(_min, _max, NormalizedPositionValueAt(worldPos));

    // Scene-view aid: the min (blue) and max (red) value lines that bound the
    // standing-position feedback, plus the corridor centre (yellow).
    private void OnDrawGizmosSelected()
    {
        if (!_ready || !_hasValueAxis) return;
        Vector3 a = _start - _dir * regionMargin;
        Vector3 b = _start + _dir * (TotalLength + regionMargin);

        Gizmos.color = new Color(0.3f, 0.5f, 1f);
        Gizmos.DrawLine(a + _valueDir * _latMin, b + _valueDir * _latMin);
        Gizmos.color = new Color(1f, 0.3f, 0.3f);
        Gizmos.DrawLine(a + _valueDir * _latMax, b + _valueDir * _latMax);
        Gizmos.color = new Color(1f, 0.85f, 0.2f);
        float mid = 0.5f * (_latMin + _latMax);
        Gizmos.DrawLine(a + _valueDir * mid, b + _valueDir * mid);
    }
}
