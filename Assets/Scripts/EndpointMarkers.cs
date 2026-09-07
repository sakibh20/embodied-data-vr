using TMPro;
using UnityEngine;

/// <summary>
/// Floor markers -- a coloured rectangle outline plus a "Start"/"End" label lying
/// flat on the ground -- placed at the two ends of the current trial's walk corridor
/// once GraphManager finishes generating/aligning its graph (see
/// GraphManager.ConfigureSampler/PlaceEndpointMarkers). The rectangle is sized to
/// match the actual walkable zone (PathSampler's own regionHalfWidth/regionMargin),
/// not an arbitrary constant, and is centred on the corridor's real centreline (the
/// anchor points GraphManager passes in are PathSampler's own d=0/d=TotalLength
/// points) rather than the graph's value=0 baseline -- see ROADMAP.md M40 for why
/// that distinction matters. Pure world-space geometry (a closed LineRenderer loop +
/// a 3D TextMeshPro object, both lying flat on the floor): nothing here is
/// screen-space UI, so it renders identically on desktop, the simulator, and a real
/// headset with no VR-specific handling needed. Markers persist across
/// Walk/Distractor/Retrace/TrialComplete (they are also what the participant walks
/// back to before the next trial can start -- see SessionController.
/// CheckReadyToStartNext) and are cleared/replaced the moment the next trial's graph
/// starts generating (GraphManager.ResetGraph).
/// </summary>
public class EndpointMarkers : MonoBehaviour
{
    [Header("Rectangle")]
    [SerializeField] private float lineWidth = 0.04f;
    [SerializeField] private float floorOffset = 0.01f;   // avoid z-fighting with the grid/ground line

    [Header("Label")]
    [SerializeField] private float labelWorldSize = 0.35f;   // roughly the rendered text height, in metres

    [SerializeField] private Color startColor = new Color(0.25f, 0.9f, 0.4f);
    [SerializeField] private Color endColor = new Color(0.95f, 0.3f, 0.3f);

    private GameObject _startMarker;
    private GameObject _endMarker;

    /// <summary>
    /// (Re)places the Start/End markers.
    /// </summary>
    /// <param name="startZoneEdge">World position of the "feedback zone"'s near
    /// boundary along the walk (PathSampler's d = -regionMargin point) -- NOT dot 0's
    /// own position; see GraphManager.PlaceEndpointMarkers for why.</param>
    /// <param name="endZoneEdge">Same, for the zone's far boundary (PathSampler's
    /// d = TotalLength + regionMargin point).</param>
    /// <param name="walkDir">Ground-projected walk direction, used to orient both the
    /// rectangle (long/short axes) and the label (reading direction).</param>
    /// <param name="halfWidth">Half the rectangle's width, perpendicular to walkDir --
    /// pass PathSampler.RegionHalfWidth so the marker matches the real walkable zone.</param>
    /// <param name="halfDepth">Half the rectangle's depth, along walkDir.</param>
    public void Show(Vector3 startZoneEdge, Vector3 endZoneEdge, Vector3 walkDir, float halfWidth, float halfDepth)
    {
        Clear();

        // Centre each rectangle OUTSIDE the zone boundary rather than on it: pushing
        // the centre a further halfDepth away from the corridor means the rectangle's
        // near edge lands exactly on *ZoneEdge, and the whole marker sits beyond the
        // real feedback zone instead of straddling its boundary. See ROADMAP.md M41.
        Vector3 startCentre = startZoneEdge - walkDir * halfDepth;
        Vector3 endCentre = endZoneEdge + walkDir * halfDepth;

        _startMarker = BuildMarker("StartMarker", startCentre, walkDir, "Start", startColor, halfWidth, halfDepth);
        _endMarker = BuildMarker("EndMarker", endCentre, walkDir, "End", endColor, halfWidth, halfDepth);
    }

    /// <summary>Removes any markers currently placed. Safe to call when none exist.</summary>
    public void Clear()
    {
        if (_startMarker != null) Destroy(_startMarker);
        if (_endMarker != null) Destroy(_endMarker);
        _startMarker = null;
        _endMarker = null;
    }

    private GameObject BuildMarker(string name, Vector3 anchor, Vector3 walkDir, string text, Color color,
                                    float halfWidth, float halfDepth)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, true);
        root.transform.position = anchor + Vector3.up * floorOffset;
        root.transform.rotation = Quaternion.identity;

        BuildRectangle(root.transform, walkDir, color, halfWidth, halfDepth);
        BuildLabel(root.transform, walkDir, text, color);

        return root;
    }

    // A rectangle outline aligned to the corridor: `halfWidth` perpendicular to the
    // walk (matching the walkable zone's real width) and `halfDepth` along it
    // (matching the "still on the walk" margin), centred on `parent`'s position.
    private void BuildRectangle(Transform parent, Vector3 walkDir, Color color, float halfWidth, float halfDepth)
    {
        Vector3 fwd = walkDir;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        var rectObj = new GameObject("Rectangle");
        rectObj.transform.SetParent(parent, false);

        var lr = rectObj.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.widthMultiplier = lineWidth;
        lr.positionCount = 4;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Local positions here are offsets from `parent` (identity rotation), so
        // right/fwd (world-ish, ground-projected) double directly as the rectangle's
        // local axes -- it stays aligned to the corridor regardless of world orientation.
        Vector3 rOff = right * halfWidth;
        Vector3 fOff = fwd * halfDepth;
        lr.SetPosition(0, -rOff - fOff);
        lr.SetPosition(1, rOff - fOff);
        lr.SetPosition(2, rOff + fOff);
        lr.SetPosition(3, -rOff + fOff);
    }

    // Lies the label flat (facing up) so it reads correctly from directly above --
    // rotated so its "up" (reading direction) points along the walk, i.e. the way a
    // participant standing on the marker and facing down the corridor (entering at
    // Start / having just arrived at End) would read it, matching Unity's world Z
    // walk axis in this scene.
    private void BuildLabel(Transform parent, Vector3 walkDir, string text, Color color)
    {
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent, false);

        float yaw = Mathf.Atan2(walkDir.x, walkDir.z) * Mathf.Rad2Deg;
        labelObj.transform.localRotation = Quaternion.Euler(90f, yaw, 0f);

        var tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 8f;
        // TMP's 3D fontSize doesn't map 1:1 to metres -- scale the object instead so
        // labelWorldSize is (roughly) the rendered text height regardless of font.
        float scale = labelWorldSize / 1.4f;
        labelObj.transform.localScale = Vector3.one * scale;
    }
}
