using UnityEngine;

public class GridGenerator : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private GraphData graphData;
    [SerializeField] private GraphSettings graphSettings;

    [Header("Line Settings")]
    [SerializeField] private float lineWidth = 0.02f;
    [SerializeField] private Color lineColor = Color.gray;
    [SerializeField] private float lineAlpha = 0.5f;

    [Tooltip("Extra padding (local units) added beyond the data's value range at each " +
             "end of the per-point cross lines, so the grid always over-spans the graph.")]
    [SerializeField] private float laneHalfWidth = 0.3f;

    [Header("References")]
    [SerializeField] private Transform gridRoot;

    private Material _lineMaterial;

    public void GenerateGrid(GraphData data, GraphSettings settings, Transform alignmentParent = null)
    {
        graphData = data;
        graphSettings = settings;

        // Parent the grid under the same root as the graph so it inherits the
        // graph's alignment (the rotate + flip in GraphManager.StartAlignment).
        // Without this the grid stays in the graph's initial, un-rotated pose.
        if (alignmentParent != null)
            AlignToGraph(alignmentParent);

        GenerateGridInternal();
    }

    private void AlignToGraph(Transform alignmentParent)
    {
        if (gridRoot == null)
            gridRoot = transform;

        // Match the graph's local space exactly: identity local TRS under graphRoot.
        gridRoot.SetParent(alignmentParent, worldPositionStays: false);
        gridRoot.localPosition = Vector3.zero;
        gridRoot.localRotation = Quaternion.identity;
        gridRoot.localScale = Vector3.one;
    }

    [ContextMenu("Generate Grid")]
    public void GenerateGrid()
    {
        if (graphData == null || graphSettings == null)
        {
            Debug.LogWarning("GraphData or GraphSettings is not assigned!");
            return;
        }
        GenerateGridInternal();
    }

    private void GenerateGridInternal()
    {
        if (gridRoot == null)
            gridRoot = transform;

        // Clear existing grid
        ClearGrid();

        if (graphData.values.Count == 0)
            return;

        // Create material for lines
        _lineMaterial = new Material(Shader.Find("Sprites/Default"));
        _lineMaterial.color = new Color(lineColor.r, lineColor.g, lineColor.b, lineAlpha);

        // Size the cross-lines to the graph's actual value spread so they always
        // span the whole graph (value maps to local Y = value * heightScale).
        int n = graphData.values.Count;
        float hs = graphSettings.heightScale;
        float minV = float.MaxValue, maxV = float.MinValue;
        foreach (var v in graphData.values) { if (v < minV) minV = v; if (v > maxV) maxV = v; }

        float yLo = Mathf.Min(0f, minV * hs) - laneHalfWidth;   // laneHalfWidth = padding
        float yHi = Mathf.Max(0f, maxV * hs) + laneHalfWidth;
        float walkLen = Mathf.Max(0, n - 1) * graphSettings.spacing;

        // Cross line at each data point, spanning the value axis (local Y). Lies in the
        // graph's plane and flips flat + scales with it (grid is a child of graphRoot).
        for (int i = 0; i < n; i++)
        {
            float xPos = i * graphSettings.spacing;
            CreateLine(new Vector3(xPos, yLo, 0f), new Vector3(xPos, yHi, 0f));
        }

        // Baseline walk line along the X (time) axis.
        CreateLine(new Vector3(0f, 0f, 0f), new Vector3(walkLen, 0f, 0f));

        Debug.Log($"Grid generated: {n} points, spacing {graphSettings.spacing}, " +
                  $"walk {walkLen:0.##}, value span [{yLo:0.##},{yHi:0.##}]");
    }

    private void CreateLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("GridLine");
        lineObj.transform.SetParent(gridRoot);
        lineObj.transform.localPosition = Vector3.zero;

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = _lineMaterial;
        lr.widthMultiplier = lineWidth;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.useWorldSpace = false;
    }

    private void ClearGrid()
    {
        // Destroy all child GameObjects (grid lines)
        while (gridRoot.childCount > 0)
        {
            DestroyImmediate(gridRoot.GetChild(0).gameObject);
        }
    }
}
