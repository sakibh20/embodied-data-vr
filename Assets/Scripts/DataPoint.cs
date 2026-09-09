using TMPro;
using UnityEngine;

public class DataPoint : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    private int _index;
    private GraphData _graphData;
    
    public void Init(int index, GraphData graphData)
    {
        _index = index;
        _graphData = graphData;
    }

    public void Show()
    {
        label.SetText($"{_graphData.prefix} {(_index+1)*_graphData.dataInterval} : {_graphData.values[_index]} {_graphData.unit}");
    }

    /// <summary>
    /// Blanks the value label instead of showing it. Used for dots that have no
    /// matching GraphData value to display -- e.g. the retrace-review graph, whose
    /// points are resampled from the participant's own walked path rather than real
    /// dataset entries. See ROADMAP.md M44.
    /// </summary>
    public void HideLabel()
    {
        if (label != null) label.SetText(string.Empty);
    }
}
