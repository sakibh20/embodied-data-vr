using System;
using System.Collections.Generic;
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

/// <summary>A single recall question with multiple-choice options and the given answer.</summary>
[Serializable]
public class RecallQuestion
{
    public string prompt;
    public string[] options;
    public int correctIndex;
    public int answeredIndex = -1;

    public bool Answered => answeredIndex >= 0;
    public bool IsCorrect => answeredIndex == correctIndex;
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
