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

    [Header("Start / End zones (M56)")]
    [Tooltip("Safe gap (metres) between the graph zone (this area's near/far edge) and the " +
             "Start / End markers. Standing in the gap gives no feedback and does not count " +
             "as being at the start or the end -- a transition buffer.")]
    [SerializeField] private float markerGap = 0.2f;
    [Tooltip("Depth (metres, along the walk) of the Start and End marker zones.")]
    [SerializeField] private float markerDepth = 0.6f;

    public float MarkerGap => markerGap;

    /// <summary>M64: runtime size/gap from the in-app settings (next graph/session uses them).</summary>
    /// M68: the Start spot (where the participant stands) stays put; the area grows or
    /// shrinks forward from it, instead of around its centre (which moved the Start
    /// marker away from the participant and could leave the graph off the marked area).
    public void SetSize(Vector2 newSize)
    {
        Vector3 spot = ParticipantSpot;
        size = new Vector2(Mathf.Max(0.5f, newSize.x), Mathf.Max(0.5f, newSize.y));
        KeepParticipantSpot(spot);
    }

    public void SetMarkerGap(float gap)
    {
        Vector3 spot = ParticipantSpot;
        markerGap = Mathf.Max(0f, gap);
        KeepParticipantSpot(spot);
    }

    private void KeepParticipantSpot(Vector3 spot)
    {
        Vector3 c = spot + Forward * (NearZoneEdge + markerDepth * 0.5f);
        transform.position = new Vector3(c.x, transform.position.y, c.z);
    }
    public float MarkerDepth => markerDepth;

    // Layout along the walk axis (area frame z, from the centre):
    //   Start marker | gap | ---------- graph zone (Depth) ---------- | gap | End marker
    private float NearZoneEdge => size.y * 0.5f + markerGap;   // |z| where a marker begins

    /// <summary>True when the position is on (or beyond) the Start marker -- i.e. past
    /// the safe gap on the near side. Height and lateral position are ignored.</summary>
    public bool IsInStartZone(Vector3 world) => WorldToArea(world).z <= -NearZoneEdge;

    /// <summary>True when the position is on (or beyond) the End marker.</summary>
    public bool IsInEndZone(Vector3 world) => WorldToArea(world).z >= NearZoneEdge;

    /// <summary>World position of the inner edge (towards the graph) of the Start / End
    /// marker, on the centre line, at floor height.</summary>
    public Vector3 StartMarkerInnerEdge => Center - Forward * NearZoneEdge;
    public Vector3 EndMarkerInnerEdge => Center + Forward * NearZoneEdge;

    public Vector3 Center => transform.position;
    public Vector3 Right => transform.right;      // width axis (lateral)
    public Vector3 Forward => transform.forward;  // depth axis (walk direction)
    public float Width => size.x;
    public float Depth => size.y;
    public Vector2 Size => size;

    /// <summary>Map normalized area coords (u,v in -0.5..0.5) to a world point on the area plane.</summary>
    public Vector3 PointAt(float u, float v) =>
        Center + Right * (u * size.x) + Forward * (v * size.y);

    /// <summary>
    /// Re-anchor the area to the participant (M51): rotate it so its walk axis (Forward)
    /// points the way they are facing (yaw only), and move it so its near edge is
    /// in front of them such that they stand in the middle of the Start marker (M56: past
    /// the safe gap). Floor height (Y) is kept. Everything
    /// that lays out from the area -- graph, grid, Start/End markers, cue field -- follows
    /// on the next graph generation / spawn.
    /// </summary>
    public void AlignToParticipant(Transform head)
    {
        if (head == null) return;

        Vector3 fwd = head.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
        {
            // Looking (almost) straight down/up: use the top of the head instead.
            fwd = head.up; fwd.y = 0f;
        }
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 feet = new Vector3(head.position.x, transform.position.y, head.position.z);
        transform.SetPositionAndRotation(
            feet + fwd * (NearZoneEdge + markerDepth * 0.5f),
            Quaternion.LookRotation(fwd, Vector3.up));
    }

    /// <summary>Where the participant stands (on the floor) relative to this area:
    /// the middle of the Start marker (gap + half its depth before the near edge), on the
    /// centre line. Equals the
    /// participant's feet right after AlignToParticipant.</summary>
    public Vector3 ParticipantSpot => Center - Forward * (NearZoneEdge + markerDepth * 0.5f);

    /// <summary>
    /// World position -> area frame: x = lateral (along Right), y = height above the
    /// area floor, z = along the walk axis, all relative to the area centre. With the
    /// area at the world origin facing +Z this is identical to world coordinates, so
    /// logs written in this frame stay comparable with pre-M51 data.
    /// </summary>
    public Vector3 WorldToArea(Vector3 world)
    {
        Vector3 d = world - Center;
        return new Vector3(Vector3.Dot(d, Right), d.y, Vector3.Dot(d, Forward));
    }

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
