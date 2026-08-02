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
}
