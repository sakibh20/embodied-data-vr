using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Response format of a questionnaire item.</summary>
public enum QuestionnaireScale
{
    /// <summary>5-point agreement: 1 = I do not agree at all ... 5 = I fully agree (SPES format).</summary>
    Agree5,
    /// <summary>NASA (Raw) TLX: 21 segments, 0..100 in steps of 5.</summary>
    Tlx21,
    /// <summary>Pick one of <see cref="QuestionnaireItem.options"/> (M61, background questions).
    /// Logged as the option text.</summary>
    Choice,
}

[Serializable]
public class QuestionnaireItem
{
    public string id;           // e.g. "SPES-SL1"
    public string instrument;   // e.g. "SPES", "Cue check", "NASA Raw TLX"
    public string subscale;     // e.g. "self-location"
    [TextArea(2, 4)] public string text;
    public QuestionnaireScale scale = QuestionnaireScale.Agree5;
    public string lowAnchor;    // label at the low end of the scale
    public string highAnchor;   // label at the high end of the scale
    public List<string> options = new List<string>();   // Choice items only (M61)
}

/// <summary>
/// The post-condition questionnaire (M58): asked ONCE after each condition (after its
/// last trial), never per trial. Items, wording, order, scale type and end labels are
/// all data on this asset (Assets/ScriptableObjects/PostConditionQuestionnaire.asset),
/// so they can be edited without code. Responses go to questionnaires.csv.
/// </summary>
[CreateAssetMenu(fileName = "PostConditionQuestionnaire", menuName = "Embodied Data VR/Post-Condition Questionnaire")]
public class QuestionnaireDefinition : ScriptableObject
{
    public string title = "About this part of the study";
    [Tooltip("Shown on its own screen (with a Start button) before the first item (M59).")]
    [TextArea(2, 4)]
    public string intro = "You have finished this part of the study. Please answer the following " +
                          "questions about the walks you just completed. Point at your answer " +
                          "and press the trigger.";
    public List<QuestionnaireItem> items = new List<QuestionnaireItem>();

    public int Count => items != null ? items.Count : 0;

    /// <summary>Fill with the supervisor's item set (2026-10-18): SPES self-location +
    /// possible actions, two cue-noticing checks, NASA Raw TLX.</summary>
    /// <summary>Fill with the supervisor's background questions (2026-10-20): previous VR
    /// experience and age range, asked once after consent.</summary>
    [ContextMenu("Reset To Background Items (VR experience + age range)")]
    public void ResetToBackgroundItems()
    {
        title = "About you";
        intro = "";
        items = new List<QuestionnaireItem>
        {
            new QuestionnaireItem { id = "VR-EXP", instrument = "Background", subscale = "VR experience",
                text = "How often have you used virtual reality before?", scale = QuestionnaireScale.Choice,
                options = new List<string> { "At least every day", "At least once a week", "At least once a month",
                                             "At least once a year", "Once in the last 5 years", "Never" } },
            new QuestionnaireItem { id = "AGE", instrument = "Background", subscale = "age range",
                text = "What is your age range?", scale = QuestionnaireScale.Choice,
                options = new List<string> { "18-24", "25-29", "30-34", "35-39", "40-44", "45-49", "50-54", "55-60" } },
        };
    }

    [ContextMenu("Reset To Supervisor Items (SPES + Cue checks + Raw TLX)")]
    public void ResetToSupervisorItems()
    {
        const string agreeLo = "I do not agree at all", agreeHi = "I fully agree";
        const string tlxLo = "Very low", tlxHi = "Very high";
        items = new List<QuestionnaireItem>
        {
            A("SPES-SL1", "SPES", "self-location", "I felt like I was actually there in the virtual environment."),
            A("SPES-SL2", "SPES", "self-location", "It seemed as though I actually took part in the action of the virtual environment."),
            A("SPES-SL3", "SPES", "self-location", "It was as though my true location had shifted into the virtual environment."),
            A("SPES-SL4", "SPES", "self-location", "I felt as though I was physically present in the virtual environment."),
            A("SPES-PA1", "SPES", "possible actions", "The dots in the virtual environment gave me the feeling that I could do things with them."),
            A("SPES-PA2", "SPES", "possible actions", "I had the impression that I could be active in the virtual environment."),
            A("SPES-PA5", "SPES", "possible actions", "I felt like I could move around among the dots in the virtual environment."),
            A("SPES-PA8", "SPES", "possible actions", "It seemed to me that I could do whatever I wanted in the virtual environment."),
            A("CUE-V", "Cue check", "visual cue", "I noticed the surroundings changing as I walked."),
            A("CUE-A", "Cue check", "audio cue", "I noticed the sound changing as I walked."),
            T("TLX-1", "Mental Demand", "How mentally demanding was the walk?", tlxLo, tlxHi),
            T("TLX-2", "Physical Demand", "How physically demanding was the walk?", tlxLo, tlxHi),
            T("TLX-3", "Temporal Demand", "How hurried or rushed was the pace of the walk?", tlxLo, tlxHi),
            // Performance is the one TLX item with reversed anchors in the original instrument.
            T("TLX-4", "Performance", "How successful were you in accomplishing what you were asked to do?", "Perfect", "Failure"),
            T("TLX-5", "Effort", "How hard did you have to work to accomplish your level of performance?", tlxLo, tlxHi),
            T("TLX-6", "Frustration", "How insecure, discouraged, irritated, stressed, and annoyed were you during the walk?", tlxLo, tlxHi),
        };

        QuestionnaireItem A(string id, string inst, string sub, string text) => new QuestionnaireItem
            { id = id, instrument = inst, subscale = sub, text = text, scale = QuestionnaireScale.Agree5, lowAnchor = agreeLo, highAnchor = agreeHi };
        QuestionnaireItem T(string id, string sub, string text, string lo, string hi) => new QuestionnaireItem
            { id = id, instrument = "NASA Raw TLX", subscale = sub, text = text, scale = QuestionnaireScale.Tlx21, lowAnchor = lo, highAnchor = hi };
    }
}
