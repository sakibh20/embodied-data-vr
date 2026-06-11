using UnityEngine;

// Single shared source of truth for graph + grid layout. Referenced as an asset
// by both GraphManager and GridGenerator so values never diverge between the two.
[CreateAssetMenu(fileName = "GraphSettings", menuName = "Thesis/Graph Settings")]
public class GraphSettings : ScriptableObject
{
    public float spacing = 0.5f;
    public float heightScale = 0.2f;

    [Header("Animation")]
    public float spawnDuration = 0.3f;
    public float delayBetweenPoints = 0.05f;

    [Header("Dot")]
    public float dotSize = 0.1f;
}