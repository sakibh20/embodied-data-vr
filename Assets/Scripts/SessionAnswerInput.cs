using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lives on the root of the SessionAnswerInput prefab, used for the recall phase's
/// free-text numeric entry (RunSettings.recallInputMode = TextEntry). SessionUI
/// instantiates this once per question under SessionCanvasView.Content, sets
/// UnitLabel.text to the question's unit, and listens for Submit's click.
/// </summary>
public class SessionAnswerInput : MonoBehaviour
{
    public TMP_InputField Input;
    public TextMeshProUGUI UnitLabel;
    public Button Submit;
}
