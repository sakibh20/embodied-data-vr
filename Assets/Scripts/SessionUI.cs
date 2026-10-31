using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;   // XRUIInputModule, TrackedDeviceGraphicRaycaster
using System.Globalization;

/// <summary>
/// World-anchored in-VR UI for the study (M51: was head-locked). The panel is placed
/// in front of the participant whenever it appears or the phase changes, then stays
/// fixed in the world; it only re-centres if they turn well away from it. Instantiates a SessionCanvasView prefab (the
/// panel's whole look — title/body/scrollable content area — is authored in the
/// Editor, not built in code) and populates it per SessionController phase change:
/// stacks SessionOptionButton items for phase actions / multiple-choice recall
/// options, or a single SessionAnswerInput for free-text recall answers when
/// RunSettings.recallInputMode is TextEntry. Works with a mouse on desktop and with
/// an XR controller ray via TrackedDeviceGraphicRaycaster/XRUIInputModule.
/// The panel is only ever visible when a real interaction is expected of the
/// participant (Idle's start/audio-mode picker, Distractor's "Done", Recall's MCQ/text
/// entry, Complete) — it is fully hidden (GameObject.SetActive(false)) during Walk and
/// Retrace, since those phases show no UI at all and would otherwise occlude the floor
/// graph while the participant is looking at/re-walking it (ROADMAP.md M12); see
/// SessionController.CheckAutoEndOfPhase for how those two phases end without a button.
/// Recall answers are never skippable: MCQ requires clicking a real option, and the
/// free-text entry rejects an empty/unparseable submission in place (with inline
/// feedback) rather than letting it advance as a blank answer.
/// (Clearing test/pilot CSV data is a Unity Editor menu action, not part of this
/// runtime UI — see Assets/Editor/StudyDataMenu.cs, "Tools/Study Data".)
/// </summary>
public class SessionUI : MonoBehaviour
{
    [SerializeField] private SessionController session;
    [Tooltip("Session-wide audio cue mode selector, shown on the Idle screen. Auto-found if empty.")]
    [SerializeField] private AudioModeSelector audioMode;

    [Header("Prefabs (Assets/Prefabs/UI)")]
    [SerializeField] private SessionCanvasView canvasPrefab;
    [SerializeField] private SessionOptionButton optionButtonPrefab;
    [SerializeField] private SessionAnswerInput answerInputPrefab;

