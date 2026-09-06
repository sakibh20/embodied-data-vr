using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lives on the root of the SessionOptionButton prefab (one instance per phase
/// action / recall option). SessionUI instantiates this under
/// SessionCanvasView.Content and sets Label.text + Button.onClick.
/// </summary>
public class SessionOptionButton : MonoBehaviour
{
    public Button Button;
    public TextMeshProUGUI Label;
}
