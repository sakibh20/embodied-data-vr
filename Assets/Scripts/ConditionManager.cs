using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central switch for the study conditions. Holds every registered ConditionBase,
/// keeps exactly one active, and forwards the walk value to it. WalkValueDriver
/// talks only to this manager, so adding/swapping conditions never touches the
/// walk pipeline. Context-menu entries let you switch conditions in the editor
/// without VR (UI-driven switching comes later via these same calls).
/// </summary>
public class ConditionManager : MonoBehaviour
{
    [Tooltip("All condition cue-sets in the scene. Each must have a unique ConditionId.")]
    [SerializeField] private List<ConditionBase> conditions = new List<ConditionBase>();

    [SerializeField] private ConditionId current = ConditionId.Control;

    private ConditionBase _active;

    public ConditionId Current => current;
    public ConditionBase Active => _active;

    private void Awake()
    {
        // Auto-discover conditions when the list wasn't hand-populated (empty) or
        // has gone stale (a null/missing entry, e.g. after a component swap).
        // Includes inactive objects, since deactivated conditions are SetActive(false).
        bool hasNull = false;
        if (conditions != null)
            foreach (var c in conditions)
                if (c == null) { hasNull = true; break; }

        if (conditions == null || conditions.Count == 0 || hasNull)
        {
            conditions = new List<ConditionBase>(
                FindObjectsByType<ConditionBase>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }
    }

    private void Start()
    {
        ApplyCurrent();
    }

    /// <summary>Switch to a condition: deactivates all others, activates this one.</summary>
    public void SetCondition(ConditionId id)
    {
        current = id;
        ApplyCurrent();
    }

    /// <summary>Forward the current normalized walk value to the active condition.</summary>
    public void SetNormalizedValue(float normalized)
    {
        if (_active != null) _active.SetNormalizedValue(normalized);
    }

    /// <summary>Gate the active condition's cues (on only while on the walk).</summary>
    public void SetCuesActive(bool active)
    {
        if (_active != null) _active.SetCuesActive(active);
    }

    private void ApplyCurrent()
    {
        _active = null;
        foreach (var c in conditions)
        {
            if (c == null) continue;

            if (c.Id == current)
            {
                _active = c;
                c.Activate();
            }
            else
            {
                c.Deactivate();
            }
        }

        if (_active == null)
            Debug.LogWarning($"ConditionManager: no condition registered for '{current}'.");
    }

    // ---- Editor navigation (no VR needed) ----
    [ContextMenu("Set Condition / 1 Control")]
    private void _SetControl() => SetCondition(ConditionId.Control);

    [ContextMenu("Set Condition / 2 Abstract")]
    private void _SetAbstract() => SetCondition(ConditionId.Abstract);

    [ContextMenu("Set Condition / 3 Representative")]
    private void _SetRepresentative() => SetCondition(ConditionId.Representative);

    [ContextMenu("Set Condition / 4 Semantic Audio (electric hum + sphere)")]
    private void _SetSemanticAudio() => SetCondition(ConditionId.SemanticAudio);

    [ContextMenu("Set Condition / 5 Semantic Visual (spark + beep)")]
    private void _SetSemanticVisual() => SetCondition(ConditionId.SemanticVisual);
}
