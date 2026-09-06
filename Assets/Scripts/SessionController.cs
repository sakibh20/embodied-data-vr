using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates a full study session: builds a counterbalanced list of trials
/// (condition × dataset), then drives each trial through the phases
/// Walk → Distractor → Retrace → Recall, and writes a JSON log per participant.
///
/// The in-VR UI (built separately) calls the public API — StartSession, EndWalk,
/// EndDistractor, EndRetrace, AnswerCurrentQuestion — and reads Phase / the current
/// question / the distractor number to drive its panels. Optional debug keys let
/// the whole flow be exercised on desktop without the UI.
/// </summary>
public class SessionController : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private GraphManager graphManager;
    [SerializeField] private ConditionManager conditionManager;
    [SerializeField] private Transform player;   // head/camera, for retrace capture

    [Header("Design")]
    [SerializeField] private List<GraphData> datasetPool = new List<GraphData>();
    [SerializeField] private int participantId = 1;
    [Tooltip("1 or 2 per the plan (2 where session length allows).")]
    [SerializeField, Range(1, 2)] private int trialsPerCondition = 1;

    [Header("Retrace capture")]
    [SerializeField] private float retraceSampleInterval = 0.1f;

    [Header("Debug")]
    [Tooltip("Enter = advance phase; number keys answer during Recall. For desktop " +
             "testing without the VR UI. Disable ExperimentController's keys to avoid clashes.")]
    [SerializeField] private bool debugKeys = false;

    // ---- runtime state ----
    private readonly List<TrialSpec> _trials = new List<TrialSpec>();
    private SessionLog _log;
    private TrialResult _result;
    private int _index = -1;

    private SessionPhase _phase = SessionPhase.Idle;
    private float _phaseStart;
    private float _retraceTimer;
    private int _questionIndex;

    private static readonly ConditionId[] AllConditions =
    {
        ConditionId.Control, ConditionId.Abstract, ConditionId.Representative,
        ConditionId.SemanticAudio, ConditionId.SemanticVisual
    };

    /// <summary>Raised whenever the phase changes, so UI can refresh.</summary>
    public event Action<SessionPhase> PhaseChanged;

