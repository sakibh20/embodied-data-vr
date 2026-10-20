using UnityEngine;

/// <summary>
/// Consent screen shown once at the start of each session (M59, wording M61), before
/// anything else. Participants read the information sheet and privacy notice ON PAPER
/// first; this screen only records consent. All wording lives on the asset
/// (Assets/ScriptableObjects/ConsentForm.asset). Continue with the box ticked logs
/// `consentResponse`, unticked logs `noConsentResponse`, to consent.csv with the
/// participant id, `version` and the date/time.
/// Leave SessionController's Consent Form field empty to skip the screen.
/// </summary>
[CreateAssetMenu(fileName = "ConsentForm", menuName = "Embodied Data VR/Consent Form")]
public class ConsentDefinition : ScriptableObject
{
    [Tooltip("Change this whenever the wording changes; it is logged with each answer.")]
    public string version = "2026-10-20";
    public string title = "Consent to take part in the study";
    [Tooltip("Shown under the title (the study name).")]
    public string subtitle = "“Audio and visual cues for remembering data in virtual reality”";
    [TextArea(8, 20)]
    public string body =
        "• I have read the information sheet and the privacy notice, and I have had the chance to ask the researcher questions.\n" +
        "• I am 18 years of age or older.\n" +
        "• I understand that taking part is voluntary and that I can stop at any time without giving a reason.\n" +
        "• I understand that my data are labelled only with a participant code and that, after I leave, my data cannot be removed because they are not linked to my name.\n" +
        "• I agree that my anonymised data can be used in a master’s thesis, in scientific publications and in later research.";
    [Tooltip("The tick box. Continue with it ticked = consent; unticked = no consent.")]
    public string checkboxLabel = "I agree to take part in this study.";
    public string continueLabel = "Continue";
    [Tooltip("Written to consent.csv.")]
    public string consentResponse = "I consent";
    public string noConsentResponse = "I do not consent";
    [TextArea(2, 4)]
    public string declinedMessage = "Thank you. The study will not start. Please tell the researcher.";
}
