using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "GraphData", menuName = "Thesis/Graph Data")]
public class GraphData : ScriptableObject
{
    public List<float> values;
}