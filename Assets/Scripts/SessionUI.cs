using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Self-building in-VR UI for the study. Constructs a world-space canvas (a single
/// reconfigurable panel: title, body, stacked buttons) in code and drives it from
/// the SessionController: it shows the right prompt/buttons for each phase and calls
/// the controller's API on click. Works with a mouse on desktop; for on-device VR
/// swap the EventSystem's input module for XRI's XRUIInputModule and (optionally)
/// detach the canvas from the head to a comfortable fixed/controller-anchored spot.
/// </summary>
public class SessionUI : MonoBehaviour
{
    [SerializeField] private SessionController session;
    [SerializeField] private Camera targetCamera;
    [Tooltip("Session-wide audio cue mode selector, shown on the Idle screen. Auto-found if empty.")]
    [SerializeField] private AudioModeSelector audioMode;

    [Header("Placement (head-locked)")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.15f, 2f);
    [SerializeField] private float canvasScale = 0.0025f;

    private TextMeshProUGUI _title;
    private TextMeshProUGUI _body;
    private RectTransform _buttonColumn;
    private readonly List<GameObject> _buttons = new List<GameObject>();

    private void Awake()
    {
        if (session == null) session = FindAnyObjectByType<SessionController>();
        if (audioMode == null) audioMode = FindAnyObjectByType<AudioModeSelector>();
        if (targetCamera == null)
        {
            var walker = FindAnyObjectByType<DesktopWalker>();
            targetCamera = walker != null ? walker.GetComponent<Camera>() : Camera.main;
            if (targetCamera == null) targetCamera = FindAnyObjectByType<Camera>();
        }

        EnsureEventSystem();
        BuildUI();
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
        ClearButtons();
        string trialTag = (phase != SessionPhase.Idle && phase != SessionPhase.Complete)
            ? $"Trial {session.TrialNumber}/{session.TotalTrials}   " : "";

        switch (phase)
        {
            case SessionPhase.Idle:
                _title.text = "Data Physicalisation Study";
                if (audioMode != null)
                {
                    _body.text = $"Audio cue mode: <b>{ModeLabel(audioMode.Mode)}</b>\nChoose a mode, then start.";
                    AddButton(Tick(audioMode.Mode, CueAudioController.AudioMode.PitchOnly) + "Audio: Pitch only",
                              () => { audioMode.SetPitchOnly(); Render(SessionPhase.Idle); });
                    AddButton(Tick(audioMode.Mode, CueAudioController.AudioMode.TempoOnly) + "Audio: Tempo only",
                              () => { audioMode.SetTempoOnly(); Render(SessionPhase.Idle); });
                    AddButton(Tick(audioMode.Mode, CueAudioController.AudioMode.Both) + "Audio: Both",
                              () => { audioMode.SetBoth(); Render(SessionPhase.Idle); });
                }
                else
                {
                    _body.text = "Ready to begin.";
                }
                AddButton("Start Session", () => session.StartSession());
                break;

            case SessionPhase.Walk:
                _title.text = trialTag + "Walk";
                _body.text = "Walk to the end of the graph, taking in the data as you go.";
                AddButton("Done walking", () => session.EndWalk());
                break;

            case SessionPhase.Distractor:
                _title.text = trialTag + "Counting task";
                _body.text = $"Count out loud backwards from <b>{session.DistractorStartNumber}</b> " +
                             "in steps of 3, until told to stop.";
                AddButton("Done", () => session.EndDistractor());
                break;

            case SessionPhase.Retrace:
                _title.text = trialTag + "Retrace";
                _body.text = "Now walk the same path again from memory. The line is hidden.";
                AddButton("Done retracing", () => session.EndRetrace());
                break;

            case SessionPhase.Recall:
                RenderRecall(trialTag);
                break;

            case SessionPhase.Complete:
                _title.text = "Session complete";
                _body.text = "Thank you! You can remove the headset.";
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
        if (q == null) { _body.text = ""; return; }

        _title.text = $"{trialTag}Question {session.QuestionNumber}/{session.QuestionCount}";
        _body.text = q.prompt;

        for (int i = 0; i < q.options.Length; i++)
        {
            int choice = i;   // capture
            AddButton(q.options[i], () => session.AnswerCurrentQuestion(choice));
        }
    }

    // =====================================================================
    // UI construction
    // =====================================================================

    private void BuildUI()
    {
        var canvasGo = new GameObject("SessionCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var crt = (RectTransform)canvasGo.transform;
        crt.sizeDelta = new Vector2(900f, 650f);

        if (targetCamera != null)
        {
            canvas.worldCamera = targetCamera;
            canvasGo.transform.SetParent(targetCamera.transform, false);
            canvasGo.transform.localPosition = localOffset;
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * canvasScale;
        }

        // Background panel with a vertical layout of title / body / buttons.
        var panel = NewChild("Panel", canvasGo.transform);
        var panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.82f);
        Stretch(panel);

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(50, 50, 50, 50);
        vlg.spacing = 28;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
        vlg.childControlHeight = false; vlg.childForceExpandHeight = false;

        _title = NewText(panel, 44, FontStyles.Bold);
        _body = NewText(panel, 30, FontStyles.Normal);
        _body.enableWordWrapping = true;

        _buttonColumn = NewChild("Buttons", panel);
        var col = _buttonColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        col.spacing = 16;
        col.childAlignment = TextAnchor.UpperCenter;
        col.childControlWidth = true; col.childForceExpandWidth = true;
        col.childControlHeight = false; col.childForceExpandHeight = false;
        col.padding = new RectOffset(0, 0, 20, 0);
    }

    private void AddButton(string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_buttonColumn, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.20f, 0.40f, 0.70f, 1f);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 78f; le.preferredHeight = 78f;

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var labelRt = NewChild("Label", go.transform);
        Stretch(labelRt);
        var txt = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 30;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;

        _buttons.Add(go);
    }

    private void ClearButtons()
    {
        foreach (var b in _buttons) if (b != null) Destroy(b);
        _buttons.Clear();
    }

    private TextMeshProUGUI NewText(Transform parent, float size, FontStyles style)
    {
        var rt = NewChild("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = size + 10f;
        return t;
    }

    private static RectTransform NewChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
