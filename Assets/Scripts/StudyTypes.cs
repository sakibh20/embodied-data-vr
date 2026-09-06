using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Serializable data model for a study session, written to disk as JSON.</summary>

public enum SessionPhase { Idle, Walk, Distractor, Retrace, Recall, Complete }

/// <summary>One planned walk: which condition, which dataset, and its ordering.</summary>
[Serializable]
public class TrialSpec
{
    public ConditionId condition;
    public GraphData dataset;      // not serialized to JSON directly; name is logged
    public int orderIndex;         // 0-based position within the session
    public int trialInCondition;   // 0-based repeat index for this condition
}

/// <summary>
/// A single recall question. Always carries both a multiple-choice representation
/// (options/correctIndex) and the raw numeric answer/unit, so the UI can render
/// either style (RunSettings.recallInputMode) without SessionController needing to
/// know which one is active. Answered via exactly one of answeredIndex (MCQ) or
/// typedAnswer (free-text numeric entry).
/// </summary>
[Serializable]
public class RecallQuestion
{
    public string prompt;
    public string[] options;
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

    // Matches SessionController.Display() so a typed value is judged by the same
    // rounding the multiple-choice options are shown with.
    private static string DisplayValue(float v) => Mathf.Max(0f, v).ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>One sample of the participant's ground position during retrace.</summary>
[Serializable]
public struct RetraceSample
{
    public float t;   // seconds since retrace start
    public float x;   // world X
    public float z;   // world Z
}

/// <summary>Everything recorded for one completed trial.</summary>
[Serializable]
public class TrialResult
{
    public int orderIndex;
    public string condition;
    public string datasetName;
    public int trialInCondition;

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

/// <summary>Top-level log for a participant's whole session.</summary>
[Serializable]
public class SessionLog
{
    public int participantId;
    public string startedUtc;
    public string finishedUtc;
    public int trialsPerCondition;
    public List<TrialResult> trials = new List<TrialResult>();
}
