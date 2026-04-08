using UnityEngine;

[System.Serializable]
public class GraphSettings
{
    public float spacing = 0.5f;
    public float heightScale = 0.2f;

    [Header("Animation")]
    public float spawnDuration = 0.3f;
    public float delayBetweenPoints = 0.05f;

    [Header("Dot")]
    public float dotSize = 0.1f;
}