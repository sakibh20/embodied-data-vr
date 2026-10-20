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
/// and retracing_log.csv (one row per retrace sample) -- written into that
/// participant's own subfolder under StudyData/ (StudyData/p01/trials.csv, etc., see
/// ParticipantDir), so each participant's data is a self-contained set of files that
/// can be copied/backed up/deleted independently instead of one giant shared file per
/// study. Still written incrementally within that folder (retrace rows at end of
/// Retrace, a question row the moment it's answered, a trial row the moment its last
/// recall question is answered) so a crash mid-session loses at most the current
/// in-progress trial, not the whole participant.
///
/// The in-VR UI (built separately, SessionUI) calls the public API -- StartSession,
/// EndDistractor, AnswerCurrentQuestion/AnswerCurrentQuestionText -- and reads Phase /
/// the current question / the distractor number to drive its panels. EndWalk/EndRetrace
/// are instead called by this class itself (CheckAutoEndOfPhase, from Update): Walk and
/// Retrace show no UI panel (it would occlude the floor graph -- ROADMAP.md M12), so
/// those two phases end themselves once the participant walks past the far end of the
/// graph corridor, rather than waiting for a button press.
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
    [SerializeField] private PathSampler pathSampler;   // for auto-detecting end of Walk/Retrace

    [Header("Design")]
    [SerializeField] private List<GraphData> datasetPool = new List<GraphData>();
    [SerializeField] private int participantId = 1;
    // Trials per condition now lives on RunSettings (M59).
    private int TrialsPerCondition =>
        StudyConfig.Settings != null ? Mathf.Max(1, StudyConfig.Settings.trialsPerCondition) : 1;

    [Header("Retrace capture")]
    [SerializeField] private float retraceSampleInterval = 0.1f;

    [Header("Consent (M59)")]
    [Tooltip("Shown once at the start of each session, before the first trial. " +
             "Leave empty to skip.")]
    [SerializeField] private ConsentDefinition consentForm;

    [Header("Background questions (M61)")]
    [Tooltip("Asked once right after consent (VR experience, age range). Written to " +
             "background.csv. Leave empty to skip.")]
    [SerializeField] private QuestionnaireDefinition backgroundQuestions;

    [Header("Post-condition questionnaire (M58)")]
    [Tooltip("Asked once after each condition's LAST trial (not after every trial). " +
             "Leave empty to skip. Items are edited on the asset.")]
    [SerializeField] private QuestionnaireDefinition postConditionQuestionnaire;

    [Header("Participant reference (M51)")]
    [Tooltip("On Start Session, re-anchor the ExperimentArea (graph, grid, markers, cue " +
             "field) to wherever the participant is standing and facing, so the walk " +
             "always starts right in front of them. Have them stand at the agreed spot, " +
             "facing the walk direction, then press Start Session.")]
    [SerializeField] private bool alignAreaToParticipantOnStart = true;
    [SerializeField] private ExperimentArea experimentArea;
    [Tooltip("Seconds the just-walked path is drawn on the floor (same dot/line style " +
             "as the real graph) after Retrace ends, before Recall questions begin. " +
             "Shown only AFTER retrace, never while walking -- Retrace itself stays a " +
             "pure memory test. See ROADMAP.md M44.")]
    [SerializeField] private float retraceReviewSeconds = 5f;

    [Header("Walk/Retrace auto-end (UI is hidden during these phases -- see SessionUI)")]
    [Tooltip("Metres the participant must walk past the far end of the graph corridor " +
             "before Walk/Retrace auto-ends. Requires an intentional pass-through rather " +
             "than stopping exactly on the last data point.")]
    [SerializeField] private float autoEndOvershoot = 0.3f;
    [Tooltip("Seconds to ignore auto-end right after the phase starts, so the participant's " +
             "starting position (or a stale PathSampler reading mid graph-generation) can't " +
             "fire it immediately.")]
    [SerializeField] private float autoEndGracePeriod = 0.5f;

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
    private bool _hasVisitedStartThisPhase;
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
        if (pathSampler == null)
        {
            // Resolve to the SAME PathSampler WalkValueDriver/GraphManager already use,
            // rather than a blind scene-wide FindAnyObjectByType -- which is ambiguous
            // (and silently picks the wrong, never-configured one) if any other
            // PathSampler exists in the scene. Found and fixed live: a stray leftover
            // "TempSampler" GameObject was exactly that -- CheckAutoEndOfPhase's
            // !pathSampler.IsReady guard was permanently true against it, so Walk/Retrace
            // could never auto-complete. See ROADMAP.md M37.
            var walkDriver = FindAnyObjectByType<WalkValueDriver>();
            pathSampler = walkDriver != null ? walkDriver.Path : null;
            if (pathSampler == null) pathSampler = FindAnyObjectByType<PathSampler>();
        }

        // Auto-suggest the next participant ID from what's already in trials.csv,
        // instead of always starting from whatever was last left in the Inspector
        // (which is how every test session so far ended up logged as p01 -- nothing
        // ever advanced it). Still adjustable on the Idle screen (+/-) before Start.
        participantId = SuggestNextParticipantId();

        // Session starts in Idle: no cues until a Walk begins (M53).
        _walkDriver = FindAnyObjectByType<WalkValueDriver>();
        if (_walkDriver != null) _walkDriver.SetActive(false);

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

    // ---- Post-condition questionnaire (M58) ----
    private int _qnIndex;
    private int[] _qnResponses;   // -1 = not answered yet
    /// <summary>True while the questionnaire's intro screen is shown (M59), before item 1.</summary>
    public bool QnShowingIntro { get; private set; }

    public void QnBeginItems()
    {
        if (_phase != SessionPhase.Questionnaire || !QnShowingIntro) return;
        QnShowingIntro = false;
        PhaseChanged?.Invoke(_phase);
    }

    // M61: the questionnaire screen serves both the post-condition questionnaire and the
    // background questions; _qnDef is whichever is running.
    private QuestionnaireDefinition _qnDef;
    private bool _qnIsBackground;
    public bool QnIsBackground => _qnIsBackground;

    private void StartQuestionnaire(QuestionnaireDefinition def, bool background)
    {
        _qnDef = def;
        _qnIsBackground = background;
        _qnIndex = 0;
        QnShowingIntro = !string.IsNullOrEmpty(def.intro);
        _qnResponses = new int[def.Count];
        for (int i = 0; i < _qnResponses.Length; i++) _qnResponses[i] = -1;
        SetPhase(SessionPhase.Questionnaire);
    }

    public QuestionnaireDefinition Questionnaire => _qnDef;
    public QuestionnaireItem CurrentQnItem =>
        (_phase == SessionPhase.Questionnaire && _qnDef != null &&
         _qnIndex >= 0 && _qnIndex < _qnDef.Count)
            ? _qnDef.items[_qnIndex] : null;
    public int QnNumber => _qnIndex + 1;
    public int QnCount => _qnDef != null ? _qnDef.Count : 0;
    public int CurrentQnResponse => (_qnResponses != null && _qnIndex < _qnResponses.Length) ? _qnResponses[_qnIndex] : -1;
    public string ConditionDisplayName => _result != null ? _result.condition : "";

    /// <summary>Record the answer for the current item (1..5 or 0..100); the participant
    /// can still change it until they press Next.</summary>
    public void SetQnResponse(int value)
    {
        if (_phase != SessionPhase.Questionnaire || _qnResponses == null) return;
        _qnResponses[_qnIndex] = value;
        PhaseChanged?.Invoke(_phase);
    }

    /// <summary>Next item (only once answered); after the last one, writes all answers to
    /// questionnaires.csv and continues to TrialComplete.</summary>
    public void QnNext()
    {
        if (_phase != SessionPhase.Questionnaire) return;
        if (QnShowingIntro) { QnBeginItems(); return; }
        if (CurrentQnResponse < 0) return;
        if (_qnIndex < QnCount - 1) { _qnIndex++; PhaseChanged?.Invoke(_phase); return; }

        if (_qnIsBackground)
        {
            WriteBackgroundCsv();
            _qnIsBackground = false;
            ContinueAfterBackground();
            return;
        }
        WriteQuestionnaireCsv();
        ReadyToStartNext = false;
        SetPhase(SessionPhase.TrialComplete);
    }

    public void QnBack()
    {
        if (_phase != SessionPhase.Questionnaire || _qnIndex <= 0) return;
        _qnIndex--;
        PhaseChanged?.Invoke(_phase);
    }

    // True when no later trial in the session uses this trial's condition, i.e. this
    // is the end of the condition (works for trialsPerCondition 1 or 2).
    private bool IsLastTrialOfCondition(int index)
    {
        if (index < 0 || index >= _trials.Count) return false;
        var c = _trials[index].condition;
        for (int i = index + 1; i < _trials.Count; i++)
            if (_trials[i].condition == c) return false;
        return true;
    }
    public int QuestionCount => _result?.questions.Count ?? 0;

    /// <summary>
    /// True once the participant has walked back near the start of the corridor
    /// during TrialComplete -- i.e. the "Start Next Trial" gate (see StartNextTrial)
    /// is unlocked. Only meaningful while Phase == TrialComplete. See ROADMAP.md M39.
    /// </summary>
    public bool ReadyToStartNext { get; private set; }

    /// <summary>Participant label as written to CSV ("p01", "p02", ...). Shown/adjusted on the Idle screen.</summary>
    public string ParticipantLabel => $"p{participantId:00}";

    public void IncrementParticipantId() => participantId++;
    public void DecrementParticipantId() => participantId = Mathf.Max(1, participantId - 1);

    /// <summary>
    /// Scans StudyData/ for the highest participant folder ("p07" -> 7) already
    /// present and returns one past it (or 1 if there's no data yet). Folder-based
    /// rather than an in-memory counter, so it's correct across Editor restarts and
    /// gives a sensible default even the very first time the scene is opened.
    /// Test/pilot participant folders count too -- clear them first via Tools > Study
    /// Data > Clear Study Data before a real run if you don't want them to push the
    /// suggested number up.
    /// </summary>
    public static int SuggestNextParticipantId()
    {
        string baseDir = StudyDataDir();
        int max = 0;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            if (TryParseParticipantNumber(Path.GetFileName(dir), out int n) && n > max) max = n;
        }

        // Also fold in a legacy flat trials.csv directly in StudyData/, from before
        // per-participant folders existed (ROADMAP M28), so a stale legacy file
        // can't cause a new participant to collide with an old one's id.
        string legacyTrials = Path.Combine(baseDir, "trials.csv");
        if (File.Exists(legacyTrials))
        {
            string[] lines = File.ReadAllLines(legacyTrials);
            for (int i = 1; i < lines.Length; i++)   // skip header row
            {
                int comma = lines[i].IndexOf(',');
                if (comma <= 0) continue;
                if (TryParseParticipantNumber(lines[i].Substring(0, comma).Trim(), out int n) && n > max) max = n;
            }
        }

        return max + 1;
    }

    // Parses a participant label ("p07") into its numeric id (7). Shared by
    // SuggestNextParticipantId's folder scan and its legacy-file fallback.
    private static bool TryParseParticipantNumber(string label, out int n)
    {
        n = 0;
        if (string.IsNullOrEmpty(label) || label.Length < 2) return false;
        char c0 = label[0];
        if (c0 != 'p' && c0 != 'P') return false;
        return int.TryParse(label.Substring(1), out n);
    }

    // =====================================================================
    // Session lifecycle
    // =====================================================================

    /// <summary>Begins the session: builds the counterbalanced trial list and starts the first trial.</summary>
    [ContextMenu("Start Session")]
    public void StartSession()
    {
        // Only from Idle: calling it again while the consent screen is up must never skip consent.
        if (_phase != SessionPhase.Idle) return;

        // M59: the consent screen comes first. The area is aligned now, while the
        // participant still stands where the researcher placed them.
        if (consentForm != null && _phase == SessionPhase.Idle)
        {
            if (alignAreaToParticipantOnStart) AlignAreaToParticipant();
            ConsentDeclined = false;
            ConsentChecked = false;
            SetPhase(SessionPhase.Consent);
            return;
        }
        BeginTrials(alignArea: consentForm == null);
    }

    // ---- Consent (M59) ----
    public ConsentDefinition ConsentForm => consentForm;
    public bool ConsentDeclined { get; private set; }
    /// <summary>State of the "I agree to take part" tick box (M61).</summary>
    public bool ConsentChecked { get; private set; }

    public void ToggleConsentCheck()
    {
        if (_phase != SessionPhase.Consent || ConsentDeclined) return;
        ConsentChecked = !ConsentChecked;
        PhaseChanged?.Invoke(_phase);
    }

    /// <summary>Continue: ticked = consent (logged, study starts); unticked = no consent
    /// (logged, the declined message is shown).</summary>
    public void ConsentContinue()
    {
        if (ConsentChecked) ConsentAgree(); else ConsentDecline();
    }

    public void ConsentAgree()
    {
        if (_phase != SessionPhase.Consent || ConsentDeclined) return;
        WriteConsentCsv(consentForm != null ? consentForm.consentResponse : "I consent");
        BeginTrials(alignArea: false);
    }

    public void ConsentDecline()
    {
        if (_phase != SessionPhase.Consent || ConsentDeclined) return;
        WriteConsentCsv(consentForm != null ? consentForm.noConsentResponse : "I do not consent");
        ConsentDeclined = true;
        PhaseChanged?.Invoke(_phase);
    }

    /// <summary>From the "declined" message back to the researcher's Idle screen.</summary>
    public void ConsentBackToIdle()
    {
        if (_phase != SessionPhase.Consent) return;
        ConsentDeclined = false;
        SetPhase(SessionPhase.Idle);
    }

    private static readonly string[] ConsentHeader =
        { "participant_id", "consent_version", "response", "timestamp", "datetime_local" };

    private void WriteConsentCsv(string response)
    {
        AppendCsvRow(ParticipantLabel, "consent.csv", ConsentHeader, new[]
        {
            ParticipantLabel, consentForm != null ? consentForm.version : "", response,
            DateTime.UtcNow.ToString("o"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    private void BeginTrials(bool alignArea)
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

        if (alignArea && alignAreaToParticipantOnStart) AlignAreaToParticipant();

        BuildTrials();
        _index = -1;
        TutorialJustCompleted = false;
        Debug.Log($"[Session] Participant {participantId}: {PracticeCount} practice + " +
                  $"{_trials.Count - PracticeCount} trials.");

        // M61: background questions first (they use the questionnaire screen).
        if (backgroundQuestions != null && backgroundQuestions.Count > 0)
        {
            StartQuestionnaire(backgroundQuestions, background: true);
            return;
        }
        ContinueAfterBackground();
    }

    private void ContinueAfterBackground()
    {
        // M60: with a tutorial, the instructions screen comes first.
        if (PracticeCount > 0) { SetPhase(SessionPhase.Instructions); return; }
        NextTrial();
    }

    // ---- Tutorial (M60) ----
    /// <summary>Number of practice (tutorial) trials at the start of the trial list.</summary>
    public int PracticeCount { get; private set; }
    public bool IsPracticeTrial => _index >= 0 && _index < _trials.Count && _trials[_index].isPractice;
    /// <summary>True in the TrialComplete phase right after the last tutorial trial.</summary>
    public bool TutorialJustCompleted { get; private set; }

    /// <summary>"Practice 1/2" or "Trial 3/10" (experiment trials numbered without the tutorial).</summary>
    public string TrialTag => IsPracticeTrial
        ? $"Practice {TrialNumber}/{PracticeCount}"
        : $"Trial {TrialNumber - PracticeCount}/{_trials.Count - PracticeCount}";

    /// <summary>Instructions screen -> first tutorial trial.</summary>
    public void BeginTutorial()
    {
        if (_phase != SessionPhase.Instructions) return;
        NextTrial();
    }

    /// <summary>Re-anchor the ExperimentArea to the participant's current head pose
    /// (see ExperimentArea.AlignToParticipant). Also on the context menu, and debug key
    /// R while Idle.</summary>
    [ContextMenu("Align Area To Participant Now")]
    public void AlignAreaToParticipant()
    {
        if (experimentArea == null) experimentArea = FindAnyObjectByType<ExperimentArea>();
        Transform head = PlayerRig.Head != null ? PlayerRig.Head : player;
        if (experimentArea == null || head == null) return;

        experimentArea.AlignToParticipant(head);
        Debug.Log($"[Session] Area aligned to participant: head {head.position}, " +
                  $"area centre {experimentArea.Center}, walk dir {experimentArea.Forward}.");
    }

    /// <summary>
    /// Called after a session finishes (Complete) to prepare for the next
    /// participant without stopping/restarting Play mode: re-suggests the
    /// participant id from what's now on disk (the session that just finished is
    /// already written, so this correctly advances past it) and returns to Idle so
    /// the researcher sees/can adjust the new id and audio mode before Start Session.
    /// Without this, back-to-back sessions in the same Play session would otherwise
    /// keep reusing the same participantId that was only ever suggested once, at
    /// Awake -- every subsequent run would silently log under the same participant.
    /// </summary>
    public void ReturnToIdle()
    {
        if (_phase != SessionPhase.Complete) return;
        participantId = SuggestNextParticipantId();
        SetPhase(SessionPhase.Idle);
    }

    // Condition order per RunSettings.conditionOrderMode (Sequential / Random /
    // RandomExceptControlFirst -- see BuildConditionOrder), one order per block.
    // Dataset order is unrelated to condition order: it always advances by the flat
    // trial index (participant-rotated), independent of which condition lands where.
    private void BuildTrials()
    {
        _trials.Clear();
        PracticeCount = 0;
        var tut = StudyConfig.Settings != null ? StudyConfig.Settings.tutorialTrials : null;
        if (tut != null)
            foreach (var t in tut)
            {
                if (t == null || t.dataset == null) continue;
                _trials.Add(new TrialSpec
                {
                    condition = t.condition, dataset = t.dataset,
                    orderIndex = PracticeCount, trialInCondition = 0, isPractice = true
                });
                PracticeCount++;
            }

        int n = AllConditions.Length;
        int startDs = datasetPool.Count > 0 ? (participantId - 1) % datasetPool.Count : 0;
        int order = 0;

        ConditionOrderMode mode = StudyConfig.Settings != null
            ? StudyConfig.Settings.conditionOrderMode
            : ConditionOrderMode.Sequential;

        // M59: blocked design -- one condition order per participant, and each
        // condition's trials run back to back (A A A, B B B, ...). Every trial still
        // takes the next dataset in the participant-rotated pool.
        int reps = TrialsPerCondition;
        ConditionId[] condOrder = BuildConditionOrder(mode, 0);
        for (int k = 0; k < n; k++)
        {
            for (int rep = 0; rep < reps; rep++)
            {
                var ds = datasetPool[(startDs + order) % datasetPool.Count];
                _trials.Add(new TrialSpec
                {
                    condition = condOrder[k],
                    dataset = ds,
                    orderIndex = order,
                    trialInCondition = rep
                });
                order++;
            }
        }

        if (datasetPool.Count < _trials.Count - PracticeCount)
            Debug.LogWarning($"[Session] Only {datasetPool.Count} datasets for {_trials.Count - PracticeCount} " +
                             "trials -- some will repeat within the session. Add more datasets.");
    }

    // Builds the condition order for one block (repeat) of trials:
    //  - Sequential: the same fixed AllConditions order every time -- identical across
    //    participants, so this mode alone provides no order-effect counterbalancing.
    //  - Random: all 5 conditions (Control included) shuffled freely.
    //  - RandomExceptControlFirst: Control forced into position 0, the remaining 4
    //    shuffled after it -- Control is always the first condition a participant sees.
    // The two random modes are seeded from participantId + block (ConditionOrderSeed),
    // matching the reproducible-seed pattern already used for recall answer-slot
    // placement, so a participant's order is stable/reproducible rather than depending
    // on Unity's global random state.
    private ConditionId[] BuildConditionOrder(ConditionOrderMode mode, int block)
    {
        switch (mode)
        {
            case ConditionOrderMode.Random:
            {
                var order = (ConditionId[])AllConditions.Clone();
                Shuffle(order, ConditionOrderSeed(block));
                return order;
            }
            case ConditionOrderMode.RandomExceptControlFirst:
            {
                var rest = new List<ConditionId>(AllConditions.Length - 1);
                foreach (var c in AllConditions)
                    if (c != ConditionId.Control) rest.Add(c);
                var restArray = rest.ToArray();
                Shuffle(restArray, ConditionOrderSeed(block));

                var order = new ConditionId[AllConditions.Length];
                order[0] = ConditionId.Control;
                restArray.CopyTo(order, 1);
                return order;
            }
            case ConditionOrderMode.LatinSquare:
            {
                // Later blocks (trialsPerCondition > 1) step to the next row so a
                // participant doesn't repeat the identical order.
                int[] row = WilliamsRow(AllConditions.Length, participantId - 1 + block);
                var order = new ConditionId[row.Length];
                for (int i = 0; i < row.Length; i++) order[i] = AllConditions[row[i]];
                return order;
            }
            case ConditionOrderMode.Sequential:
            default:
                return (ConditionId[])AllConditions.Clone();
        }
    }

    // Balanced (Williams) Latin square row. First row: 0, 1, n-1, 2, n-2, ...; row r adds
    // r (mod n) to every entry. For odd n the square needs 2n rows -- the n rows plus
    // their reverses -- to balance first-order carryover; for even n, n rows suffice.
    // Every condition appears once per row and once per position across a full set of
    // rows. Indices refer to AllConditions. See ROADMAP.md M47.
    public static int WilliamsRowCount(int n) => n % 2 == 0 ? n : 2 * n;

    public static int[] WilliamsRow(int n, int rowIndex)
    {
        int rows = WilliamsRowCount(n);
        int r = ((rowIndex % rows) + rows) % rows;

        int[] first = new int[n];
        int lo = 1, hi = n - 1;
        for (int j = 1; j < n; j++)
            first[j] = (j % 2 == 1) ? lo++ : hi--;

        int shift = r % n;
        int[] row = new int[n];
        for (int j = 0; j < n; j++) row[j] = (first[j] + shift) % n;

        if (r >= n) System.Array.Reverse(row);   // mirrored half (odd n only)
        return row;
    }

    // Reproducible per participant/block seed for condition-order shuffling.
    private int ConditionOrderSeed(int block) => participantId * 40503 ^ (block + 1) * 200003;

    private static void Shuffle<T>(T[] array, int seed)
    {
        var rng = new System.Random(seed);
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
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
        TutorialJustCompleted = false;
        _result = new TrialResult
        {
            orderIndex = spec.orderIndex,
            condition = spec.condition.ToString(),
            datasetName = spec.dataset != null ? spec.dataset.name : "(none)",
            trialInCondition = spec.trialInCondition,
            isPractice = spec.isPractice,
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

    /// <summary>
    /// Called by the UI's "Start Next Trial" button (TrialComplete phase only, and
    /// only once ReadyToStartNext is true -- see Update) to actually generate and
    /// begin the next trial. Splitting this out of AdvanceRecall means the next
    /// graph never starts forming until the participant has physically returned to
    /// the start and chosen to continue. See ROADMAP.md M39.
    /// </summary>
    public void StartNextTrial()
    {
        if (_phase != SessionPhase.TrialComplete || !ReadyToStartNext) return;
        NextTrial();
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

        // Retrace: hide the line/dots. M53 (supervisor): the trial's audio + visual cues
        // stay ON, exactly as during Walk (they follow where the participant is standing,
        // StandingPosition feedback), instead of switching to Control.
        graphManager.SetGraphVisible(false);
        _result.retracePath.Clear();
        _retraceTimer = 0f;
        SetPhase(SessionPhase.Retrace);
    }

    public void EndRetrace()
    {
        if (_phase != SessionPhase.Retrace) return;
        _result.retraceSeconds = Time.time - _phaseStart;
        WriteRetraceCsv(_result);

        // Briefly show the participant what they just walked, drawn in the same
        // dot/line style as the real graph, before Recall -- purely a post-hoc review.
        // Nothing is shown DURING Retrace itself (that stays a pure memory test); this
        // only fires once Retrace has already ended. See ROADMAP.md M44.
        graphManager.ShowRetraceGraph(_result.retracePath);
        SetPhase(SessionPhase.RetraceReview);
        Invoke(nameof(EndRetraceReview), retraceReviewSeconds);
    }

    /// <summary>
    /// Ends the brief post-retrace review (see EndRetrace) and moves on to Recall.
    /// Normally fires automatically after retraceReviewSeconds; also callable directly
    /// (e.g. a debug key) to skip ahead. See ROADMAP.md M44.
    /// </summary>
    public void EndRetraceReview()
    {
        if (_phase != SessionPhase.RetraceReview) return;
        CancelInvoke(nameof(EndRetraceReview));
        graphManager.ClearRetraceGraph();
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

        // Recall answers are not skippable: reject an empty/unparseable submission
        // instead of silently recording a blank "skip" and advancing. SessionUI
        // validates first and shows the participant feedback; this is the
        // authoritative backstop in case anything else ever calls this directly.
        if (string.IsNullOrWhiteSpace(text) ||
            !float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            Debug.LogWarning("[Session] Rejected empty/invalid recall answer -- question is not skippable.");
            return;
        }

        q.typedAnswer = text;
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

            // Don't generate the next trial's graph immediately: the participant is
            // still standing wherever Recall left them (typically right at the far
            // end of the corridor they just retraced), so starting generation here
            // put them in the middle of a new graph forming around them, with no
            // chance to see it "spawn". Require they walk back near the start first,
            // then press a button -- see StartNextTrial / Update. See ROADMAP.md M39.
            // M58: at the end of a condition (its last trial), the post-condition
            // questionnaire comes first; TrialComplete follows it (see QnNext).
            // M60: practice trials never get the questionnaire; after the last one the
            // TrialComplete screen says the tutorial is complete.
            if (_trials[_index].isPractice)
            {
                TutorialJustCompleted = _index + 1 >= _trials.Count || !_trials[_index + 1].isPractice;
                ReadyToStartNext = false;
                SetPhase(SessionPhase.TrialComplete);
                return;
            }

            if (postConditionQuestionnaire != null && postConditionQuestionnaire.Count > 0 &&
                IsLastTrialOfCondition(_index))
            {
                StartQuestionnaire(postConditionQuestionnaire, background: false);
                return;
            }

            ReadyToStartNext = false;
            SetPhase(SessionPhase.TrialComplete);
        }
        else
        {
            PhaseChanged?.Invoke(_phase);   // same phase, next question
        }
    }

    private WalkValueDriver _walkDriver;

    private void SetFeedbackArmed(bool armed)
    {
        if (_walkDriver == null) _walkDriver = FindAnyObjectByType<WalkValueDriver>();
        bool walking = _phase == SessionPhase.Walk || _phase == SessionPhase.Retrace;
        if (_walkDriver != null) _walkDriver.SetActive(armed && walking);
    }

    private void SetPhase(SessionPhase phase)
    {
        _phase = phase;
        _phaseStart = Time.time;

        // M53: cues run only while the participant is walking -- Walk and Retrace.
        // M56: and even then only once they have stood on the Start marker in THIS
        // phase (CheckAutoEndOfPhase arms it). Everywhere else (Idle, Distractor,
        // review, Recall, between trials, walking back to the start) they are off.
        SetFeedbackArmed(false);
        _hasVisitedStartThisPhase = false;
        PhaseChanged?.Invoke(phase);
        Debug.Log($"[Session] {(phase == SessionPhase.Idle || phase == SessionPhase.Complete || phase == SessionPhase.Consent || phase == SessionPhase.Instructions ? "" : $"{TrialTag} -> ")}{phase}");
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

        // M56: only the actual attempt is recorded -- from the moment the participant
        // stands on the Start marker -- not the walk back to it.
        if (_phase == SessionPhase.Retrace && head != null && _hasVisitedStartThisPhase)
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

        CheckAutoEndOfPhase(head);
        CheckReadyToStartNext(head);

        if (debugKeys) HandleDebugKeys();
    }

    // TrialComplete: the participant must walk back near the start of the corridor
    // they just retraced before the "Start Next Trial" button (SessionUI) does
    // anything -- see StartNextTrial. pathSampler still holds the JUST-FINISHED
    // trial's geometry here (GenerateGraph/PathSampler.Clear() only runs once
    // StartNextTrial actually fires), so "near the start" means the same physical
    // spot the participant started that trial's Walk from. Only re-renders the UI
    // (via PhaseChanged) on the frame this flips, not every frame. See ROADMAP.md M39.
    private void CheckReadyToStartNext(Transform head)
    {
        if (_phase != SessionPhase.TrialComplete) return;
        if (head == null || pathSampler == null || !pathSampler.IsReady) return;

        if (experimentArea == null) experimentArea = FindAnyObjectByType<ExperimentArea>();
        bool nearStart = experimentArea != null ? experimentArea.IsInStartZone(head.position)
                                                : pathSampler.DistanceAlong(head.position) <= autoEndOvershoot;
        if (nearStart == ReadyToStartNext) return;

        ReadyToStartNext = nearStart;
        PhaseChanged?.Invoke(_phase);
    }

    // Walk and Retrace have no on-screen UI while they're active (SessionUI hides its
    // panel during these phases so the head-locked panel doesn't occlude the floor
    // graph -- see PROJECT_DESCRIPTION.md Sec.7 / ROADMAP.md M12). Instead of a "Done"
    // button, each phase ends itself once the participant has actually walked past the
    // far end of the graph corridor -- a physical completion signal instead of a UI one.
    private void CheckAutoEndOfPhase(Transform head)
    {
        if (_phase != SessionPhase.Walk && _phase != SessionPhase.Retrace) return;
        if (head == null || pathSampler == null || !pathSampler.IsReady) return;
        if (graphManager != null && graphManager.IsGenerating) return;   // sampler may be stale mid-tween

        // Walk and Retrace both require an actual start-to-end pass. Nothing resets
        // the participant's *physical* position between phases/trials -- e.g. Retrace
        // begins with them still standing wherever Walk+Distractor left them, right
        // near the far end -- so checking only "have they passed the far end" would
        // fire almost instantly (as soon as the grace period elapses) without them
        // ever walking anything. Require they've been back near the start at some
        // point during *this* phase before the far-end check can complete it.
        // M56: "at the start" / "at the end" now mean standing on the Start / End
        // MARKER (past the safe gap either side of the graph zone), not merely near
        // the first/last point. Arming also switches feedback on (see SetFeedbackArmed):
        // walking back to the start during Retrace is silent and not recorded.
        if (experimentArea == null) experimentArea = FindAnyObjectByType<ExperimentArea>();
        float d = pathSampler.DistanceAlong(head.position);

        bool atStart = experimentArea != null ? experimentArea.IsInStartZone(head.position)
                                              : d <= autoEndOvershoot;
        if (atStart && !_hasVisitedStartThisPhase)
        {
            _hasVisitedStartThisPhase = true;
            SetFeedbackArmed(true);
        }

        if (Time.time - _phaseStart < autoEndGracePeriod) return;
        if (!_hasVisitedStartThisPhase) return;

        bool atEnd = experimentArea != null ? experimentArea.IsInEndZone(head.position)
                                            : d >= pathSampler.TotalLength + autoEndOvershoot;
        if (!atEnd) return;

        if (_phase == SessionPhase.Walk) EndWalk();
        else EndRetrace();
    }

    private void HandleDebugKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (_phase == SessionPhase.Idle && kb.rKey.wasPressedThisFrame)
            AlignAreaToParticipant();

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            switch (_phase)
            {
                case SessionPhase.Idle: StartSession(); break;
                case SessionPhase.Complete: ReturnToIdle(); break;
                case SessionPhase.Walk: EndWalk(); break;
                case SessionPhase.Distractor: EndDistractor(); break;
                case SessionPhase.Retrace: EndRetrace(); break;
                case SessionPhase.RetraceReview: EndRetraceReview(); break;
                case SessionPhase.TrialComplete: StartNextTrial(); break;
                case SessionPhase.Questionnaire: QnNext(); break;
                case SessionPhase.Instructions: BeginTutorial(); break;
                case SessionPhase.Consent: if (ConsentDeclined) ConsentBackToIdle(); else ConsentAgree(); break;
            }
        }

        // Questionnaire (debug): 1-5 answers an agreement item; Backspace goes back.
        if (_phase == SessionPhase.Questionnaire)
        {
            var item = CurrentQnItem;
            if (item != null && item.scale == QuestionnaireScale.Agree5)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetQnResponse(1);
                else if (kb.digit2Key.wasPressedThisFrame) SetQnResponse(2);
                else if (kb.digit3Key.wasPressedThisFrame) SetQnResponse(3);
                else if (kb.digit4Key.wasPressedThisFrame) SetQnResponse(4);
                else if (kb.digit5Key.wasPressedThisFrame) SetQnResponse(5);
            }
            if (kb.backspaceKey.wasPressedThisFrame) QnBack();
        }

        // Only treat 1-4 as MCQ shortcuts when the recall UI is actually in
        // MultipleChoice mode. In TextEntry mode those same digits are legitimate
        // characters the participant is typing into the answer box (e.g. "12.5") --
        // without this guard, typing "1"/"2"/"3" into the field also fired an MCQ
        // answer-by-index here and immediately advanced the question out from under
        // the input field, bypassing it (and the not-skippable validation) entirely.
        bool mcqShortcutsActive = StudyConfig.Settings == null
            || StudyConfig.Settings.recallInputMode == RecallInputMode.MultipleChoice;

        if (_phase == SessionPhase.Recall && mcqShortcutsActive)
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
        AppendCsvRow(ParticipantLabel, DataFile("trials.csv", r.isPractice), TrialsHeader, new[]
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
        AppendCsvRow(ParticipantLabel, DataFile("value_questions.csv", _result.isPractice), QuestionsHeader, new[]
        {
            ParticipantLabel, _result.condition, audio, visual, (_result.orderIndex + 1).ToString(),
            _result.datasetName, "q" + (_result.questions.IndexOf(q) + 1), q.questionType, q.prompt,
            q.answerValue.ToString("0.###", CultureInfo.InvariantCulture),
            response.HasValue ? response.Value.ToString("0.###", CultureInfo.InvariantCulture) : "",
            q.IsCorrect.ToString(), q.AnswerMode, DateTime.UtcNow.ToString("o")
        });
    }

    private static readonly string[] QuestionnaireHeader =
        { "participant_id", "condition", "audio_cue", "visual_cue", "presentation_order", "dataset_id",
          "instrument", "subscale", "item_id", "item_text", "scale", "response", "timestamp" };

    // One row per item, written when the questionnaire is finished (so answers changed
    // via Back are logged with their final value). presentation_order/dataset_id are
    // those of the condition's last trial. (M58)
    private void WriteQuestionnaireCsv()
    {
        if (postConditionQuestionnaire == null || _qnResponses == null || _result == null) return;
        var (audio, visual) = CueLabels(_result.condition);
        string order = (_result.orderIndex + 1).ToString();
        string now = DateTime.UtcNow.ToString("o");
        for (int i = 0; i < postConditionQuestionnaire.Count; i++)
        {
            var it = postConditionQuestionnaire.items[i];
            AppendCsvRow(ParticipantLabel, "questionnaires.csv", QuestionnaireHeader, new[]
            {
                ParticipantLabel, _result.condition, audio, visual, order, _result.datasetName,
                it.instrument, it.subscale, it.id, it.text,
                it.scale == QuestionnaireScale.Tlx21 ? "0-100" : "1-5",
                _qnResponses[i].ToString(), now
            });
        }
    }

    private static readonly string[] BackgroundHeader =
        { "participant_id", "item_id", "item_text", "response", "timestamp", "datetime_local" };

    // M61: one row per background question (VR experience, age range), answer as its text.
    private void WriteBackgroundCsv()
    {
        if (_qnDef == null || _qnResponses == null) return;
        string now = DateTime.UtcNow.ToString("o"), local = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        for (int i = 0; i < _qnDef.Count; i++)
        {
            var it = _qnDef.items[i];
            int r = _qnResponses[i];
            string answer = it.scale == QuestionnaireScale.Choice && it.options != null && r >= 0 && r < it.options.Count
                ? it.options[r] : r.ToString();
            AppendCsvRow(ParticipantLabel, "background.csv", BackgroundHeader,
                         new[] { ParticipantLabel, it.id, it.text, answer, now, local });
        }
    }

    private void WriteRetraceCsv(TrialResult r)
    {
        var (audio, visual) = CueLabels(r.condition);
        string order = (r.orderIndex + 1).ToString();

        // M51: samples are kept in WORLD space in memory (the retrace-review graph draws
        // them back in place), but logged in the ExperimentArea frame -- head_x = lateral,
        // head_z = along the walk, relative to the area centre -- because the area is now
        // re-anchored to wherever the participant stood at Start Session. With the area at
        // the origin facing +Z (the pre-M51 layout) this is identical to world coords, so
        // analyze_study.py (x = lateral, z = forward) and older data stay comparable.
        if (experimentArea == null) experimentArea = FindAnyObjectByType<ExperimentArea>();

        foreach (var s in r.retracePath)
        {
            Vector3 p = new Vector3(s.x, s.y, s.z);
            if (experimentArea != null) p = experimentArea.WorldToArea(p);

            AppendCsvRow(ParticipantLabel, DataFile("retracing_log.csv", r.isPractice), RetraceHeader, new[]
            {
                ParticipantLabel, r.condition, audio, visual, order, r.datasetName,
                s.timestampUtc, s.t.ToString("0.###", CultureInfo.InvariantCulture),
                p.x.ToString("0.###", CultureInfo.InvariantCulture),
                p.y.ToString("0.###", CultureInfo.InvariantCulture),
                p.z.ToString("0.###", CultureInfo.InvariantCulture)
            });
        }
    }

    // audio_cue/visual_cue ("semantic"/"plain"/"none") per the modified plan's condition
    // mapping (ROADMAP M4): Abstract = plain sphere + plain beep; Representative = semantic
    // spark + semantic electric buzz; SemanticAudio = plain sphere +
    // semantic electric buzz; SemanticVisual = semantic spark + plain
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
            case "Tutorial": return ("tutorial", "tutorial");
            default: return ("unknown", "unknown");
        }
    }

    // M60: tutorial trials go to practice_*.csv so the analysis never sees them.
    private static string DataFile(string name, bool practice) => practice ? "practice_" + name : name;

    private static void AppendCsvRow(string participantLabel, string fileName, string[] header, string[] row)
    {
        try
        {
            string path = Path.Combine(ParticipantDir(participantLabel), fileName);
            bool exists = File.Exists(path);

            using (var w = new StreamWriter(path, append: true))
            {
                if (!exists) w.WriteLine(string.Join(",", Array.ConvertAll(header, EscapeCsv)));
                w.WriteLine(string.Join(",", Array.ConvertAll(row, EscapeCsv)));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] Failed to write {fileName} for {participantLabel}: {e.Message}");
        }
    }

    private static string EscapeCsv(string value)
    {
        value ??= "";
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    /// <summary>The root StudyData folder (holds one subfolder per participant).</summary>
    public static string StudyDataDir()
    {
        string dir = Path.Combine(Application.persistentDataPath, "StudyData");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>One participant's own folder (StudyData/p01/, etc.), created on first use.
    /// Each participant's trials/questions/retrace CSVs live only here, self-contained --
    /// so a single participant's folder can be copied, zipped, or deleted independently
    /// of everyone else's data.</summary>
    public static string ParticipantDir(string participantLabel)
    {
        string dir = Path.Combine(StudyDataDir(), participantLabel);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // The raw CSV files SessionController writes, inside each participant's own folder.
    // "demographics.csv" is included only so a settings-menu clear picks up any leftover
    // file from before the participant demographics form was removed from the study --
    // nothing writes it any more.
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
        string baseDir = StudyDataDir();
        var lines = new List<string>();

        // Legacy flat files directly in StudyData/, from before per-participant
        // folders existed (ROADMAP M28) -- still surfaced here so "Clear Study Data"
        // has full visibility into everything it's about to delete.
        var legacy = new List<string>();
        foreach (var f in StudyDataFiles)
        {
            string p = Path.Combine(baseDir, f);
            if (!File.Exists(p)) continue;
            int rows = Math.Max(0, File.ReadAllLines(p).Length - 1);
            legacy.Add($"{f}: {rows} row(s)");
        }
        if (legacy.Count > 0)
            lines.Add("(legacy, pre-per-participant) -- " + string.Join(", ", legacy));

        var participantDirs = Directory.GetDirectories(baseDir);
        Array.Sort(participantDirs);
        foreach (var pdir in participantDirs)
        {
            string label = Path.GetFileName(pdir);
            var fileSummaries = new List<string>();
            foreach (var f in StudyDataFiles)
            {
                string p = Path.Combine(pdir, f);
                if (!File.Exists(p)) continue;
                int rows = Math.Max(0, File.ReadAllLines(p).Length - 1);   // minus header
                fileSummaries.Add($"{f}: {rows} row(s)");
            }
            if (fileSummaries.Count > 0)
                lines.Add($"{label} -- " + string.Join(", ", fileSummaries));
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "No study data recorded yet.";
    }

    /// <summary>True if any legacy flat file or participant folder has recorded CSV data
    /// (so the UI can grey out/hide "Clear" when there's nothing to clear).</summary>
    public static bool HasStudyData()
    {
        string baseDir = StudyDataDir();
        foreach (var f in StudyDataFiles)
            if (File.Exists(Path.Combine(baseDir, f))) return true;   // legacy flat file
        foreach (var pdir in Directory.GetDirectories(baseDir))
            foreach (var f in StudyDataFiles)
                if (File.Exists(Path.Combine(pdir, f))) return true;
        return false;
    }

    /// <summary>Permanently deletes every recorded CSV file -- any legacy flat files directly
    /// in StudyData/, plus trials/questions/retrace (and any leftover demographics.csv) inside
    /// every participant folder -- then removes any participant folder left empty. Intended
    /// for clearing out test/pilot runs from the settings menu before real data collection --
    /// irreversible, so the UI must confirm before calling this.</summary>
    public static string ClearAllStudyData()
    {
        string baseDir = StudyDataDir();
        int deletedFiles = 0;
        int touchedParticipants = 0;

        // Legacy flat files directly in StudyData/ (pre-per-participant).
        foreach (var f in StudyDataFiles)
        {
            string p = Path.Combine(baseDir, f);
            if (File.Exists(p)) { File.Delete(p); deletedFiles++; }
        }

        foreach (var pdir in Directory.GetDirectories(baseDir))
        {
            bool any = false;
            foreach (var f in StudyDataFiles)
            {
                string p = Path.Combine(pdir, f);
                if (File.Exists(p)) { File.Delete(p); deletedFiles++; any = true; }
            }
            if (any) touchedParticipants++;

            // Remove the now-empty participant folder too, so old test participants
            // don't linger and skew SuggestNextParticipantId.
            try
            {
                if (Directory.GetFileSystemEntries(pdir).Length == 0) Directory.Delete(pdir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] Could not remove empty folder {pdir}: {e.Message}");
            }
        }
        Debug.Log($"[Session] Cleared {deletedFiles} study data file(s) across {touchedParticipants} participant folder(s) (plus any legacy files) from {baseDir}");
        return deletedFiles > 0
            ? $"Cleared {deletedFiles} file(s) across {touchedParticipants} participant folder(s) (including any legacy files)."
            : "No study data found.";
    }
}
