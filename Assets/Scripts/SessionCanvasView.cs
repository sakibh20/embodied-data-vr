using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lives on the root of the SessionCanvas prefab. Exposes the parts SessionUI needs
/// to populate each frame/phase, so SessionUI never has to find children by name or
/// build any of this structure itself -- the prefab's own layout (title/body up top,
/// a scrollable column below for buttons/the answer box) is authored in the Editor
/// and can be freely restyled there without touching code.
/// </summary>
public class SessionCanvasView : MonoBehaviour
{
    [Header("Fixed header (not scrolled)")]
    public TextMeshProUGUI Title;
    public TextMeshProUGUI Body;

    [Header("Scrollable content area")]
    [Tooltip("Parent for instantiated option-button / answer-input items.")]
    public RectTransform Content;
    public ScrollRect Scroll;

    [Header("Wiring")]
    public Canvas Canvas;
}
