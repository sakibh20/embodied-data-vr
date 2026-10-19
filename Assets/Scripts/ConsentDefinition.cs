using UnityEngine;

/// <summary>
/// Privacy / informed-consent screen shown once at the start of each session (M59),
/// before the first trial. All wording lives on the asset
/// (Assets/ScriptableObjects/ConsentForm.asset) -- replace the placeholder text with the
/// wording approved for the study. The answer is written to consent.csv together with
/// `version`, so you can tell which wording a participant agreed to.
/// Leave SessionController's Consent Form field empty to skip the screen.
/// </summary>
[CreateAssetMenu(fileName = "ConsentForm", menuName = "Embodied Data VR/Consent Form")]
public class ConsentDefinition : ScriptableObject
{
    [Tooltip("Change this whenever the wording changes; it is logged with each answer.")]
    public string version = "draft-1";
    public string title = "Before we start";
    [TextArea(8, 20)]
    public string body =
        "Please read the points below. Ask the researcher if anything is unclear.\n\n" +
        "• I have read the study information sheet and had the chance to ask questions.\n" +
        "• Taking part is voluntary. I can stop at any time without giving a reason.\n" +
        "• My movements in the virtual environment and my answers are recorded under a " +
        "participant code, not my name.\n" +
        "• The anonymised data may be used for research and publications.\n" +
        "• I can take off the headset at any time if I feel unwell.";
    public string question = "Do you agree to take part?";
    public string agreeLabel = "I agree";
    public string declineLabel = "I do not agree";
    [TextArea(2, 4)]
    public string declinedMessage = "Thank you. The study will not start. Please tell the researcher.";
}
