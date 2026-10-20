using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DEV ONLY (M62 data audit). Added at runtime to the DesktopWalker to walk a whole
/// session without a person: in Walk it goes to the Start marker, follows the graph dots
/// and steps onto the End marker; in Retrace the same with lateral noise (an imperfect
/// memory); between trials it returns to the Start marker. Never in the scene by default.
/// </summary>
public class SimParticipant : MonoBehaviour
{
    public float speed = 1.2f;          // m/s
    public float retraceNoise = 0.25f;  // m of lateral error in the retrace

    private SessionController _session;
    private ExperimentArea _area;
    private GraphManager _graph;
    private readonly List<Vector3> _route = new List<Vector3>();
    private int _leg;
    private SessionPhase _routeFor = SessionPhase.Idle;
    private int _routeTrial = -1;

    void Awake()
    {
        _session = FindAnyObjectByType<SessionController>();
        _area = FindAnyObjectByType<ExperimentArea>();
        _graph = FindAnyObjectByType<GraphManager>();
        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != this && mb.GetType().Name == "DesktopWalker") mb.enabled = false;
    }

    public bool autoAnswer = true;      // also clicks through distractor, recall, questionnaires
    private float _wait;
    private readonly System.Random _ans = new System.Random(7);

    void Update()
    {
        var phase = _session.Phase;
        if (autoAnswer)
        {
            _wait += Time.unscaledDeltaTime;
            if (_wait > 0.15f)
            {
                _wait = 0f;
                switch (phase)
                {
                    case SessionPhase.Distractor: _session.EndDistractor(); break;
                    case SessionPhase.RetraceReview: _session.EndRetraceReview(); break;
                    case SessionPhase.Recall:
                        var q = _session.CurrentQuestion;
                        if (q != null) _session.AnswerCurrentQuestionText(
                            (q.answerValue * (0.7f + 0.6f * (float)_ans.NextDouble())).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case SessionPhase.Questionnaire:
                        if (_session.QnShowingIntro) { _session.QnBeginItems(); break; }
                        var it = _session.CurrentQnItem;
                        if (it == null) break;
                        int v = it.scale == QuestionnaireScale.Tlx21 ? 5 * _ans.Next(0, 21)
                              : it.scale == QuestionnaireScale.Choice ? _ans.Next(0, it.options.Count) : _ans.Next(1, 6);
                        _session.SetQnResponse(v); _session.QnNext(); break;
                    case SessionPhase.TrialComplete:
                        if (_session.ReadyToStartNext) _session.StartNextTrial(); break;
                }
                phase = _session.Phase;
            }
        }
        if (phase != _routeFor || _session.TrialNumber != _routeTrial) BuildRoute(phase);
        if (_leg >= _route.Count) return;
        Vector3 pos = transform.position;
        Vector3 target = new Vector3(_route[_leg].x, pos.y, _route[_leg].z);
        Vector3 next = Vector3.MoveTowards(pos, target, speed * Time.deltaTime);
        Vector3 dir = target - pos; dir.y = 0;
        if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
        transform.position = next;
        if ((next - target).sqrMagnitude < 1e-4f) _leg++;
    }

    void BuildRoute(SessionPhase phase)
    {
        _routeFor = phase; _routeTrial = _session.TrialNumber; _route.Clear(); _leg = 0;
        Vector3 start = _area.ParticipantSpot;
        Vector3 end = _area.Center + _area.Forward * (_area.Depth * 0.5f + _area.MarkerGap + _area.MarkerDepth * 0.5f);
        if (phase == SessionPhase.Walk || phase == SessionPhase.Retrace)
        {
            _route.Add(start);
            var dots = _graph.DotWorldPositions();
            var rng = new System.Random(_session.TrialNumber * 31 + (int)phase);
            foreach (var d in dots)
            {
                Vector3 p = d;
                if (phase == SessionPhase.Retrace)
                    p += _area.transform.right * (float)((rng.NextDouble() * 2 - 1) * retraceNoise);
                _route.Add(p);
            }
            _route.Add(end);
        }
        else if (phase == SessionPhase.TrialComplete)
        {
            _route.Add(start);
        }
    }
}
