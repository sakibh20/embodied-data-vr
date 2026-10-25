using TMPro;
using UnityEngine;

public class DataPoint : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    private int _index;
    private GraphData _graphData;

    // M67: the value label is sized in WORLD units, independent of the graph's
    // resize scale. Before, it was a child of the dot and inherited dot scale x graph
    // scale (0.1 x ~0.2 for a 30-point graph on a 3 m walk), so the text came out
    // ~1 cm tall and sat under the graph line -- unreadable in the headset.
    // GraphManager sets LabelHeight (m, height of one text line) from the spacing
    // between points before calling Show().
    public static float LabelHeight = 0.08f;
    const float LiftAboveFloor = 0.02f;   // m, above the line so it is never hidden
    const float GapFromDot = 0.02f;       // m, between the dot and the text start
    static readonly Color Background = new Color(0.08f, 0.08f, 0.08f, 1f);
    static Material _plateMat;
    private Transform _plate;
    const int LabelRenderQueue = 3600;    // after the line/grid/cue visuals

    private float _localLineHeight;       // text line height in label-local units
    private float _highSide = 1f;         // +1: label on the higher-value side of the dot, -1: lower

    public void Init(int index, GraphData graphData)
    {
        _index = index;
        _graphData = graphData;
    }

    public void Show()
    {
        if (label == null) return;
        string text = $"{_graphData.prefix} {(_index + 1) * _graphData.dataInterval} : {_graphData.values[_index]} {_graphData.unit}";
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        // Put the label on the side of the dot where the line is least in the way.
        // The text runs sideways (along the value axis), so a neighbour with a much
        // higher/lower value means a near-sideways segment on that side. Pick the
        // side whose segment is steeper (smaller value change); a peak's label goes
        // outside the peak, a valley's outside the valley.
        var vals = _graphData.values;
        float v = vals[_index];
        float highBlock = 0f, lowBlock = 0f;
        if (_index > 0) Block(vals[_index - 1] - v, ref highBlock, ref lowBlock);
        if (_index < vals.Count - 1) Block(vals[_index + 1] - v, ref highBlock, ref lowBlock);
        _highSide = highBlock < lowBlock ? 1f : -1f;
        label.GetComponent<Renderer>().sortingOrder = 100;
        label.fontMaterial.renderQueue = LabelRenderQueue;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.SetText(text);
        label.ForceMeshUpdate();
        _localLineHeight = Mathf.Max(0.001f, label.fontSize * 0.12f);
        if (_plate != null) _plate.gameObject.SetActive(false);   // rebuilt for the new text
        PlaceLabel();
    }

    // Solid dark plate behind the text so the white graph line never shows
    // through it. Sprites/Default is used because it is already in the build
    // (the graph lines use it).
    private void BuildPlate()
    {
        if (_plateMat == null)
        {
            _plateMat = new Material(Shader.Find("Sprites/Default")) { color = Background };
            _plateMat.renderQueue = LabelRenderQueue - 1;
        }
        if (_plate == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "LabelPlate";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _plateMat;
            r.sortingOrder = 99;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _plate = go.transform;
            _plate.SetParent(label.transform, false);
        }
        _plate.gameObject.SetActive(true);
        Bounds b = label.textBounds;
        float pad = label.fontSize * 0.04f;
        _plate.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
        _plate.localRotation = Quaternion.identity;
        _plate.localScale = new Vector3(b.size.x + 2f * pad, Mathf.Max(b.size.y, label.fontSize * 0.1f) + pad, 1f);
    }

    private static void Block(float delta, ref float high, ref float low)
    {
        if (delta > 0f) high = Mathf.Max(high, delta);
        else low = Mathf.Max(low, -delta);
    }

    /// <summary>
    /// Blanks the value label instead of showing it. Used for dots that have no
    /// matching GraphData value to display -- e.g. the retrace-review graph, whose
    /// points are resampled from the participant's own walked path rather than real
    /// dataset entries. See ROADMAP.md M44.
    /// </summary>
    public void HideLabel()
    {
        if (label != null) label.SetText(string.Empty);
        if (_plate != null) _plate.gameObject.SetActive(false);
        _localLineHeight = 0f;
    }

    // The graph is still being scaled/moved for a moment after Show() (alignment
    // tweens), so keep the label at its world size and position every frame.
    private void LateUpdate()
    {
        if (_localLineHeight > 0f) PlaceLabel();
    }

    private void PlaceLabel()
    {
        Transform t = label.transform;
        float parentScale = Mathf.Max(1e-6f, transform.lossyScale.x);
        t.localScale = Vector3.one * (LabelHeight / _localLineHeight / parentScale);
        float dotRadius = parentScale * 0.5f;
        // Which way along the text's reading direction the values increase.
        Transform g = transform.parent != null ? transform.parent : transform;
        float highIsRight = Vector3.Dot(g.up, t.right) >= 0f ? 1f : -1f;
        float side = _highSide * highIsRight;     // +1 right of the dot, -1 left
        bool right = side > 0f;
        var align = right ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        if (label.alignment != align || _plate == null || !_plate.gameObject.activeSelf)
        {
            label.alignment = align;
            label.rectTransform.pivot = new Vector2(right ? 0f : 1f, 0.5f);
            label.ForceMeshUpdate();
            BuildPlate();
        }
        t.position = transform.position + Vector3.up * LiftAboveFloor
                   + t.right * (side * (dotRadius + GapFromDot));
    }
}
