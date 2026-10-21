using System.IO;
using UnityEngine;

/// <summary>
/// DEV ONLY (M66 on-device test). If the file "autotest.txt" exists in the app's data
/// folder at launch (on the Quest: /sdcard/Android/data/&lt;app id&gt;/files/autotest.txt,
/// pushed with adb), the app runs one fully simulated session -- consent, background,
/// tutorial, all trials and questionnaires -- with SimParticipant walking the graph, and
/// logs frame-time statistics. The file is deleted first, so it runs once. Never triggers
/// in a normal session. File contents (optional): "pid=990 timescale=2".
/// </summary>
public class DevAutoTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string f = Path.Combine(Application.persistentDataPath, "autotest.txt");
        if (!File.Exists(f)) return;
        string args = File.ReadAllText(f);
        File.Delete(f);
        var go = new GameObject("DevAutoTest");
        DontDestroyOnLoad(go);
        go.AddComponent<DevAutoTest>()._args = args;
    }

    private string _args = "";
    private float _frameSum, _frameMax; private int _frames, _slow;
    private float _nextReport;

    private System.Collections.IEnumerator Start()
    {
        int pid = 990; float ts = 1f;
        foreach (var tok in _args.Split(' ', '\n', '\r'))
        {
            if (tok.StartsWith("pid=")) int.TryParse(tok.Substring(4), out pid);
            if (tok.StartsWith("timescale=")) float.TryParse(tok.Substring(10), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ts);
        }
        yield return new WaitForSeconds(3f);   // let XR, rig and UI settle
        var sc = FindAnyObjectByType<SessionController>();
        var f = typeof(SessionController).GetField("participantId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        f.SetValue(sc, pid);
        Debug.Log($"[AutoTest] start pid={pid} timescale={ts} head={(PlayerRig.Head ? PlayerRig.Head.name : "none")} xr={UnityEngine.XR.XRSettings.isDeviceActive}");
        sc.StartSession();
        sc.ToggleConsentCheck();
        sc.ConsentContinue();
        Transform head = PlayerRig.Head;
        var origin = head != null ? head.root : null;
        var sim = (origin != null ? origin.gameObject : gameObject).AddComponent<SimParticipant>();
        sim.mover = origin;
        Time.timeScale = ts;
        while (sc.Phase != SessionPhase.Complete) yield return null;
        Time.timeScale = 1f;
        Destroy(sim);
        Debug.Log($"[AutoTest] COMPLETE session={sc.SessionId} data={SessionController.ParticipantDir(sc.ParticipantLabel)}");
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _frameSum += dt; _frames++; if (dt > _frameMax) _frameMax = dt; if (dt > 1f / 60f) _slow++;
        if (Time.unscaledTime >= _nextReport)
        {
            var sc = FindAnyObjectByType<SessionController>();
            var cond = FindAnyObjectByType<ConditionManager>();
            if (_frames > 0)
                Debug.Log($"[AutoTest] fps={_frames / _frameSum:0.0} worstMs={_frameMax * 1000f:0.0} framesOver16.7ms={_slow}/{_frames} " +
                          $"phase={(sc ? sc.Phase.ToString() : "-")} trial={(sc ? sc.TrialTag : "-")} cond={(cond ? cond.Current.ToString() : "-")}");
            _frameSum = _frameMax = 0f; _frames = _slow = 0;
            _nextReport = Time.unscaledTime + 5f;
        }
    }
}