    [Header("Placement (world-anchored, M51)")]
    [Tooltip("Where the panel is placed relative to the head when it appears: x = right, " +
             "y = up, z = forward (metres). Facing is yaw-only so it stays upright.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.15f, 2.7f);
    [SerializeField] private float canvasScale = 0.0025f;
    [Tooltip("If the panel is more than this many degrees (left/right) from where the " +
             "participant is looking for refollowDelay seconds, it re-centres in front of " +
             "them. 0 = never (fully fixed until the next phase).")]
    [SerializeField] private float refollowAngle = 55f;
    [SerializeField] private float refollowDelay = 0.8f;

    private bool _wasVisible;
    private SessionPhase _placedPhase;
    private float _offViewTime;

    [Header("Retrace")]
    [Tooltip("How long the \"walk back to the start, then retrace\" instruction stays " +
             "on screen at the start of Retrace before the panel hides itself (same as " +
             "Walk) so it doesn't occlude the floor while the participant actually " +
             "retraces. See ROADMAP.md M36.")]
    [SerializeField] private float retraceInstructionSeconds = 4f;
    [Tooltip("How long the \"here's what you traced\" panel stays on screen at the start " +
             "of RetraceReview before hiding itself, so it doesn't block the view of the " +
             "floor graph for the rest of the review window (SessionController's " +
             "retraceReviewSeconds). See ROADMAP.md M44.")]
    [SerializeField] private float retraceReviewPanelSeconds = 2.5f;

    private SessionCanvasView _view;
    private readonly List<GameObject> _items = new List<GameObject>();

    private void Awake()
    {
        if (session == null) session = FindAnyObjectByType<SessionController>();
        if (audioMode == null) audioMode = FindAnyObjectByType<AudioModeSelector>();

        EnsureEventSystem();
        BuildCanvas();
    }

    private void OnEnable()
    {
        if (session != null) session.PhaseChanged += Render;
    }

    private void OnDisable()
    {
        if (session != null) session.PhaseChanged -= Render;
    }

    private void Start()
    {
        Render(session != null ? session.Phase : SessionPhase.Idle);
    }

    // =====================================================================
    // Phase rendering
    // =====================================================================

    private void Render(SessionPhase phase)
    {
        CancelInvoke(nameof(HidePanel));   // a pending Retrace auto-hide must never fire into a later phase
        ClearItems();
        if (_view == null) return;

        // M66: the researcher menu overrides every phase while open.
        if (_menuOpen)
        {
            _view.gameObject.SetActive(true);
            RenderResearcherMenu();
            return;
        }

        // Walk has no interaction and no instruction -- hidden immediately (unchanged).
        // Retrace briefly SHOWS an instruction (M36: "walk back to the start, then
        // retrace") since nothing else tells the participant they need to physically
        // return to the start before the shape auto-completes (see M33) -- then hides
        // itself the same way Walk does, so it doesn't occlude the floor graph while
        // they actually retrace. Idle/Distractor/Recall/Complete all need a real
        // interaction (button or MCQ), so the panel stays visible there.
        // Retrace in two steps, like the between-trials screen: first "walk back to the
        // Start marker"; once on it, "walk the shape from memory" + a Start button; after
        // Start the panel hides so it doesn't cover the floor.
        if (phase == SessionPhase.Retrace)
        {
            if (session.RetraceStarted) { _view.gameObject.SetActive(false); return; }
            _view.gameObject.SetActive(true);
            string tag = session.TrialTag + "   ";
            _view.Title.text = tag + "Retrace";
            if (session.RetraceReady)
            {
                _view.Body.text = "Now walk the shape again <b>from memory</b>, to the red <b>End</b> marker.\n\n" +
                                  "Press <b>Start</b> when you're ready.";
                Cta(AddOptionButton("Start", () => session.BeginRetrace()));
            }
            else
            {
                _view.Body.text = (session.CountingSeconds > 0f ? "<b>Stop counting.</b>\n\n" : "") +
                                  "Walk back to the green <b>Start</b> marker.";
            }
            return;
        }

        // RetraceReview: the floor now shows the participant's own traced shape
        // (GraphManager.ShowRetraceGraph) -- briefly say so, then get the head-locked
        // panel out of the way for the rest of the review window so it doesn't occlude
        // that shape, the same way Retrace's own instruction hides itself. See
        // ROADMAP.md M44.
        // M59 (supervisor): no instruction during the review any more -- the panel
        // stays hidden and the traced shape is simply shown on the floor.
        if (phase == SessionPhase.RetraceReview)
        {
            _view.gameObject.SetActive(false);
            return;
        }

        bool showPanel = phase != SessionPhase.Walk;
        _view.gameObject.SetActive(showPanel);
        if (!showPanel) return;

        string trialTag = (phase != SessionPhase.Idle && phase != SessionPhase.Complete &&
                           phase != SessionPhase.Consent && phase != SessionPhase.Instructions)
            ? session.TrialTag + "   " : "";

        switch (phase)
        {
            case SessionPhase.Idle:
                if (_settingsOpen) { RenderSettings(); break; }
                _view.Title.text = "Data Physicalisation Study";
                {
                    string body = $"Participant: <b>{session.ParticipantLabel}</b> (auto-suggested from existing data -- adjust below if this is wrong)\n";
                    if (session.ParticipantHasData())
                        body = $"<color=#FF6B6B><b>{session.ParticipantLabel} already has recorded data.</b> Pick another id unless you are deliberately continuing this participant.</color>\n" + body;
                    var v = RuntimeSettings.Current;
                    if (v != null)
                        body += $"<size=80%>Walk {v.walkLength:0.0} m · {v.trialsPerCondition} trial(s)/condition · {v.conditionOrder} · " +
                                $"tutorial {(v.tutorial ? "on" : "off")} · audio {ModeLabel(v.audioMode)}</size>";
                    _view.Body.text = body;
                }
                AddOptionButton($"−  Participant  ({session.ParticipantLabel})",
                          () => { session.DecrementParticipantId(); Render(SessionPhase.Idle); });
                AddOptionButton($"+  Participant  ({session.ParticipantLabel})",
                          () => { session.IncrementParticipantId(); Render(SessionPhase.Idle); });
                AddOptionButton("Settings", () => { _settingsOpen = true; Render(SessionPhase.Idle); });
                Cta(AddOptionButton("Start Session", () => session.StartSession()));
                break;

            case SessionPhase.Distractor:
                _view.Title.text = trialTag + "Counting task";
                if (session.CountingSeconds > 0f)
                {
                    // Timed (ends by itself, see SessionController.EndWalk).
                    _view.Body.text = $"Count out loud backwards from <b>{session.DistractorStartNumber}</b> " +
                                      "in steps of 3.\n\nKeep counting until the screen tells you to stop.";
                }
                else
                {
                    _view.Body.text = $"Count out loud backwards from <b>{session.DistractorStartNumber}</b> " +
                                      "in steps of 3, until told to stop.";
                    Cta(AddOptionButton("Done", () => session.EndDistractor()));
                }
                break;

            case SessionPhase.Recall:
                RenderRecall(trialTag);
                break;

            case SessionPhase.Questionnaire:
                RenderQuestionnaire();
                break;

            case SessionPhase.Consent:
                RenderConsent();
                break;

            case SessionPhase.Instructions:
            {
                var rs = StudyConfig.Settings;
                _view.Title.text = rs != null ? rs.instructionsTitle : "Instructions";
                // First paragraph in the panel's Body, the rest below it (as on the consent screen).
                string txt = rs != null ? rs.instructionsText ?? "" : "";
                int cut = txt.IndexOf("\n\n", System.StringComparison.Ordinal);
                _view.Body.text = cut >= 0 ? txt.Substring(0, cut) : "";
                AddTextRow(cut >= 0 ? txt.Substring(cut + 2) : txt, 28, TextAlignmentOptions.Left, 300);
                var row = AddRow(68, 40);
                Cta(AddButtonTo(row, "Start the tutorial", () => session.BeginTutorial(), false, true));
                break;
            }

            case SessionPhase.TrialComplete:
                if (session.TutorialJustCompleted)
                {
                    // M60: end of the tutorial.
                    var rs = StudyConfig.Settings;
                    _view.Title.text = rs != null ? rs.tutorialCompleteTitle : "Tutorial complete";
                    string done = rs != null ? rs.tutorialCompleteText : "You have now completed the tutorial.";
                    if (session.ReadyToStartNext)
                    {
                        _view.Body.text = done + "\n\nPress Start the experiment when you're ready.";
                        Cta(AddOptionButton("Start the experiment", () => session.StartNextTrial()));
                    }
                    else
                        _view.Body.text = done + "\n\nWalk back to the <b>Start</b> marker to continue.";
                    break;
                }
                _view.Title.text = trialTag + (session.IsPracticeTrial ? "Practice complete" : "Trial complete");
                if (session.ReadyToStartNext)
                {
                    _view.Body.text = "Nice work! Press Start Next Trial when you're ready to continue.";
                    Cta(AddOptionButton("Start Next Trial", () => session.StartNextTrial()));
                }
                else
                {
                    _view.Body.text = "Trial complete. Walk back to the <b>Start</b> marker to continue.";
                }
                break;

            case SessionPhase.Complete:
                _view.Title.text = "Session complete";
                _view.Body.text = StudyConfig.Settings != null ? StudyConfig.Settings.completeText
                                                                : "Thank you! You can remove the headset.";
                Cta(AddOptionButton("Next Participant", () => session.ReturnToIdle()));
                break;
        }
    }

    // =====================================================================
    // Researcher menu (M66): hold the LEFT controller's menu button for 1.5 s
    // (Esc on a keyboard) at any time. Participants won't trigger it by accident.
    // =====================================================================

    [Header("Researcher menu (M66)")]
    [SerializeField] private float menuHoldSeconds = 1.5f;
    private bool _menuOpen;
    private string _menuConfirm = "";   // "", "end", "exit"
    private float _menuHold;
    private bool _menuHoldFired;
    private float _sessionStartTime = -1f;

    private void Update()
    {
        bool held = false, tapped = false;
        var left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool m)) held = m;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) tapped = true;

