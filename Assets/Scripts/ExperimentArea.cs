using UnityEngine;

/// <summary>
/// Defines the physical experiment area in the room: a centred rectangle (or
/// square) on the ground that both the graph and the visual cue field lay out
/// within, so they share one centre. Move/rotate this GameObject to place the
/// area; set Size (width × depth, metres) to match the play space. The graph and
/// DensityController reference it (auto-found if there's one in the scene).
///
///   Right   = width axis  (lateral / value spread)
///   Forward = depth axis  (walk direction)
/// </summary>
public class ExperimentArea : MonoBehaviour
{
    [Tooltip("Footprint in metres: X = width (lateral), Y = depth (walk axis). " +
             "Equal values = square, unequal = rectangle.")]
    [SerializeField] private Vector2 size = new Vector2(4f, 4f);

    public Vector3 Center => transform.position;
    public Vector3 Right => transform.right;      // width axis (lateral)
    public Vector3 Forward => transform.forward;  // depth axis (walk direction)
    public float Width => size.x;
    public float Depth => size.y;
    public Vector2 Size => size;

    /// <summary>Map normalized area coords (u,v in -0.5..0.5) to a world point on the area plane.</summary>
    public Vector3 PointAt(float u, float v) =>
        Center + Right * (u * size.x) + Forward * (v * size.y);

    /// <summary>A uniformly random point on the area plane.</summary>
    public Vector3 RandomPoint() =>
        PointAt(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));

    private void OnDrawGizmos()
    {
        Vector3 c = Center;
        Vector3 r = Right * (size.x * 0.5f);
        Vector3 f = Forward * (size.y * 0.5f);
        Vector3 a = c - r - f, b = c + r - f, d = c + r + f, e = c - r + f;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, d);
        Gizmos.DrawLine(d, e); Gizmos.DrawLine(e, a);

        // Walk-direction arrow through the centre.
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawLine(c - Forward * (size.y * 0.5f), c + Forward * (size.y * 0.5f));
    }
}
