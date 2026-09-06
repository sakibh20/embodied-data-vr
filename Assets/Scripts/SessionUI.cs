using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;   // XRUIInputModule, TrackedDeviceGraphicRaycaster

/// <summary>
/// Head-locked in-VR UI for the study. Instantiates a SessionCanvasView prefab (the
/// panel's whole look -- title/body/scrollable content area -- is authored in the
/// Editor, not built in code) and populates it per SessionController phase change:
/// stacks SessionOptionButton items for phase actions / multiple-choice recall
/// options, or a single SessionAnswerInput for free-text recall answers when
/// RunSettings.recallInputMode is TextEntry. Works with a mouse on desktop and with
/// an XR controller ray via TrackedDeviceGraphicRaycaster/XRUIInputModule.
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

    [Header("Placement (head-locked)")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.15f, 2f);
    [SerializeField] private float canvasScale = 0.0025f;

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
        ClearItems();
        if (_view == null) return;

        string trialTag = (phase != SessionPhase.Idle && phase != SessionPhase.Complete)
            ? $"Trial {session.TrialNumber}/{session.TotalTrials}   " : "";

        switch (phase)
        {
            case SessionPhase.Idle:
                _view.Title.text = "Data Physicalisation Study";
                if (audioMode != null)
                {
                    _view.Body.text = $"Audio cue mode: <b>{ModeLabel(audioMode.Mode)}</b>\nChoose a mode, then start.";
                    AddOptionButton(Tick(audioMode.Mode, CueAudioController.AudioMode.PitchOnly) + "Audio: Pitch only",
                              () => { audioMode.SetPitchOnly(); Render(SessionPhase.Idle); });
                    AddOptionButton(Tick(audioMode.Mode, CueAudioController.AudioMode.TempoOnly) + "Audio: Tempo only",
                              () => { audioMode.SetTempoOnly(); Render(SessionPhase.Idle); });
                    AddOptionButton(Tick(audioMode.Mode, CueAudioController.AudioMode.Both) + "Audio: Both",
                              () => { audioMode.SetBoth(); Render(SessionPhase.Idle); });
                }
                else
                {
                    _view.Body.text = "Ready to begin.";
                }
                AddOptionButton("Start Session", () => session.StartSession());
                break;

            case SessionPhase.Walk:
                _view.Title.text = trialTag + "Walk";
                _view.Body.text = "Walk to the end of the graph, taking in the data as you go.";
                AddOptionButton("Done walking", () => session.EndWalk());
                break;

            case SessionPhase.Distractor:
                _view.Title.text = trialTag + "Counting task";
                _view.Body.text = $"Count out loud backwards from <b>{session.DistractorStartNumber}</b> " +
                             "in steps of 3, until told to stop.";
                AddOptionButton("Done", () => session.EndDistractor());
                break;

            case SessionPhase.Retrace:
                _view.Title.text = trialTag + "Retrace";
                _view.Body.text = "Now walk the same path again from memory. The line is hidden.";
                AddOptionButton("Done retracing", () => session.EndRetrace());
                break;

            case SessionPhase.Recall:
                RenderRecall(trialTag);
                break;

            case SessionPhase.Complete:
                _view.Title.text = "Session complete";
                _view.Body.text = "Thank you! You can remove the headset.";
                break;
        }
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
        => current == option ? "✓ " : "";

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
    // UI construction (from prefabs -- see Assets/Prefabs/UI)
    // =====================================================================

    private void BuildCanvas()
    {
        if (canvasPrefab == null)
        {
            Debug.LogError("[SessionUI] No canvasPrefab assigned -- nothing to show.");
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

        if (head != null)
        {
            _view.transform.SetParent(head, false);
            _view.transform.localPosition = localOffset;
            _view.transform.localRotation = Quaternion.identity;
            _view.transform.localScale = Vector3.one * canvasScale;
        }
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

    private void AddOptionButton(string label, UnityEngine.Events.UnityAction onClick)
    {
        if (optionButtonPrefab == null || _view == null || _view.Content == null)
        {
            Debug.LogError("[SessionUI] Missing optionButtonPrefab or canvas Content.");
            return;
        }

        var item = Instantiate(optionButtonPrefab, _view.Content);
        _items.Add(item.gameObject);

        if (item.Label != null) item.Label.text = label;
        if (item.Button != null) item.Button.onClick.AddListener(onClick);
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

        bool submitted = false;
        void Submit()
        {
            if (submitted) return;   // guard double-fire (Submit click + Enter key)
            submitted = true;
            session.AnswerCurrentQuestionText(item.Input != null ? item.Input.text : "");
        }

        if (item.Submit != null) item.Submit.onClick.AddListener(Submit);
        if (item.Input != null) item.Input.onSubmit.AddListener(_ => Submit());
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