        if (held) { _menuHold += Time.unscaledDeltaTime; if (!_menuHoldFired && _menuHold >= menuHoldSeconds) { _menuHoldFired = true; tapped = true; } }
        else { _menuHold = 0f; _menuHoldFired = false; }

        if (tapped) SetMenuOpen(!_menuOpen);
        if (session != null && session.InSession && _sessionStartTime < 0f) _sessionStartTime = Time.unscaledTime;
        if (session != null && !session.InSession) _sessionStartTime = -1f;
    }

    // M66: a red "End session" button pinned to the top-right corner of the panel,
    // visible whenever the panel is shown during a session (not inside the menus).
    // It opens a confirm screen first, so a stray click never ends a session.
    private SessionOptionButton _quitButton;
    private bool _fromQuitButton;   // Cancel then goes straight back to the session

    private void UpdateQuitButton()
    {
        if (_quitButton == null)
        {
            if (optionButtonPrefab == null) return;
            _quitButton = Instantiate(optionButtonPrefab, _view.transform);
            var rt = (RectTransform)_quitButton.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(250f, 56f);
            rt.anchoredPosition = new Vector2(-20f, -18f);
            var le = _quitButton.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            if (_quitButton.Label != null) { _quitButton.Label.text = "End session"; _quitButton.Label.enableAutoSizing = false; _quitButton.Label.fontSize = 24; }
            if (_quitButton.Button != null)
                _quitButton.Button.onClick.AddListener(() => { _menuOpen = true; _menuConfirm = "end"; _fromQuitButton = true; Render(session.Phase); });
            Danger(_quitButton);
        }
        bool show = session != null && session.InSession && !_menuOpen && !_settingsOpen;
        if (_quitButton.gameObject.activeSelf != show) _quitButton.gameObject.SetActive(show);
        _quitButton.transform.SetAsLastSibling();
    }

    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        _menuConfirm = "";
        _fromQuitButton = false;
        Render(session != null ? session.Phase : SessionPhase.Idle);
        if (open) PlaceInFrontOfHead();
    }

    private void RenderResearcherMenu()
    {
        _view.Title.text = "Researcher menu";
        string elapsed = _sessionStartTime >= 0f ? $"{(Time.unscaledTime - _sessionStartTime) / 60f:0} min" : "-";
        string status = session.InSession
            ? $"{session.ParticipantLabel} · {session.Phase} · {(session.TotalTrials > 0 && session.TrialNumber > 0 ? session.TrialTag : "-")} · {elapsed}"
            : $"No session running · next participant {session.ParticipantLabel}";
        _view.Body.text = status;

        if (_menuConfirm == "end")
        {
            AddTextRow("End this session now? Everything recorded so far stays saved; the current trial is not completed.", 26, TextAlignmentOptions.Center, 80);
            var r = AddRow(68, 40);
            Secondary(AddButtonTo(r, "Cancel", () =>
            {
                if (_fromQuitButton) SetMenuOpen(false);
                else { _menuConfirm = ""; Render(session.Phase); }
            }, false, true));
            Danger(AddButtonTo(r, "Yes, end session", () => { session.AbortSession(); SetMenuOpen(false); }, false, true));
            return;
        }
        if (_menuConfirm == "exit")
        {
            AddTextRow(session.InSession ? "Close the app? The running session ends here (data so far stays saved)." : "Close the app?", 26, TextAlignmentOptions.Center, 80);
            var r = AddRow(68, 40);
            Secondary(AddButtonTo(r, "Cancel", () => { _menuConfirm = ""; Render(session.Phase); }, false, true));
            Danger(AddButtonTo(r, "Yes, close the app", () => { if (session.InSession) session.AbortSession(); Application.Quit(); }, false, true));
            return;
        }

        var r1 = AddRow(64, 20);
        Cta(AddButtonTo(r1, "Continue", () => SetMenuOpen(false), false, true));
        AddButtonTo(r1, "Skip this step", () => { session.SkipStep(); SetMenuOpen(false); }, false, session.CanSkipStep);
        var r2 = AddRow(64, 20);
        AddButtonTo(r2, "Re-centre area here", () => { session.RecentreArea(); SetMenuOpen(false); }, false, session.CanRecentre);
        Danger(AddButtonTo(r2, "End session", () => { _menuConfirm = "end"; Render(session.Phase); }, false, session.InSession));
        var r3 = AddRow(64, 20);
        AddButtonTo(r3, "Settings", () => { _menuOpen = false; _settingsOpen = true; Render(session.Phase); }, false, !session.InSession);
        AddButtonTo(r3, "Close app", () => { _menuConfirm = "exit"; Render(session.Phase); }, false, true);
        AddTextRow("<size=80%>Skip: Walk / counting / Retrace / review / between trials. Re-centre: only between trials. Settings: only when no session is running.</size>",
                   22, TextAlignmentOptions.Center, 60);
    }

    // =====================================================================
    // In-app settings (M64): researcher-only, reachable from the Idle screen.
    // Each setting is a  label  <  value  >  stepper; two per row.
    // =====================================================================

    private bool _settingsOpen;

    private void RenderSettings()
    {
        var v = RuntimeSettings.Current;
        _view.Title.text = "Study settings";
        _view.Body.text = "Saved on this headset and used from the next session on." +
                          (RuntimeSettings.HasSavedChanges ? "" : "  <size=80%>(currently the defaults)</size>");
        if (v == null) return;

        void Changed() { RuntimeSettings.Save(); RuntimeSettings.RefreshLayoutPreview(); Render(SessionPhase.Idle); }

        var r1 = AddSettingsRow();
        AddStepper(r1, "Walk length", $"{v.walkLength:0.0} m",
            d => { v.walkLength = RuntimeSettings.Step(v.walkLength, 0.5f, 2f, 15f, d); Changed(); });
        AddStepper(r1, "Area width", $"{v.areaWidth:0.0} m",
            d => { v.areaWidth = RuntimeSettings.Step(v.areaWidth, 0.5f, 2f, 10f, d); Changed(); });

        var r2 = AddSettingsRow();
        AddStepper(r2, "Marker gap", $"{v.markerGap:0.0} m",
            d => { v.markerGap = RuntimeSettings.Step(v.markerGap, 0.1f, 0f, 1.5f, d); Changed(); });
        AddStepper(r2, "Trials / condition", v.trialsPerCondition.ToString(),
            d => { v.trialsPerCondition = Mathf.Clamp(v.trialsPerCondition + d, 1, 10); Changed(); });

        var r3 = AddSettingsRow();
        AddStepper(r3, "Condition order", OrderLabel(v.conditionOrder),
            d => { v.conditionOrder = RuntimeSettings.Cycle(v.conditionOrder, d); Changed(); });
        AddStepper(r3, "Recall answers", v.recallInput == RecallInputMode.TextEntry ? "Number pad" : "4 choices",
            d => { v.recallInput = RuntimeSettings.Cycle(v.recallInput, d); Changed(); });

        var r4 = AddSettingsRow();
        AddStepper(r4, "Audio cue", ModeLabel(v.audioMode),
            d => { v.audioMode = RuntimeSettings.Cycle(v.audioMode, d); Changed(); });
        AddStepper(r4, "Spark look", SparkLabel(v.sparkVariant),
            d => { v.sparkVariant = RuntimeSettings.Cycle(v.sparkVariant, d); Changed(); });

        var r5 = AddSettingsRow();
        AddStepper(r5, "Tutorial", v.tutorial ? "On" : "Off",
            d => { v.tutorial = !v.tutorial; Changed(); });
        AddStepper(r5, "Dataset order", v.datasetOrder == DatasetOrderMode.LatinSquareSets ? "Latin sq. sets" : "Rotate",
            d => { v.datasetOrder = RuntimeSettings.Cycle(v.datasetOrder, d); Changed(); });   // M69

        // M66: study data on this device
        string summary = SessionController.StudyDataShortSummary(out int nParticipants);
        if (_confirmClear)
        {
            AddTextRow($"<color=#FF6B6B><b>Delete ALL study data on this headset?</b></color> {summary}. This cannot be undone -- copy it to the PC first.",
                       24, TextAlignmentOptions.Center, 64);
            var c = AddRow(60, 40);
            Secondary(AddButtonTo(c, "Cancel", () => { _confirmClear = false; Render(SessionPhase.Idle); }, false, true));
            Danger(AddButtonTo(c, "Yes, delete everything", () =>
            {
                _clearResult = SessionController.ClearAllStudyData();
                _confirmClear = false;
                session.RefreshSuggestedParticipant();
                Render(SessionPhase.Idle);
            }, false, true));
            return;
        }
        var data = AddRow(60, 20);
        AddText(data, $"Study data: {summary}" + (string.IsNullOrEmpty(_clearResult) ? "" : $"  <size=80%>({_clearResult})</size>"),
                22, TextAlignmentOptions.Left);
        Danger(AddButtonTo(data, "Delete all data…", () => { _confirmClear = true; _clearResult = ""; Render(SessionPhase.Idle); }, false, nParticipants > 0))
            .GetComponent<LayoutElement>().flexibleWidth = 0.5f;

        var nav = AddRow(64, 40);
        Secondary(AddButtonTo(nav, "Reset to defaults", () => { RuntimeSettings.ResetToDefaults(); RuntimeSettings.RefreshLayoutPreview(); Render(SessionPhase.Idle); }, false, true));
        Cta(AddButtonTo(nav, "Done", () => { _settingsOpen = false; _clearResult = ""; Render(SessionPhase.Idle); }, false, true));
    }

    private bool _confirmClear;
    private string _clearResult = "";

    private RectTransform AddSettingsRow()
    {
        var row = AddRow(60, 10);
        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        return row;
    }

    // label (flexible) | < | value | >
    private void AddStepper(RectTransform row, string label, string value, System.Action<int> onStep)
    {
        var l = AddText(row, label, 24, TextAlignmentOptions.Right);
        var le = l.GetComponent<LayoutElement>(); le.flexibleWidth = 1f; le.minWidth = 150f;
        Fixed(AddButtonTo(row, "<", () => onStep(-1), false, true).GetComponent<LayoutElement>(), 60f);
        var t = AddText(row, $"<b>{value}</b>", 24, TextAlignmentOptions.Center);
        Fixed(t.GetComponent<LayoutElement>(), 190f);
        Fixed(AddButtonTo(row, ">", () => onStep(+1), false, true).GetComponent<LayoutElement>(), 60f);
    }

    private static void Fixed(LayoutElement le, float w) { le.flexibleWidth = 0f; le.minWidth = w; le.preferredWidth = w; }

    private static string OrderLabel(ConditionOrderMode m)
    {
        switch (m)
        {
            case ConditionOrderMode.LatinSquare: return "Latin square";
            case ConditionOrderMode.RandomExceptControlFirst: return "Control first";
            case ConditionOrderMode.Random: return "Random";
            default: return "Fixed";
        }
    }

    private static string SparkLabel(SemanticVisualVariant v)
    {
        switch (v)
        {
            case SemanticVisualVariant.SparkOrb: return "B: orb";
            case SemanticVisualVariant.SparkStarGlow: return "C: star+glow";
            default: return "A: star";
        }
    }

    private void HidePanel()
    {
        if (_view != null) _view.gameObject.SetActive(false);
    }

    private static string ModeLabel(CueAudioController.AudioMode m)
    {
        switch (m)
        {
            case CueAudioController.AudioMode.PitchOnly: return "Pitch only";
            case CueAudioController.AudioMode.TempoOnly: return "Tempo only";
            default: return "Both";
        }
    }

    // Leading check mark on the currently-selected mode button.
    private static string Tick(CueAudioController.AudioMode current, CueAudioController.AudioMode option)
        => current == option ? "[x] " : "";   // the UI font has no check-mark glyph

    private void RenderRecall(string trialTag)
    {
        RecallQuestion q = session.CurrentQuestion;
        if (q == null) { _view.Body.text = ""; return; }

        _view.Title.text = $"{trialTag}Question {session.QuestionNumber}/{session.QuestionCount}";
        _view.Body.text = q.prompt;

        RecallInputMode mode = StudyConfig.Settings != null
            ? StudyConfig.Settings.recallInputMode
            : RecallInputMode.MultipleChoice;

        if (mode == RecallInputMode.TextEntry)
        {
            AddAnswerInput(q);
        }
        else
        {
            for (int i = 0; i < q.options.Length; i++)
            {
                int choice = i;   // capture
                AddOptionButton(q.options[i], () => session.AnswerCurrentQuestion(choice));
            }
        }
    }

    // =====================================================================
    // Post-condition questionnaire (M58): one item per screen, answered by pointing.
    // =====================================================================

    // =====================================================================
    // Consent (M59): shown once before the first trial.
    // =====================================================================

    private void RenderConsent()
    {
        var c = session.ConsentForm;
        if (c == null) return;
        _view.Title.text = c.title;

        if (session.ConsentDeclined)
        {
            _view.Body.text = c.declinedMessage;
            var back = AddRow(68, 40);
            Cta(AddButtonTo(back, "Back to start screen", () => session.ConsentBackToIdle(), false, true));
            return;
        }

        // M61: supervisor's wording -- study name, the five statements, a tick box and
        // Continue (ticked = "I consent", unticked = "I do not consent").
        _view.Body.text = c.subtitle;
        AddTextRow(c.body, 27, TextAlignmentOptions.Left, 330);
        bool on = session.ConsentChecked;
        var tick = AddRow(76, 16);
        tick.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;   // keep the box square
        var box = AddButtonTo(tick, on ? "<b>X</b>" : "", () => session.ToggleConsentCheck(), on, true);
        var boxLe = box.GetComponent<LayoutElement>();
        boxLe.flexibleWidth = 0f; boxLe.minWidth = 76f; boxLe.preferredWidth = 76f;
        AddButtonTo(tick, c.checkboxLabel, () => session.ToggleConsentCheck(), false, true);
        var row = AddRow(68, 40);
        Cta(AddButtonTo(row, c.continueLabel, () => session.ConsentContinue(), false, true));
    }

    // M66: call-to-action buttons (continue / next / start) are green, secondary actions
    // (back / cancel) grey, destructive ones red; answer buttons keep the prefab blue.
    private static readonly Color CtaColor = new Color(0.20f, 0.55f, 0.30f, 1f);
    private static readonly Color SecondaryColor = new Color(0.36f, 0.38f, 0.42f, 1f);
    private static readonly Color DangerColor = new Color(0.72f, 0.22f, 0.20f, 1f);

    private static SessionOptionButton Tint(SessionOptionButton b, Color c)
    {
        if (b != null && b.Button != null && b.Button.targetGraphic != null) b.Button.targetGraphic.color = c;
        return b;
    }
    private static SessionOptionButton Cta(SessionOptionButton b) => Tint(b, CtaColor);
    private static SessionOptionButton Secondary(SessionOptionButton b) => Tint(b, SecondaryColor);
    private static SessionOptionButton Danger(SessionOptionButton b) => Tint(b, DangerColor);

    private static readonly Color SelectedColor = new Color(1f, 0.74f, 0.2f, 1f);

    private void RenderQuestionnaire()
    {
        var def = session.Questionnaire;

        // M59: the instruction gets its own screen, right after the recall questions;
        // the items start when the participant presses Start.
        if (session.QnShowingIntro)
        {
            _view.Title.text = def != null ? def.title : "Questionnaire";
            _view.Body.text = def != null ? def.intro : "";
            var startRow = AddRow(68, 40);
            Cta(AddButtonTo(startRow, "Start", () => session.QnBeginItems(), false, true));
            return;
        }

        QuestionnaireItem item = session.CurrentQnItem;
        if (item == null) { _view.Body.text = ""; return; }

        _view.Title.text = $"{(def != null ? def.title : "Questionnaire")}   ({session.QnNumber}/{session.QnCount})";
        _view.Body.text = item.text;

        int current = session.CurrentQnResponse;

        if (item.scale == QuestionnaireScale.Agree5)
        {
            var row = AddRow(84, 16);
            for (int v = 1; v <= 5; v++)
            {
                int value = v;
                AddButtonTo(row, v.ToString(), () => session.SetQnResponse(value), current == value, true);
            }
            AddAnchorRow(item.lowAnchor, "", item.highAnchor);
        }
        else if (item.scale == QuestionnaireScale.Choice)
        {
            // M61: one button per option, two per row.
            var opts = item.options ?? new System.Collections.Generic.List<string>();
            for (int i = 0; i < opts.Count; i += 2)
            {
                var row = AddRow(64, 16);
                int a = i;
                AddButtonTo(row, opts[a], () => session.SetQnResponse(a), current == a, true);
                if (i + 1 < opts.Count)
                {
                    int b = i + 1;
                    AddButtonTo(row, opts[b], () => session.SetQnResponse(b), current == b, true);
                }
                else AddText(row, "", 24, TextAlignmentOptions.Left);
            }
        }
        else // Tlx21: 0..100 in steps of 5, like the 21 tick marks of the paper NASA-TLX
        {
            var row = AddRow(84, 4);
            for (int k = 0; k <= 20; k++)
            {
                int value = k * 5;
                AddButtonTo(row, "", () => session.SetQnResponse(value), current == value, true);
            }
            AddAnchorRow(item.lowAnchor, "", item.highAnchor);   // M59: no "Selected: x" readout
        }

        var nav = AddRow(68, 40);
        Secondary(AddButtonTo(nav, "Back", () => session.QnBack(), false, session.QnNumber > 1));
        Cta(AddButtonTo(nav, session.QnNumber < session.QnCount ? "Next" : "Finish",
                    () => session.QnNext(), false, current >= 0));
    }

    // A horizontal row inside the scrollable content column.
    private RectTransform AddRow(float height, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_view.Content, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        _items.Add(go);
        return rt;
    }

    private SessionOptionButton AddButtonTo(RectTransform parent, string label,
                                            UnityEngine.Events.UnityAction onClick,
                                            bool selected, bool interactable)
    {
        var b = Instantiate(optionButtonPrefab, parent);
        var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 0f;
        if (b.Label != null)
        {
            b.Label.text = selected && !string.IsNullOrEmpty(label) ? $"<b>{label}</b>" : label;
            b.Label.enableAutoSizing = false;
        }
        if (b.Button != null)
        {
            b.Button.onClick.AddListener(onClick);
            b.Button.interactable = interactable;
            if (selected && b.Button.targetGraphic != null) b.Button.targetGraphic.color = SelectedColor;
        }
        return b;
    }

    private void AddAnchorRow(string left, string centre, string right)
    {
        var row = AddRow(40, 10);
        AddText(row, left, 24, TextAlignmentOptions.Left);
        AddText(row, centre, 24, TextAlignmentOptions.Center);
        AddText(row, right, 24, TextAlignmentOptions.Right);
    }

    private void AddTextRow(string text, float size, TextAlignmentOptions align, float height)
    {
        var row = AddRow(height, 0);
        AddText(row, text, size, align);
    }

    private TextMeshProUGUI AddText(RectTransform parent, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_view.Body != null) { t.font = _view.Body.font; t.color = _view.Body.color; }
        t.fontSize = size;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.text = text ?? "";
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        return t;
    }

    // =====================================================================
    // UI construction (from prefabs — see Assets/Prefabs/UI)
    // =====================================================================

    private void BuildCanvas()
    {
        if (canvasPrefab == null)
        {
            Debug.LogError("[SessionUI] No canvasPrefab assigned — nothing to show.");
            return;
        }

        Transform head = ResolveHead();
        Camera cam = head != null ? head.GetComponent<Camera>() : null;

        _view = Instantiate(canvasPrefab);
        if (_view.Canvas != null)
        {
            _view.Canvas.renderMode = RenderMode.WorldSpace;
            _view.Canvas.worldCamera = cam;
        }

        // World-anchored: NOT parented to the head (or the XR Origin). Placed in front of
        // the participant by PlaceInFrontOfHead whenever it is shown -- see LateUpdate.
        _view.transform.SetParent(null, false);
        _view.transform.localScale = Vector3.one * canvasScale;
        PlaceInFrontOfHead();
    }

    // Keeps the world-anchored panel usable without head-locking it:
    //  - when it becomes visible, or the phase changes while visible -> place it in front;
    //  - while visible, if the participant has looked away (yaw) by more than
    //    refollowAngle for refollowDelay seconds -> re-centre it in front of them
    //    (e.g. tracking had not started yet when it first appeared, or they finished
    //    Retrace facing the other way).
    private void LateUpdate()
    {
        if (_view == null) return;
        UpdateQuitButton();
        bool visible = _view.gameObject.activeSelf;
        SessionPhase phase = session != null ? session.Phase : SessionPhase.Idle;

        if (visible && (!_wasVisible || phase != _placedPhase))
        {
            PlaceInFrontOfHead();
            _placedPhase = phase;
        }
        else if (visible && refollowAngle > 0f && OffViewAngle() > refollowAngle)
        {
            _offViewTime += Time.deltaTime;
            if (_offViewTime >= refollowDelay) PlaceInFrontOfHead();
        }
        else
        {
            _offViewTime = 0f;
        }

        _wasVisible = visible;
    }

    private void PlaceInFrontOfHead()
    {
        _offViewTime = 0f;
        Transform head = ResolveHead();
        if (_view == null || head == null) return;

        Vector3 fwd = FlatForward(head);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        _view.transform.position = head.position + fwd * localOffset.z
                                   + Vector3.up * localOffset.y + right * localOffset.x;
        _view.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        if (_view.Canvas != null && head.GetComponent<Camera>() is Camera cam)
            _view.Canvas.worldCamera = cam;
    }

    // Horizontal angle between where the participant looks and the panel.
    private float OffViewAngle()
    {
        Transform head = ResolveHead();
        if (head == null) return 0f;
        Vector3 toPanel = _view.transform.position - head.position;
        toPanel.y = 0f;
        if (toPanel.sqrMagnitude < 1e-4f) return 0f;
        return Vector3.Angle(FlatForward(head), toPanel);
    }

    private static Vector3 FlatForward(Transform head)
    {
        Vector3 f = head.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) { f = head.up; f.y = 0f; }
        return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
    }

    // Same head resolution as the rest of the session (PlayerRig.Head, kept live by
    // SessionBootstrap), falling back to a direct search if no bootstrap ran.
    private static Transform ResolveHead()
    {
        if (PlayerRig.Head != null) return PlayerRig.Head;

        var walker = FindAnyObjectByType<DesktopWalker>();
        if (walker != null) return walker.transform;

        if (Camera.main != null) return Camera.main.transform;
        return FindAnyObjectByType<Camera>() is Camera c ? c.transform : null;
    }

    private SessionOptionButton AddOptionButton(string label, UnityEngine.Events.UnityAction onClick)
    {
        if (optionButtonPrefab == null || _view == null || _view.Content == null)
        {
            Debug.LogError("[SessionUI] Missing optionButtonPrefab or canvas Content.");
            return null;
        }

        var item = Instantiate(optionButtonPrefab, _view.Content);
        _items.Add(item.gameObject);

        if (item.Label != null) item.Label.text = label;
        if (item.Button != null) item.Button.onClick.AddListener(onClick);
        return item;
    }

    private void AddAnswerInput(RecallQuestion q)
    {
        if (answerInputPrefab == null || _view == null || _view.Content == null)
        {
            Debug.LogError("[SessionUI] Missing answerInputPrefab or canvas Content.");
            return;
        }

        var item = Instantiate(answerInputPrefab, _view.Content);
        _items.Add(item.gameObject);

        if (item.UnitLabel != null) item.UnitLabel.text = q.unit;
        if (item.Input != null)
        {
            item.Input.text = "";
            item.Input.contentType = TMP_InputField.ContentType.DecimalNumber;
            item.Input.Select();
            item.Input.ActivateInputField();
        }

        string basePrompt = q.prompt;
        bool submitted = false;
        void Submit()
        {
            if (submitted) return;   // guard double-fire (Submit click + Enter key)

            // Recall answers are not skippable: require a real, parseable number
            // before advancing (SessionController rejects it too, as a backstop).
            string text = item.Input != null ? item.Input.text : "";
            if (string.IsNullOrWhiteSpace(text) ||
                !float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                if (_view != null) _view.Body.text = basePrompt + "\n<color=#FF6B6B>Enter a number to continue.</color>";
                if (item.Input != null) item.Input.ActivateInputField();
                return;
            }

            submitted = true;
            session.AnswerCurrentQuestionText(text);
        }

        if (item.Submit != null) item.Submit.onClick.AddListener(Submit);
        if (item.Input != null) item.Input.onSubmit.AddListener(_ => Submit());

        // M58: on-screen number pad -- participants in the headset have no keyboard.
        // (A physical keyboard still works for the researcher on desktop.)
        void Press(string key)
        {
            if (item.Input == null) return;
            string t = item.Input.text ?? "";
            if (key == "Delete") { if (t.Length > 0) t = t.Substring(0, t.Length - 1); }
            else if (key == ".") { if (!t.Contains(".")) t = (t.Length == 0 ? "0" : t) + "."; }
            else if (t.Length < 8) t += key;
            item.Input.text = t;
            if (_view != null) _view.Body.text = basePrompt;   // clear any "enter a number" hint
        }

        foreach (var keys in new[] { new[] { "7", "8", "9" }, new[] { "4", "5", "6" },
                                     new[] { "1", "2", "3" }, new[] { ".", "0", "Delete" } })
        {
            var row = AddRow(68, 14);
            foreach (var k in keys)
            {
                string key = k;
                AddButtonTo(row, key, () => Press(key), false, true);
            }
        }
    }

    private void ClearItems()
    {
        foreach (var b in _items) if (b != null) Destroy(b);
        _items.Clear();
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        // XRUIInputModule drives BOTH the desktop mouse pointer and XR controller rays,
        // so the same UI works in desktop, simulator, and real-headset modes.
        new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
    }
}
