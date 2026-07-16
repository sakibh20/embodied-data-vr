using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps a world position to the data value at that point along the walk path.
/// Input-agnostic: it only knows the path's start, forward direction, point spacing
/// and the raw values. GraphManager configures it once the graph is aligned.
/// </summary>
public class PathSampler : MonoBehaviour
{
    private Vector3 _start;          // world position of data point 0 (time origin)
    private Vector3 _dir;            // unit forward along the path (walk/time axis), on the ground
    private float _segment;          // world distance between adjacent points (= GraphSettings.spacing)
    private List<float> _values = new List<float>();
    private float _min, _max;
    private bool _ready;

    public bool IsReady => _ready;
    public int PointCount => _values.Count;
    public float TotalLength => Mathf.Max(0, PointCount - 1) * _segment;

    /// <summary>Provide the path geometry. Call after the graph is fully aligned.</summary>
    public void Configure(Vector3 start, Vector3 forward, float segmentLength, IList<float> values)
    {
        _start = start;
        _dir = forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        _segment = segmentLength;
        _values = new List<float>(values);

        _ready = _values.Count > 0 && _segment > 0f;
        if (!_ready) return;

        _min = _max = _values[0];
        foreach (var v in _values)
        {
            if (v < _min) _min = v;
            if (v > _max) _max = v;
        }
    }

    public void Clear() => _ready = false;

    /// <summary>Signed distance the position has progressed along the path forward axis.</summary>
    public float DistanceAlong(Vector3 worldPos) => Vector3.Dot(worldPos - _start, _dir);

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

    /// <summary>Value normalized to 0..1 across the dataset's min..max, for driving cues.</summary>
    public float NormalizedValueAt(Vector3 worldPos)
    {
        if (!_ready || Mathf.Approximately(_min, _max)) return 0f;
        return Mathf.InverseLerp(_min, _max, RawValueAt(worldPos));
    }
}
