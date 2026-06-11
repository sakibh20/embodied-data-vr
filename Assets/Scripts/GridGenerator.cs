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

    [Tooltip("Half-length of each per-point cross line, along the graph's value axis. " +
             "Lines are authored in the graph's local XY plane so they lie flat on the " +
             "ground after GraphManager's alignment flip.")]
    [SerializeField] private float laneHalfWidth = 1f;

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

        // Calculate grid dimensions based on graph data
        int gridWidth = graphData.values.Count;
        float totalWidth = gridWidth * graphSettings.spacing;

        // Cross line at each data point, spanning the local Y (value) axis so it lies
        // in the same plane as the graph and flips flat onto the ground with it.
        for (int x = 0; x <= gridWidth; x++)
        {
            float xPos = x * graphSettings.spacing;
            CreateLine(
                new Vector3(xPos, -laneHalfWidth, 0f),
                new Vector3(xPos, laneHalfWidth, 0f)
            );
        }

        // Center walk line along the X (time) axis.
        CreateLine(
            new Vector3(0, 0, 0),
            new Vector3(totalWidth, 0, 0)
        );

        Debug.Log($"Grid generated: {gridWidth} data points, spacing {graphSettings.spacing}, total width {totalWidth}");
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