private void Awake()
    {
        if (graphManager == null) graphManager = FindAnyObjectByType<GraphManager>();
        if (conditionManager == null) conditionManager = FindAnyObjectByType<ConditionManager>();
        // 'player' is no longer resolved/cached here -- see Update, which reads
        // PlayerRig.Head live every frame (falling back to ResolvePlayerHead only
        // if nothing set it, e.g. no SessionBootstrap in the scene).
    }

    // Resolve the transform whose position is captured during retrace.
    // In VR (an XR device is present/active) this is the head camera — the XR rig's
    // Main Camera is tagged MainCamera, so Camera.main returns it. On desktop (no
    // device) we fall back to the serialized ref / DesktopWalker. This means the same
    // scene works in both modes with no manual re-wiring of the 'player' field.
    private Transform ResolvePlayerHead()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive && Camera.main != null)
            return Camera.main.transform;                     // VR head

        if (player != null && player.gameObject.activeInHierarchy)
            return player;                                    // explicit serialized ref (desktop)

        var walker = FindAnyObjectByType<DesktopWalker>();
        if (walker != null) return walker.transform;

        return Camera.main != null ? Camera.main.transform
             : (FindAnyObjectByType<Camera>() is Camera c ? c.transform : null);
    }

    // ---- public read state for the UI ----
    public SessionPhase Phase => _phase;
    public int TrialNumber => _index + 1;               // 1-based
    public int TotalTrials => _trials.Count;
    public int DistractorStartNumber => _result?.distractorStartNumber ?? 0;
    public RecallQuestion CurrentQuestion =>
        (_phase == SessionPhase.Recall && _result != null && _questionIndex < _result.questions.Count)
            ? _result.questions[_questionIndex] : null;
    public int QuestionNumber => _questionIndex + 1;
    public int QuestionCount => _result?.questions.Count ?? 0;

    // =====================================================================
    // Session lifecycle
    // =====================================================================

    [ContextMenu("Start Session")]
    public void StartSession()
    {
        if (graphManager == null || conditionManager == null)
        {
            Debug.LogError("[Session] Missing GraphManager/ConditionManager reference.");
            return;
        }
        if (datasetPool.Count == 0)
        {
            Debug.LogError("[Session] datasetPool is empty.");
            return;
        }

        BuildTrials();
        _log = new SessionLog
        {
            participantId = participantId,
            trialsPerCondition = trialsPerCondition,
            startedUtc = DateTime.UtcNow.ToString("o")
        };
        _index = -1;
        Debug.Log($"[Session] Participant {participantId}: {_trials.Count} trials.");
        NextTrial();
    }

    // Cyclic Latin-square counterbalancing of the 5 conditions, rotated by participant.
    // Each condition repeats trialsPerCondition times (one per block).
    private void BuildTrials()
    {
        _trials.Clear();
        int n = AllConditions.Length;
        int startRow = (participantId - 1) % n;
        int startDs = datasetPool.Count > 0 ? (participantId - 1) % datasetPool.Count : 0;
        int order = 0;

        for (int block = 0; block < trialsPerCondition; block++)
        {
            for (int k = 0; k < n; k++)
            {
                var cond = AllConditions[(startRow + block + k) % n];
                var ds = datasetPool[(startDs + order) % datasetPool.Count];
                _trials.Add(new TrialSpec
                {
                    condition = cond,
                    dataset = ds,
                    orderIndex = order,
                    trialInCondition = block
                });
                order++;
            }
        }

        if (datasetPool.Count < _trials.Count)
            Debug.LogWarning($"[Session] Only {datasetPool.Count} datasets for {_trials.Count} " +
                             "trials — some will repeat within the session. Add more datasets.");
    }

    private void NextTrial()
    {
        _index++;
        if (_index >= _trials.Count)
        {
            FinishSession();
            return;
        }

        TrialSpec spec = _trials[_index];
        _result = new TrialResult
        {
            orderIndex = spec.orderIndex,
            condition = spec.condition.ToString(),
            datasetName = spec.dataset != null ? spec.dataset.name : "(none)",
            trialInCondition = spec.trialInCondition,
            distractorStartNumber = UnityEngine.Random.Range(300, 999),
            questions = BuildQuestions(spec.dataset, spec.orderIndex)
        };

        // Set up and show the graph for this trial's dataset and condition.
        graphManager.SetData(spec.dataset);
        graphManager.GenerateGraph();
        graphManager.SetGraphVisible(true);
        conditionManager.SetCondition(spec.condition);

        SetPhase(SessionPhase.Walk);
    }

    private void FinishSession()
    {
        _phase = SessionPhase.Complete;
        _log.finishedUtc = DateTime.UtcNow.ToString("o");
        WriteLog();
        PhaseChanged?.Invoke(_phase);
        Debug.Log("[Session] Complete.");
    }

    // =====================================================================
    // Phase transitions (called by the UI or debug keys)
    // =====================================================================

    public void EndWalk()
    {
        if (_phase != SessionPhase.Walk) return;
        _result.walkSeconds = Time.time - _phaseStart;
        SetPhase(SessionPhase.Distractor);
    }

    public void EndDistractor()
    {
        if (_phase != SessionPhase.Distractor) return;
        _result.distractorSeconds = Time.time - _phaseStart;

        // Retrace: hide the line/dots and remove cues (Control) so it's pure memory.
        graphManager.SetGraphVisible(false);
        conditionManager.SetCondition(ConditionId.Control);
        _result.retracePath.Clear();
        _retraceTimer = 0f;
        SetPhase(SessionPhase.Retrace);
    }

    public void EndRetrace()
    {
        if (_phase != SessionPhase.Retrace) return;
        _result.retraceSeconds = Time.time - _phaseStart;
        _questionIndex = 0;
        SetPhase(SessionPhase.Recall);
    }

    /// <summary>Answer the current recall question; advances to the next, or ends the trial.</summary>
    public void AnswerCurrentQuestion(int optionIndex)
    {
        if (_phase != SessionPhase.Recall) return;
        var q = CurrentQuestion;
        if (q == null) return;

        q.answeredIndex = optionIndex;
        _questionIndex++;

        if (_questionIndex >= _result.questions.Count)
        {
            _log.trials.Add(_result);
            NextTrial();
        }
        else
        {
            PhaseChanged?.Invoke(_phase);   // same phase, next question
        }
    }

    private void SetPhase(SessionPhase phase)
    {
        _phase = phase;
        _phaseStart = Time.time;
        PhaseChanged?.Invoke(phase);
        Debug.Log($"[Session] Trial {TrialNumber}/{TotalTrials} → {phase}");
    }

    // =====================================================================
    // Recall questions
    // =====================================================================

    private List<RecallQuestion> BuildQuestions(GraphData data, int orderIndex)
    {
        var qs = new List<RecallQuestion>();
        if (data == null || data.values == null || data.values.Count == 0) return qs;

        float max = float.MinValue, min = float.MaxValue;
        foreach (var v in data.values) { if (v > max) max = v; if (v < min) min = v; }
        float last = data.values[data.values.Count - 1];
        float range = max - min;

        qs.Add(MakeNumericQuestion("What was the highest value you encountered?", max, data.unit, range, orderIndex, 0));
        qs.Add(MakeNumericQuestion("What was the lowest value you encountered?", min, data.unit, range, orderIndex, 1));
        qs.Add(MakeNumericQuestion("What was the value at the end of the walk?", last, data.unit, range, orderIndex, 2));
        qs.Add(MakeNumericQuestion("What was the difference between the highest and lowest points?",
                                   range, data.unit, range, orderIndex, 3));
        return qs;
    }

    // Build a 4-option multiple-choice question around a numeric answer.
    // The correct answer's slot is randomized with a seed derived from participant +
    // trial order + question index, so placement is unpredictable to the participant
    // yet fully reproducible for analysis. Distractors are offset by multiples of a
    // step scaled to the dataset's value range, and are guaranteed distinct from each
    // other and the answer (by display value) and non-negative.
    private RecallQuestion MakeNumericQuestion(
        string prompt, float answer, string unit, float range, int orderIndex, int qIndex)
    {
        float step = Mathf.Max(1f, Mathf.Round(range * 0.15f));

        // Collect 3 distinct, non-negative distractors, comparing on the displayed value.
        var distractors = new List<float>();
        var usedDisplays = new HashSet<string> { Display(answer) };
        int[] mults = { 1, -1, 2, -2, 3, -3, 4, -4 };
        foreach (int m in mults)
        {
            if (distractors.Count == 3) break;
            float v = answer + m * step;
            if (v < 0f) continue;
            string disp = Display(v);
            if (usedDisplays.Add(disp)) distractors.Add(v);
        }
        // Safety net for tiny ranges: keep stepping up until we have 3.
        int extra = 5;
        while (distractors.Count < 3)
        {
            float v = answer + extra * step;
            if (usedDisplays.Add(Display(v))) distractors.Add(v);
            extra++;
        }

        // Reproducible per participant/trial/question RNG for answer placement.
        int seed = participantId * 73856093 ^ (orderIndex + 1) * 19349663 ^ (qIndex + 1) * 83492791;
        var rng = new System.Random(seed);
        int slot = rng.Next(4);

        string[] options = new string[4];
        int di = 0;
        for (int i = 0; i < 4; i++)
            options[i] = (i == slot) ? $"{Display(answer)} {unit}" : $"{Display(distractors[di++])} {unit}";

        return new RecallQuestion { prompt = prompt, options = options, correctIndex = slot };
    }

    // Displayed numeric token for an option value (matches option formatting).
    private static string Display(float v) => Mathf.Max(0f, v).ToString("0.#");

    // =====================================================================
    // Update: retrace capture + debug keys
    // =====================================================================

