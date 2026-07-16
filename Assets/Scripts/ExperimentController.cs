using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime control surface for the study. Centralises the three things an operator
/// needs to drive during a session — pick the condition (1–5), pick the audio mode
/// (tempo / pitch / both), and (re)build the graph — behind both keyboard shortcuts
/// (desktop testing) and button-ready public methods (for an on-screen UI later).
///
/// Keyboard (desktop):
///   1..5 → Control / Abstract / Representative / MismatchStatic / MismatchNonRep
///   T / P / B → tempo-only / pitch-only / both audio
///   G → (re)generate the graph
///
/// This is the seam the Phase 4 session controller will drive instead of a human.
/// </summary>
public class ExperimentController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ConditionManager conditionManager;
    [SerializeField] private AudioModeSelector audioModeSelector;
    [SerializeField] private GraphManager graphManager;

    [Header("Options")]
    [Tooltip("Enable the desktop keyboard shortcuts. Turn off for VR/participant runs.")]
    [SerializeField] private bool keyboardShortcuts = true;

    private void Awake()
    {
        if (conditionManager == null) conditionManager = FindAnyObjectByType<ConditionManager>();
        if (audioModeSelector == null) audioModeSelector = FindAnyObjectByType<AudioModeSelector>();
        if (graphManager == null) graphManager = FindAnyObjectByType<GraphManager>();
    }

    private void Update()
    {
        if (!keyboardShortcuts) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) SelectCondition(ConditionId.Control);
        if (kb.digit2Key.wasPressedThisFrame) SelectCondition(ConditionId.Abstract);
        if (kb.digit3Key.wasPressedThisFrame) SelectCondition(ConditionId.Representative);
        if (kb.digit4Key.wasPressedThisFrame) SelectCondition(ConditionId.SemanticAudio);
        if (kb.digit5Key.wasPressedThisFrame) SelectCondition(ConditionId.SemanticVisual);

        if (kb.tKey.wasPressedThisFrame) SetAudioTempoOnly();
        if (kb.pKey.wasPressedThisFrame) SetAudioPitchOnly();
        if (kb.bKey.wasPressedThisFrame) SetAudioBoth();

        if (kb.gKey.wasPressedThisFrame) GenerateGraph();
    }

    // ---- Button-ready API (bind UI Buttons / UnityEvents to these) ----

    public void SelectCondition(ConditionId id)
    {
        if (conditionManager == null) return;
        conditionManager.SetCondition(id);
        Debug.Log($"[Experiment] Condition → {id}");
    }

    // Enum-free wrappers so inspector UnityEvents can bind them directly.
    public void SelectControl() => SelectCondition(ConditionId.Control);
    public void SelectAbstract() => SelectCondition(ConditionId.Abstract);
    public void SelectRepresentative() => SelectCondition(ConditionId.Representative);
    public void SelectSemanticAudio() => SelectCondition(ConditionId.SemanticAudio);
    public void SelectSemanticVisual() => SelectCondition(ConditionId.SemanticVisual);

    public void SetAudioTempoOnly() => audioModeSelector?.SetTempoOnly();
    public void SetAudioPitchOnly() => audioModeSelector?.SetPitchOnly();
    public void SetAudioBoth() => audioModeSelector?.SetBoth();

    public void GenerateGraph()
    {
        if (graphManager == null) return;
        graphManager.GenerateGraph();
        Debug.Log("[Experiment] Graph (re)generated");
    }
}
