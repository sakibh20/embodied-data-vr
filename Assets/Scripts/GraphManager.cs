using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

public class GraphManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DataPoint dataPoint;
    [SerializeField] private Transform graphRoot;
    [SerializeField] private Transform playerCamera;
    [SerializeField] private GridGenerator gridGenerator;
    [SerializeField] private PathSampler pathSampler;

    [Header("Data")]
    [SerializeField] private GraphData graphData;

    [Header("Settings")]
    [SerializeField] private GraphSettings settings;

    private readonly List<DataPoint> _spawnedDots = new List<DataPoint>();
    private LineRenderer _lineRenderer;
    private LineRenderer _groundLineRenderer;

    private bool _isGenerating = false;
    private Sequence _generationSequence;

    // private void Start()
    // {
    //     GenerateGraph();
    // }

    public bool IsGenerating => _isGenerating;
    public GraphData Data => graphData;

    /// <summary>Swap the dataset to render on the next GenerateGraph (used per trial).</summary>
    public void SetData(GraphData data) => graphData = data;

    /// <summary>
    /// Show/hide the line + dots (and their value tags) for the retrace phase.
    /// The grid stays visible so it can still guide walk spacing.
    /// </summary>
    public void SetGraphVisible(bool visible)
    {
        if (_lineRenderer != null) _lineRenderer.enabled = visible;
        if (_groundLineRenderer != null) _groundLineRenderer.enabled = visible;
        foreach (var dot in _spawnedDots)
            if (dot != null) dot.gameObject.SetActive(visible);
    }

    // PUBLIC ENTRY POINT
    [ContextMenu("GenerateGraph")]
    public void GenerateGraph()
    {
        if (_isGenerating)
        {
            Debug.LogWarning("Graph generation already in progress!");
            return;
        }

        _isGenerating = true;

        ResetGraph();
        Generate();
    }

    // RESET BEFORE REGEN
    private void ResetGraph()
    {
        foreach (var dot in _spawnedDots)
        {
            if (dot != null) Destroy(dot.gameObject);
        }
        _spawnedDots.Clear();

        if (_lineRenderer != null)
        {
            Destroy(_lineRenderer.gameObject);
        }

        graphRoot.rotation = Quaternion.identity;
    }

    // CORE GENERATION
    public void Generate()
    {
        if (graphData == null || graphData.values.Count == 0)
        {
            Debug.LogWarning("No graph data assigned!");
            _isGenerating = false;
            return;
        }

        _generationSequence = DOTween.Sequence();

        // Create LineRenderer
        GameObject lineObj = new GameObject("Line");
        lineObj.transform.SetParent(graphRoot);
        _lineRenderer = lineObj.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = false;
        _lineRenderer.widthMultiplier = 0.05f;
        _lineRenderer.positionCount = 0;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        
        GameObject groundLineObj = new GameObject("GroundLine");
        groundLineObj.transform.SetParent(graphRoot);
        _groundLineRenderer = groundLineObj.AddComponent<LineRenderer>();
        
        _groundLineRenderer.startColor = Color.gray;
        _groundLineRenderer.endColor = Color.gray;

        _groundLineRenderer.useWorldSpace = false;
        _groundLineRenderer.widthMultiplier = 0.03f;
        _groundLineRenderer.positionCount = 0;
        _groundLineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        Vector3[] positions = new Vector3[graphData.values.Count];

        for (int i = 0; i < graphData.values.Count; i++)
        {
            float value = graphData.values[i];

            Vector3 localPos = new Vector3(i * settings.spacing, value * settings.heightScale, 0);

            positions[i] = localPos;

            float delay = i * settings.delayBetweenPoints;

            // DOT animation
            _generationSequence.Insert(delay, CreateDotTween(localPos, i));

            // LINE animation (slight offset after dot)
            _generationSequence.Insert(delay + 0.1f, CreateLineTween(localPos));
            _generationSequence.Insert(delay + 0.1f, CreateGroundLineTween(localPos));
        }

        // After everything → ALIGNMENT
        _generationSequence.AppendInterval(0.2f);
        _generationSequence.AppendCallback(() => StartAlignment());

        _generationSequence.OnComplete(() =>
        {
            foreach (DataPoint point in _spawnedDots)
            {
                point.Show();
            }

            // Generate grid after graph is complete
            if (gridGenerator != null)
            {
                // Pass graphRoot so the grid shares the graph's alignment (rotate + flip).
                gridGenerator.GenerateGrid(graphData, settings, graphRoot);
            }

            _isGenerating = false;
        });
    }
    
    private Tween CreateDotTween(Vector3 localPos, int index)
    {
        //GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DataPoint dot = Instantiate(dataPoint, graphRoot);
        dot.Init(index, graphData);
        //dot.transform.SetParent(graphRoot);
        dot.transform.localPosition = localPos;
        dot.transform.localScale = Vector3.zero;

        _spawnedDots.Add(dot);

        return dot.transform.DOScale(settings.dotSize, settings.spawnDuration).SetEase(Ease.OutBack);
    }
    
    private Tween CreateLineTween(Vector3 localPos)
    {
        return DOVirtual.DelayedCall(0, () =>
        {
            _lineRenderer.positionCount++;
            _lineRenderer.SetPosition(_lineRenderer.positionCount - 1, localPos);
        });
    }
    
    private Tween CreateGroundLineTween(Vector3 localPos)
    {
        Vector3 groundPos = new Vector3(localPos.x, 0, 0);

        return DOVirtual.DelayedCall(0, () =>
        {
            _groundLineRenderer.positionCount++;
            _groundLineRenderer.SetPosition(_groundLineRenderer.positionCount - 1, groundPos);
        });
    }

    private void StartAlignment()
    {
        Vector3 forward = playerCamera.forward;
        forward.y = 0;

        Quaternion alignToForward = Quaternion.LookRotation(forward) * Quaternion.Euler(0, -90f, 0);

        Sequence alignSeq = DOTween.Sequence();

        // STEP 1
        alignSeq.Append(
            graphRoot.DORotateQuaternion(alignToForward, 0.8f)
                .SetEase(Ease.OutCubic)
        );

        // STEP 2 (relative rotation, no euler)
        alignSeq.Append(
            graphRoot.DORotateQuaternion(
                alignToForward * Quaternion.Euler(90f, 0f, 0f),
                0.8f
            ).SetEase(Ease.InOutSine)
        );

        // Once aligned, the path's final world orientation is known: local +X (time
        // axis) maps to graphRoot.right. Configure the sampler so the walk drives cues.
        alignSeq.OnComplete(() =>
        {
            if (pathSampler != null && graphData != null)
                pathSampler.Configure(graphRoot.position, graphRoot.right, settings.spacing, graphData.values);
        });
    }
}