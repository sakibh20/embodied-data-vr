using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Docs only: in Play mode, walks the session UI through every screen and saves each
/// with UiScreenshot (one frame after each change so replaced items are gone).
/// Uses participant 996 and 1 trial per condition (not saved to the device settings).
/// </summary>
public class UiScreenshotTour : MonoBehaviour
{
    public static string Status = "";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    SessionController sc; SessionUI ui;
    readonly List<string> done = new List<string>();

    static bool _running;
    // Guard: a repeated call (e.g. a retried remote command) must never start a second tour.
    public static void Begin() { if (_running) return; _running = true; new GameObject("UiScreenshotTour").AddComponent<UiScreenshotTour>(); }
    void OnDestroy() { _running = false; }

    void Start() { StartCoroutine(Run()); }

    IEnumerator Cap(string name)
    {
        yield return null; yield return null;
        typeof(SessionUI).GetMethod("UpdateQuitButton", F).Invoke(ui, null);
        yield return null;
        done.Add(UiScreenshot.Capture(name));
        Status = string.Join(", ", done);
    }
    // The "ready" variant of the between-trial screens appears once the participant stands on the Start marker.
    void StandOnEnd()
    {
        var area = FindAnyObjectByType<ExperimentArea>(); var head = PlayerRig.Head;
        if (area == null || head == null) return;
        var cc = head.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        Vector3 end = area.Center + area.Forward * (area.Depth * 0.5f + area.MarkerGap + area.MarkerDepth * 0.5f);
        head.position = new Vector3(end.x, head.position.y, end.z);
    }
    void StandOnStart()
    {
        var area = FindAnyObjectByType<ExperimentArea>();
        var head = PlayerRig.Head;
        if (area == null || head == null) return;
        var cc = head.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        Vector3 spot = area.ParticipantSpot;
        head.position = new Vector3(spot.x, head.position.y, spot.z);
    }
    void Render() => typeof(SessionUI).GetMethod("Render", F).Invoke(ui, new object[] { sc.Phase });
    void SetUi(string field, object v) => typeof(SessionUI).GetField(field, F).SetValue(ui, v);
    IEnumerator Recall(string prefix)
    {
        int n = 1;
        while (sc.Phase == SessionPhase.Recall)
        {
            yield return Cap($"{prefix}_q{n}");
            sc.AnswerCurrentQuestionText("12.5");
            n++;
        }
    }

    IEnumerator Run()
    {
        sc = FindAnyObjectByType<SessionController>(); ui = FindAnyObjectByType<SessionUI>();
        typeof(SessionController).GetField("participantId", F).SetValue(sc, 996);
        RuntimeSettings.Current.trialsPerCondition = 1; RuntimeSettings.Apply();   // not saved

        Render(); yield return Cap("01_start_screen");
        SetUi("_settingsOpen", true); Render(); yield return Cap("40_settings");
        SetUi("_confirmClear", true); Render(); yield return Cap("41_settings_delete_confirm");
        SetUi("_confirmClear", false); SetUi("_settingsOpen", false); Render();

        sc.StartSession(); yield return Cap("02_consent");
        sc.ToggleConsentCheck(); yield return Cap("02b_consent_ticked");
        sc.ToggleConsentCheck(); sc.ConsentContinue(); yield return Cap("03_consent_not_given");
        sc.ConsentBackToIdle(); yield return null;
        sc.StartSession(); sc.ToggleConsentCheck(); sc.ConsentContinue();
        yield return Cap("04_background_vr_experience");
        sc.SetQnResponse(2); yield return Cap("04b_background_vr_experience_answered");
        sc.QnNext(); yield return Cap("05_background_age");
        sc.SetQnResponse(1); sc.QnNext(); yield return Cap("06_welcome_instructions");

        // practice 1
        sc.BeginTutorial(); yield return null;
        sc.EndWalk(); yield return Cap("08_counting_task");
        sc.EndDistractor(); yield return Cap("09_retrace_instruction");
        sc.EndRetrace(); yield return null; sc.EndRetraceReview();
        yield return Recall("10_recall");
        StandOnEnd(); yield return null; yield return null; typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, false); Render(); yield return Cap("11_practice_complete_walk_back");
        StandOnStart(); yield return null; yield return null;
        typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, true); Render();
        yield return Cap("11b_practice_complete_ready");

        // practice 2 -> tutorial complete
        sc.StartNextTrial(); yield return null;
        sc.EndWalk(); sc.EndDistractor(); sc.EndRetrace(); yield return null; sc.EndRetraceReview();
        while (sc.Phase == SessionPhase.Recall) { sc.AnswerCurrentQuestionText("10"); yield return null; }
        StandOnEnd(); yield return null; yield return null; typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, false); Render(); yield return Cap("12_tutorial_complete_walk_back");
        StandOnStart(); yield return null; yield return null;
        typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, true); Render();
        yield return Cap("12b_tutorial_complete_ready");

        // experiment trial 1 -> questionnaire (1 trial per condition here)
        sc.StartNextTrial(); yield return null;
        sc.EndWalk(); yield return Cap("13_counting_task_experiment");
        sc.EndDistractor(); sc.EndRetrace(); yield return null; sc.EndRetraceReview();
        while (sc.Phase == SessionPhase.Recall) { sc.AnswerCurrentQuestionText("10"); yield return null; }
        yield return Cap("20_questionnaire_intro");
        sc.QnBeginItems();
        int i = 1;
        while (sc.Phase == SessionPhase.Questionnaire)
        {
            var item = sc.CurrentQnItem;
            yield return Cap($"21_questionnaire_{i:00}_{item.id}");
            if (i == 1 || i == 11)
            {
                sc.SetQnResponse(item.scale == QuestionnaireScale.Tlx21 ? 60 : 4);
                yield return Cap($"21_questionnaire_{i:00}_{item.id}_answered");
            }
            sc.SetQnResponse(item.scale == QuestionnaireScale.Tlx21 ? 50 : 3);
            sc.QnNext(); i++;
            yield return null;
        }
        StandOnEnd(); yield return null; yield return null; typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, false); Render(); yield return Cap("22_trial_complete_walk_back");
        StandOnStart(); yield return null; yield return null;
        typeof(SessionController).GetProperty("ReadyToStartNext").SetValue(sc, true); Render();
        yield return Cap("22b_trial_complete_ready");

        // researcher menu + confirms
        typeof(SessionUI).GetMethod("SetMenuOpen", F).Invoke(ui, new object[] { true });
        yield return Cap("42_researcher_menu");
        SetUi("_menuConfirm", "end"); Render(); yield return Cap("43_researcher_end_session_confirm");
        SetUi("_menuConfirm", "exit"); Render(); yield return Cap("44_researcher_close_app_confirm");
        typeof(SessionUI).GetMethod("SetMenuOpen", F).Invoke(ui, new object[] { false });

        // session complete
        typeof(SessionController).GetMethod("FinishSession", F).Invoke(sc, null);
        yield return Cap("30_session_complete");

        RuntimeSettings.Current.trialsPerCondition = RuntimeSettings.Defaults.trialsPerCondition; RuntimeSettings.Apply();
        Status += " | DONE";
        Destroy(gameObject);
    }
}
