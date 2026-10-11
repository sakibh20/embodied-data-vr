using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// The visual density cue: keeps between minCount and maxCount objects (sphere or
/// electric orb prefab, per condition) alive, scaled by the normalized value.
///
/// M48: objects are spread through a 3D room-like volume instead of lying on the
/// floor plane. The volume is configured on the shared GraphSettings asset (same
/// SO as the graph/grid layout): an ExperimentArea-centred footprint (+ padding),
/// a height band, and an optional clear walk zone so nothing floats at body/face
/// height where the participant walks. Objects scale in/out instead of popping.
/// </summary>
public class DensityController : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("Area")]
    [Tooltip("Shared room area the cue field is centred on. Auto-found if left empty. " +
             "When set, it overrides the legacy areaSize/centerPoint below.")]
    [SerializeField] private ExperimentArea area;

    [Tooltip("Shared layout settings (Assets/Scripts/GraphSettings.asset) -- holds the " +
             "3D cue-field configuration. If empty, objects fall back to the area's floor plane.")]
    [SerializeField] private GraphSettings settings;

    [Header("Area Settings (legacy fallback when no ExperimentArea)")]
    [SerializeField] private Vector2 areaSize = new Vector2(5f, 5f);
    [SerializeField] private Transform centerPoint;

    [Header("Density Settings")]
    [SerializeField] private int minCount = 5;
    [SerializeField] private int maxCount = 100;

    [Header("Size Settings")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.1f, 0.3f);

    [Header("Appear / Disappear")]
    [Tooltip("Seconds an object takes to grow in / shrink out. 0 = instant (old behaviour).")]
    [SerializeField] private float scaleTweenSeconds = 0.3f;

    private readonly List<GameObject> _pool = new List<GameObject>();

    private void Awake()
    {
        if (area == null) area = FindAnyObjectByType<ExperimentArea>();
    }

    public void UpdateDensity(float normalizedValue)
    {
        int targetCount = Mathf.RoundToInt(Mathf.Lerp(minCount, maxCount, normalizedValue));
        AdjustPool(targetCount);
    }

    /// <summary>Remove all spawned objects (used when cues are gated off).</summary>
    public void Clear() => AdjustPool(0);

    private void AdjustPool(int targetCount)
    {
        while (_pool.Count < targetCount)
            _pool.Add(CreateObject());

        while (_pool.Count > targetCount)
        {
            GameObject obj = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            Despawn(obj);
        }
    }

    private GameObject CreateObject()
    {
        Vector3 pos = GetRandomPosition();
        GameObject obj = Instantiate(spawnPrefab, pos, Quaternion.identity, transform);

        float scale = Random.Range(sizeRange.x, sizeRange.y);
        if (scaleTweenSeconds > 0f && isActiveAndEnabled)
        {
            obj.transform.localScale = Vector3.zero;
            obj.transform.DOScale(scale, scaleTweenSeconds).SetEase(Ease.OutBack).SetLink(obj);
        }
        else
        {
            obj.transform.localScale = Vector3.one * scale;
        }
        return obj;
    }

    private void Despawn(GameObject obj)
    {
        if (obj == null) return;
        // Shrink-out only while this condition is live; when it is being switched off
        // (or the scene is unloading) remove immediately so nothing lingers.
        if (scaleTweenSeconds > 0f && isActiveAndEnabled && obj.activeInHierarchy)
        {
            obj.transform.DOKill();
            obj.transform.DOScale(0f, scaleTweenSeconds * 0.8f).SetEase(Ease.InBack)
               .SetLink(obj).OnComplete(() => { if (obj != null) Destroy(obj); });
        }
        else
        {
            Destroy(obj);
        }
    }

    private Vector3 GetRandomPosition()
    {
        if (area != null)
        {
            if (settings != null && settings.cueField3D) return RandomPointInField();
            return area.RandomPoint();   // legacy: on the area's floor plane
        }

        // Legacy fallback (no ExperimentArea in the scene).
        Vector3 center = centerPoint != null ? centerPoint.position : transform.position;
        float x = Random.Range(-areaSize.x / 2f, areaSize.x / 2f);
        float z = Random.Range(-areaSize.y / 2f, areaSize.y / 2f);
        return new Vector3(center.x + x, center.y, center.z + z);
    }

    // Uniform point in the 3D field box (area footprint + padding, height band), with
    // the walk zone optionally kept clear below walkZoneClearHeight.
    private Vector3 RandomPointInField()
    {
        float w = area.Width + 2f * Mathf.Max(0f, settings.cueFieldPadding.x);
        float d = area.Depth + 2f * Mathf.Max(0f, settings.cueFieldPadding.y);
        float hMin = Mathf.Min(settings.cueFieldHeight.x, settings.cueFieldHeight.y);
        float hMax = Mathf.Max(settings.cueFieldHeight.x, settings.cueFieldHeight.y);
        float halfAreaW = area.Width * 0.5f, halfAreaD = area.Depth * 0.5f;

        float x = 0f, z = 0f, h = hMin;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            x = Random.Range(-0.5f, 0.5f) * w;
            z = Random.Range(-0.5f, 0.5f) * d;
            h = Random.Range(hMin, hMax);

            bool inWalkZone = Mathf.Abs(x) < halfAreaW && Mathf.Abs(z) < halfAreaD;
            if (!settings.keepWalkZoneClear || !inWalkZone || h >= settings.walkZoneClearHeight)
                break;

            // Last resort (e.g. padding 0): lift it above the clear height instead.
            if (attempt == 23) h = Mathf.Max(settings.walkZoneClearHeight, hMax);
        }

        return area.Center + area.Right * x + area.Forward * z + Vector3.up * h;
    }

    // Scene-view aid: the cue-field box (cyan) and the kept-clear walk zone (orange).
    private void OnDrawGizmosSelected()
    {
        var a = area != null ? area : FindAnyObjectByType<ExperimentArea>();
        if (a == null || settings == null || !settings.cueField3D) return;

        float w = a.Width + 2f * Mathf.Max(0f, settings.cueFieldPadding.x);
        float d = a.Depth + 2f * Mathf.Max(0f, settings.cueFieldPadding.y);
        float hMin = Mathf.Min(settings.cueFieldHeight.x, settings.cueFieldHeight.y);
        float hMax = Mathf.Max(settings.cueFieldHeight.x, settings.cueFieldHeight.y);

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(a.Center, Quaternion.LookRotation(a.Forward, Vector3.up), Vector3.one);

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(0f, (hMin + hMax) * 0.5f, 0f), new Vector3(w, hMax - hMin, d));

        if (settings.keepWalkZoneClear)
        {
            float top = Mathf.Min(settings.walkZoneClearHeight, hMax);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(0f, top * 0.5f, 0f), new Vector3(a.Width, top, a.Depth));
        }
        Gizmos.matrix = old;
    }
}
