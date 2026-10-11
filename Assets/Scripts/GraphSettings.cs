using UnityEngine;

// Single shared source of truth for graph + grid layout, and (M48) for how the
// density cue field fills the room. Referenced as an asset by GraphManager,
// GridGenerator and every DensityController so values never diverge between them.
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

    // ---- Cue field (M48): where the abstract spheres / semantic orbs spawn ----
    // The field is a box centred on ExperimentArea, so it follows the area's
    // position, rotation and size automatically.

    [Header("Cue Field (3D spread)")]
    [Tooltip("On: cue objects fill a 3D room-like volume (footprint + height range " +
             "below). Off: legacy behaviour, objects lie on the ExperimentArea floor plane.")]
    public bool cueField3D = true;

    [Tooltip("Metres added on EACH side of the ExperimentArea footprint: X = width " +
             "(lateral), Y = depth (walk axis). (0,0) = exactly the area footprint.")]
    public Vector2 cueFieldPadding = new Vector2(1.5f, 1.5f);

    [Tooltip("Height band (metres above the floor) that cue objects are spread through.")]
    public Vector2 cueFieldHeight = new Vector2(0.3f, 2.8f);

    [Tooltip("Keep the walk zone (the ExperimentArea footprint) free of cue objects " +
             "below 'Walk Zone Clear Height', so nothing floats at body/face height " +
             "where the participant walks -- avoids heads passing through orbs in VR. " +
             "Objects still fill the space around and above the zone.")]
    public bool keepWalkZoneClear = true;

    [Tooltip("Inside the walk zone, cue objects only spawn above this height (metres).")]
    public float walkZoneClearHeight = 2.1f;
}