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

    [Header("Room area (centre + walk direction only)")]
    [Tooltip("Shared room area. The graph is centred on it and oriented along its " +
             "walk axis. Its SIZE does NOT scale the graph — it only bounds the cue " +
             "field. Auto-found if empty.")]
    [SerializeField] private ExperimentArea area;

    [Header("Graph size (independent of the area)")]
    [Tooltip("Scale the finished graph so its walk spans this many metres. Uniform " +
             "scale, so the data's shape is preserved and walk/retrace values stay correct.")]
    [SerializeField] private bool resizeGraph = true;
    [SerializeField] private float targetWalkLength = 3f;
    [Tooltip("Seconds to tween the resize + centring after the graph lies flat.")]
    [SerializeField] private float scaleTweenDuration = 0.6f;

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

        // Nothing else marks the sampler stale for the NEW graph: without this, a
        // participant's cues (WalkValueDriver gates only on PathSampler.IsReady) and
        // SessionController.CheckAutoEndOfPhase could still react against the PREVIOUS
        // trial's geometry for the ~2s the graph spends flipping/resizing into place
        // below (align + resize run AFTER _isGenerating used to go false -- see the
        // FinishGeneration note) -- confirmed as the cause of feedback sometimes
        // starting before the graph had visually settled. See ROADMAP.md M36.
        if (pathSampler != null) pathSampler.Clear();

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
        graphRoot.localScale = Vector3.one;   // clear any fit-to-area scale from a prior gen
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

        if (area == null) area = FindAnyObjectByType<ExperimentArea>();

        // Pivot the graph on the area centre so the flip/rotation swings around it
        // and the final graph shares the cue field's centre.
        if (area != null) graphRoot.position = area.Center;

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

            // _isGenerating no longer clears here: StartAlignment() (called just above,
            // via AppendCallback) kicks off its own independent rotate/resize sequence
            // that is NOT joined to this one, so this OnComplete fired ~2s before the
            // graph was actually done flipping/resizing/re-centring -- FinishGeneration()
            // (called once alignment + resize + ConfigureSampler all genuinely finish)
            // is what clears it now. See ROADMAP.md M36.
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
        // Orient to the room area when present (fixed layout), else to the player.
        Vector3 forward = area != null ? area.Forward
                        : (playerCamera != null ? playerCamera.forward : Vector3.forward);
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

        // Once flat, resize the graph to the requested walk length and centre it on
        // the area — tweened, not an instant pop — then configure the sampler.
        alignSeq.OnComplete(ApplyGraphSizeAndCentre);
    }

    // Uniformly scale the whole graph (dots + line + grid — all children of graphRoot)
    // so its walk spans targetWalkLength, and move it so the dots' centre sits on the
    // area centre. Uniform scale keeps the data's proportions, so the value read while
    // walking/retracing is unchanged (PathSampler is reconfigured from final positions).
    private void ApplyGraphSizeAndCentre()
    {
        if (_spawnedDots.Count == 0) { FinishGeneration(); return; }

        // Local bounds of the dots. Walk axis is local X (before the flat-lay flip).
        Bounds lb = new Bounds(_spawnedDots[0].transform.localPosition, Vector3.zero);
        foreach (var d in _spawnedDots) lb.Encapsulate(d.transform.localPosition);
        Vector3 localCentre = lb.center;
        float walkExtentLocal = Mathf.Max(0.001f, lb.size.x);

        float s = 1f;
        if (resizeGraph) s = targetWalkLength / walkExtentLocal;

        // Solve for the root position so the dots' centre lands on the area centre
        // after scaling by s about the (rotated) root: worldCentre = Pf + R*(s*localCentre).
        Vector3 centreTarget = area != null ? area.Center : graphRoot.position;
        Quaternion R = graphRoot.rotation;
        Vector3 targetPos = centreTarget - R * (s * localCentre);

        graphRoot.DOKill();
        Sequence resize = DOTween.Sequence();
        resize.Join(graphRoot.DOScale(Vector3.one * s, scaleTweenDuration).SetEase(Ease.OutCubic));
        resize.Join(graphRoot.DOMove(targetPos, scaleTweenDuration).SetEase(Ease.OutCubic));
        resize.OnComplete(FinishGeneration);
    }

    // The graph is only truly "settled" once it has (re)pointed the sampler at its
    // final, post-resize geometry -- so IsGenerating now stays true until here, not
    // until the earlier dot-spawn sequence finishes (that left ~2s of alignment/resize
    // tweening still running with IsGenerating already false). See ROADMAP.md M36.
    private void FinishGeneration()
    {
        ConfigureSampler();
        _isGenerating = false;
    }

    // Point the sampler at the graph's final geometry. Reads the segment length from
    // the actual (scaled) dot spacing, so the value mapping is correct at any scale.
private void ConfigureSampler()
    {
        if (pathSampler == null || graphData == null || _spawnedDots.Count == 0) return;

        Vector3 start = _spawnedDots[0].transform.position;
        Vector3 walkDir = area != null ? area.Forward : graphRoot.right;

        // Segment = projection of the dot spacing onto the walk axis, NOT raw
        // Euclidean distance between adjacent dots. Value maps to LATERAL floor
        // offset (see PROJECT_DESCRIPTION Sec.1), so adjacent dots also differ in
        // that lateral direction whenever the data value changes -- Vector3.Distance
        // between them is inflated by that lateral offset and is NOT the true
        // per-point spacing along the walk. That inflated segment made PathSampler's
        // TotalLength (and therefore both the value mapping and the on-walk region
        // check) wrong -- values lagged behind the participant's real position, and
        // the activation corridor extended well past the visible graph. Using the
        // start->last span projected onto the walk axis, divided evenly across all
        // points, is also more robust than reading a single dot0/dot1 pair.
        float segment = settings.spacing;
        if (_spawnedDots.Count > 1)
        {
            Vector3 last = _spawnedDots[_spawnedDots.Count - 1].transform.position;
            float span = Vector3.Dot(last - start, walkDir);
            segment = span / (_spawnedDots.Count - 1);
        }

        pathSampler.Configure(start, walkDir, segment, graphData.values);
    }
}