private void Update()
    {
        Transform head = PlayerRig.Head != null ? PlayerRig.Head : ResolvePlayerHead();

        if (_phase == SessionPhase.Retrace && head != null)
        {
            _retraceTimer -= Time.deltaTime;
            if (_retraceTimer <= 0f)
            {
                _retraceTimer = retraceSampleInterval;
                Vector3 p = head.position;
                _result.retracePath.Add(new RetraceSample
                {
                    t = Time.time - _phaseStart,
                    x = p.x,
                    z = p.z
                });
            }
        }

        if (debugKeys) HandleDebugKeys();
    }

    private void HandleDebugKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            switch (_phase)
            {
                case SessionPhase.Idle:
                case SessionPhase.Complete: StartSession(); break;
                case SessionPhase.Walk: EndWalk(); break;
                case SessionPhase.Distractor: EndDistractor(); break;
                case SessionPhase.Retrace: EndRetrace(); break;
            }
        }

        if (_phase == SessionPhase.Recall)
        {
            if (kb.digit1Key.wasPressedThisFrame) AnswerCurrentQuestion(0);
            else if (kb.digit2Key.wasPressedThisFrame) AnswerCurrentQuestion(1);
            else if (kb.digit3Key.wasPressedThisFrame) AnswerCurrentQuestion(2);
            else if (kb.digit4Key.wasPressedThisFrame) AnswerCurrentQuestion(3);
        }
    }

    // =====================================================================
    // Logging
    // =====================================================================

    private void WriteLog()
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "StudyData");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string file = Path.Combine(dir, $"participant_{participantId:00}_{stamp}.json");
            File.WriteAllText(file, JsonUtility.ToJson(_log, true));
            Debug.Log($"[Session] Log written: {file}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] Failed to write log: {e.Message}");
        }
    }
}
