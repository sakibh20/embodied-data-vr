using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Data model for a study session. Nothing here is JSON-serialized any more --
/// SessionController writes CSV rows directly (see its Write*Csv methods), matching
/// the schema in the sources-folder data notes doc (trials.csv, value_questions.csv,
/// retracing_log.csv). These classes remain as convenient in-memory containers for a
/// trial/question/sample while it's being built up.
/// </summary>

public enum SessionPhase { Idle, Walk, Distractor, Retrace, Recall, Complete }

/// <summary>One planned walk: which condition, which dataset, and its ordering.</summary>
[Serializable]
public class TrialSpec
{
    public ConditionId condition;
    public GraphData dataset;      // not written directly; name is logged
    public int orderIndex;         // 0-based position within the session
    public int trialInCondition;   // 0-based repeat index for this condition
}

/// <summary>
/// A single recall question. Always carries both a multiple-choice representation
/// (options/optionValues/correctIndex) and the raw answerValue/unit, so the UI can
/// render either style (RunSettings.recallInputMode) without SessionController
/// needing to know which one is active. Answered via exactly one of answeredIndex
/// (MCQ) or typedAnswer (free-text numeric entry).
/// </summary>
[Serializable]
public class RecallQuestion
{
    public string questionType;  // e.g. "max_value", "min_value", "end_value", "range_value"
    public string prompt;
    public string[] options;
    public float[] optionValues; // raw numeric value behind each options[] entry, same order
    public int correctIndex;
    public int answeredIndex = -1;

    // Raw ground truth + unit, for text-entry scoring and for labelling the input field.
    public float answerValue;
    public string unit;

    // Set when answered via a typed numeric value instead of picking an option.
    public string typedAnswer;

    public bool Answered => answeredIndex >= 0 || !string.IsNullOrEmpty(typedAnswer);

    public bool IsCorrect
    {
        get
        {
            if (!string.IsNullOrEmpty(typedAnswer))
            {
                if (!float.TryParse(typedAnswer.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return false;
                return DisplayValue(v) == DisplayValue(answerValue);
            }
            return answeredIndex == correctIndex;
        }
    }

    /// <summary>The participant's response as a plain number, however it was given (for CSV export).</summary>
    public float? ResponseValue
    {
        get
        {
            if (!string.IsNullOrEmpty(typedAnswer))
                return float.TryParse(typedAnswer.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : (float?)null;
            if (answeredIndex >= 0 && optionValues != null && answeredIndex < optionValues.Length)
                return optionValues[answeredIndex];
            return null;
        }
    }

    public string AnswerMode => !string.IsNullOrEmpty(typedAnswer) ? "text" : "mcq";

    // Matches SessionController.Display() so a typed value is judged by the same
    // rounding the multiple-choice options are shown with.
    private static string DisplayValue(float v) => Mathf.Max(0f, v).ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>One sample of the participant's ground position during retrace.</summary>
[Serializable]
public struct RetraceSample
{
    public string timestampUtc; // ISO-8601 wall-clock time of this sample
    public float t;             // seconds since retrace start
    public float x;             // world X
    public float y;             // world Y (head height)
    public float z;             // world Z
}

/// <summary>Everything recorded for one completed trial.</summary>
[Serializable]
public class TrialResult
{
    public int orderIndex;
    public string condition;
    public string datasetName;
    public int trialInCondition;

    public string startedUtc;
    public string finishedUtc;

    public float walkSeconds;
    public float distractorSeconds;
    public int distractorStartNumber;
    public float retraceSeconds;

    public List<RetraceSample> retracePath = new List<RetraceSample>();
    public List<RecallQuestion> questions = new List<RecallQuestion>();

    public int RecallScore
    {
        get
        {
            int s = 0;
            foreach (var q in questions) if (q.IsCorrect) s++;
            return s;
        }
    }
}
