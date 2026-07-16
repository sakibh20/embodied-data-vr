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

    [ContextMenu("Set Condition / 4 Mismatch (static audio)")]
    private void _SetMismatchStatic() => SetCondition(ConditionId.MismatchStaticAudio);

    [ContextMenu("Set Condition / 5 Mismatch (non-rep audio)")]
    private void _SetMismatchNonRep() => SetCondition(ConditionId.MismatchNonRepAudio);
}
