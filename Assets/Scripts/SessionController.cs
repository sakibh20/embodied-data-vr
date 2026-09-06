using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates a full study session: a counterbalanced list of trials (condition x
/// dataset), each driven through the phases Walk -> Distractor -> Retrace -> Recall.
/// Data is written as CSV, matching the schema in the sources-folder data notes doc --
/// trials.csv (one row per trial), value_questions.csv (one row per recall question),
/// and retracing_log.csv (one row per retrace sample) -- all appended into a shared,
/// growing file per study rather than one file per participant, and written
/// incrementally (retrace rows at end of Retrace, a question row the moment it's
/// answered, a trial row the moment its last recall question is answered) so a crash
/// mid-session loses at most the current in-progress trial, not the whole participant.
///
/// The in-VR UI (built separately) calls the public API -- StartSession, EndWalk,
/// EndDistractor, EndRetrace, AnswerCurrentQuestion/AnswerCurrentQuestionText -- and
/// reads Phase / the current question / the distractor number to drive its panels.
/// Optional debug keys let the whole flow be exercised on desktop without the UI.
/// The static ClearAllStudyData/StudyDataSummary/HasStudyData helpers are for clearing
/// out test/pilot CSV data -- see the "Tools/Study Data" Unity menu (Editor/StudyDataMenu.cs),
/// which is the intended way to use them.
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
    // In VR (an XR device is present/active) this is the head camera -- the XR rig's
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

    private string ParticipantLabel => $"p{participantId:00}";

    // =====================================================================
    // Session lifecycle
    // =====================================================================

    /// <summary>Begins the session: builds the counterbalanced trial list and starts the first trial.</summary>
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
                             "trials -- some will repeat within the session. Add more datasets.");
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
            startedUtc = DateTime.UtcNow.ToString("o"),
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
        SetPhase(SessionPhase.Complete);
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
        WriteRetraceCsv(_result);
        _questionIndex = 0;
        SetPhase(SessionPhase.Recall);
    }

    /// <summary>Answer the current recall question by picking option index (multiple-choice mode).</summary>
    public void AnswerCurrentQuestion(int optionIndex)
    {
        var q = CurrentQuestion;
        if (q == null) return;
        q.answeredIndex = optionIndex;
        WriteQuestionCsv(q);
        AdvanceRecall();
    }

    /// <summary>Answer the current recall question with a typed numeric value (text-entry mode).</summary>
    public void AnswerCurrentQuestionText(string text)
    {
        var q = CurrentQuestion;
        if (q == null) return;
        q.typedAnswer = text ?? "";
        WriteQuestionCsv(q);
        AdvanceRecall();
    }

    // Shared "move to next question, or finish the trial" step used by both answer entry points.
    private void AdvanceRecall()
    {
        if (_phase != SessionPhase.Recall) return;

        _questionIndex++;
        if (_questionIndex >= _result.questions.Count)
        {
            _result.finishedUtc = DateTime.UtcNow.ToString("o");
            WriteTrialCsv(_result);
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
        Debug.Log($"[Session] {(phase == SessionPhase.Idle || phase == SessionPhase.Complete ? "" : $"Trial {TrialNumber}/{TotalTrials} -> ")}{phase}");
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

        qs.Add(MakeNumericQuestion("max_value", "What was the highest value you encountered?", max, data.unit, range, orderIndex, 0));
        qs.Add(MakeNumericQuestion("min_value", "What was the lowest value you encountered?", min, data.unit, range, orderIndex, 1));
        qs.Add(MakeNumericQuestion("end_value", "What was the value at the end of the walk?", last, data.unit, range, orderIndex, 2));
        qs.Add(MakeNumericQuestion("range_value", "What was the difference between the highest and lowest points?",
                                   range, data.unit, range, orderIndex, 3));
        return qs;
    }

    // Build a recall question around a numeric answer. Always fills both a 4-option
    // multiple-choice representation (options/optionValues/correctIndex) and the raw
    // answerValue/unit, so RunSettings.recallInputMode can switch how it's rendered
    // without touching this logic. The correct answer's slot is randomized with a seed
    // derived from participant + trial order + question index, so placement is
    // unpredictable to the participant yet fully reproducible for analysis. Distractors
    // are offset by multiples of a step scaled to the dataset's value range, and are
    // guaranteed distinct from each other and the answer (by display value) and non-negative.
    private RecallQuestion MakeNumericQuestion(
        string questionType, string prompt, float answer, string unit, float range, int orderIndex, int qIndex)
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
        float[] optionValues = new float[4];
        int di = 0;
        for (int i = 0; i < 4; i++)
        {
            float v = (i == slot) ? answer : distractors[di++];
            optionValues[i] = v;
            options[i] = $"{Display(v)} {unit}";
        }

        return new RecallQuestion
        {
            questionType = questionType,
            prompt = prompt,
            options = options,
            optionValues = optionValues,
            correctIndex = slot,
            answerValue = answer,
            unit = unit
        };
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
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    t = Time.time - _phaseStart,
                    x = p.x,
                    y = p.y,
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
    // CSV logging -- schema follows the sources-folder data notes doc. Files are
    // shared/growing across the whole study (not one per participant): a header row
    // is written once, then every session appends more rows. Written incrementally
    // (not batched to session end) so a crash loses at most the current trial.
    // =====================================================================

    private static readonly string[] TrialsHeader =
        { "participant_id", "condition", "audio_cue", "visual_cue", "presentation_order", "trial_in_condition",
          "dataset_id", "timestamp_start", "timestamp_end", "walk_seconds", "distractor_seconds",
          "distractor_start_number", "retrace_seconds", "recall_score", "recall_total" };

    private static readonly string[] QuestionsHeader =
        { "participant_id", "condition", "audio_cue", "visual_cue", "presentation_order", "dataset_id",
          "question_id", "question_type", "question_text", "correct_answer", "participant_response",
          "is_correct", "answer_mode", "timestamp" };

    private static readonly string[] RetraceHeader =
        { "participant_id", "condition", "audio_cue", "visual_cue", "presentation_order", "dataset_id",
          "timestamp", "t_seconds", "head_x", "head_y", "head_z" };

    private void WriteTrialCsv(TrialResult r)
    {
        var (audio, visual) = CueLabels(r.condition);
        AppendCsvRow("trials.csv", TrialsHeader, new[]
        {
            ParticipantLabel, r.condition, audio, visual, (r.orderIndex + 1).ToString(),
            r.trialInCondition.ToString(), r.datasetName, r.startedUtc, r.finishedUtc,
            r.walkSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            r.distractorSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            r.distractorStartNumber.ToString(),
            r.retraceSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            r.RecallScore.ToString(), r.questions.Count.ToString()
        });
    }

    private void WriteQuestionCsv(RecallQuestion q)
    {
        var (audio, visual) = CueLabels(_result.condition);
        float? response = q.ResponseValue;
        AppendCsvRow("value_questions.csv", QuestionsHeader, new[]
        {
            ParticipantLabel, _result.condition, audio, visual, (_result.orderIndex + 1).ToString(),
            _result.datasetName, "q" + (_result.questions.IndexOf(q) + 1), q.questionType, q.prompt,
            q.answerValue.ToString("0.###", CultureInfo.InvariantCulture),
            response.HasValue ? response.Value.ToString("0.###", CultureInfo.InvariantCulture) : "",
            q.IsCorrect.ToString(), q.AnswerMode, DateTime.UtcNow.ToString("o")
        });
    }

    private void WriteRetraceCsv(TrialResult r)
    {
        var (audio, visual) = CueLabels(r.condition);
        string order = (r.orderIndex + 1).ToString();
        foreach (var s in r.retracePath)
        {
            AppendCsvRow("retracing_log.csv", RetraceHeader, new[]
            {
                ParticipantLabel, r.condition, audio, visual, order, r.datasetName,
                s.timestampUtc, s.t.ToString("0.###", CultureInfo.InvariantCulture),
                s.x.ToString("0.###", CultureInfo.InvariantCulture),
                s.y.ToString("0.###", CultureInfo.InvariantCulture),
                s.z.ToString("0.###", CultureInfo.InvariantCulture)
            });
        }
    }

    // audio_cue/visual_cue ("semantic"/"plain"/"none") per the modified plan's condition
    // mapping (ROADMAP M4): Abstract = plain sphere + plain beep; Representative = semantic
    // spark + semantic electric buzz; SemanticAudio (aka MismatchStatic) = plain sphere +
    // semantic electric buzz; SemanticVisual (aka MismatchNonRep) = semantic spark + plain
    // beep; Control = neither.
    private static (string audio, string visual) CueLabels(string conditionName)
    {
        switch (conditionName)
        {
            case "Control": return ("none", "none");
            case "Abstract": return ("plain", "plain");
            case "Representative": return ("semantic", "semantic");
            case "SemanticAudio": return ("semantic", "plain");
            case "SemanticVisual": return ("plain", "semantic");
            default: return ("unknown", "unknown");
        }
    }

    private static void AppendCsvRow(string fileName, string[] header, string[] row)
    {
        try
        {
            string path = Path.Combine(StudyDataDir(), fileName);
            bool exists = File.Exists(path);

            using (var w = new StreamWriter(path, append: true))
            {
                if (!exists) w.WriteLine(string.Join(",", Array.ConvertAll(header, EscapeCsv)));
                w.WriteLine(string.Join(",", Array.ConvertAll(row, EscapeCsv)));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] Failed to write {fileName}: {e.Message}");
        }
    }

    private static string EscapeCsv(string value)
    {
        value ??= "";
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public static string StudyDataDir()
    {
        string dir = Path.Combine(Application.persistentDataPath, "StudyData");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // The raw CSV files SessionController writes. "demographics.csv" is included only
    // so a settings-menu clear picks up any leftover file from before the participant
    // demographics form was removed from the study -- nothing writes it any more.
    private static readonly string[] StudyDataFiles =
        { "trials.csv", "value_questions.csv", "retracing_log.csv", "demographics.csv" };

    // =====================================================================
    // Settings-menu data management -- called from the UI's Settings screen.
    // =====================================================================

    /// <summary>Human-readable listing of what's currently recorded, for a confirmation
    /// screen before a destructive clear. Row counts are cheap here (small CSVs; this is
    /// a menu action, not a per-frame call).</summary>
    public static string StudyDataSummary()
    {
        string dir = StudyDataDir();
        var lines = new List<string>();
        foreach (var f in StudyDataFiles)
        {
            string p = Path.Combine(dir, f);
            if (!File.Exists(p)) continue;
            int rows = Math.Max(0, File.ReadAllLines(p).Length - 1);   // minus header
            lines.Add($"{f}: {rows} row(s)");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "No study data recorded yet.";
    }

    /// <summary>True if any recorded CSV data exists (so the UI can grey out/hide "Clear" when there's nothing to clear).</summary>
    public static bool HasStudyData()
    {
        string dir = StudyDataDir();
        foreach (var f in StudyDataFiles)
            if (File.Exists(Path.Combine(dir, f))) return true;
        return false;
    }

    /// <summary>Permanently deletes every recorded CSV file (trials/questions/retrace, plus any
    /// leftover demographics.csv). Intended for clearing out test/pilot runs from the settings
    /// menu before real data collection -- irreversible, so the UI must confirm before calling this.</summary>
    public static string ClearAllStudyData()
    {
        string dir = StudyDataDir();
        int deleted = 0;
        foreach (var f in StudyDataFiles)
        {
            string p = Path.Combine(dir, f);
            if (File.Exists(p)) { File.Delete(p); deleted++; }
        }
        Debug.Log($"[Session] Cleared {deleted} study data file(s) from {dir}");
        return deleted > 0 ? $"Cleared {deleted} file(s)." : "No study data found.";
    }
}
