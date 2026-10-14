using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only: renders the two cue prefabs (abstract sphere + semantic electric orb)
/// side by side to a PNG, without entering Play mode, so their look can be compared
/// quickly. Menu: Tools > Study Visuals > Render Cue Preview. Output goes to
/// "Claude outputs/visual_preview/" next to the Unity project folder.
/// </summary>
public static class CueVisualPreview
{
    private const string AbstractPrefab = "Assets/Prefabs/DensityObject.prefab";
    private const string SemanticPrefab = "Assets/Prefabs/SparkObject.prefab";

    [MenuItem("Tools/Study Visuals/Render Cue Preview")]
    public static void RenderMenu()
    {
        string path = Render("preview", 0f);
        Debug.Log($"[CueVisualPreview] Saved {path}");
        EditorUtility.RevealInFinder(path);
    }

    /// <summary>Render the preview to &lt;outputs&gt;/visual_preview/&lt;fileName&gt;.png.
    /// <paramref name="time"/> sets the shader time so animated effects can be sampled.</summary>
    public static string Render(string fileName, float time, int width = 1400, int height = 700)
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Claude outputs/visual_preview"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, fileName + ".png");

        var root = new GameObject("__CueVisualPreview") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            Shader.SetGlobalVector("_Time", new Vector4(time / 20f, time, time * 2f, time * 3f));

            var state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(7);
            Spawn(AbstractPrefab, root.transform, new Vector3(-1.1f, 1.3f, 2.2f));
            Spawn(SemanticPrefab, root.transform, new Vector3(1.1f, 1.3f, 2.2f));
            UnityEngine.Random.state = state;

            var camGo = new GameObject("cam") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(root.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.45f, -0.4f);
            cam.transform.LookAt(new Vector3(0f, 1.3f, 2.2f));
            cam.fieldOfView = 55f;
            cam.allowHDR = true;

            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            cam.targetTexture = null;
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            if (tex != null) Object.DestroyImmediate(tex);
            Object.DestroyImmediate(root);
        }
        return outPath;
    }

    /// <summary>
    /// M55: one image comparing the semantic spark variants -- left to right:
    /// generic sphere | A Spark Star | B Spark Orb | C Spark Star + Glow.
    /// </summary>
    [MenuItem("Tools/Study Visuals/Render Spark Variants")]
    public static void RenderVariantsMenu()
    {
        string path = RenderVariants("spark_variants", 1.7f);
        Debug.Log($"[CueVisualPreview] Saved {path}");
        EditorUtility.RevealInFinder(path);
    }

    public static string RenderVariants(string fileName, float time, int tile = 640)
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Claude outputs/visual_preview"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, fileName + ".png");

        string[] prefabs =
        {
            AbstractPrefab,
            "Assets/Prefabs/Spark_A_Star.prefab",
            "Assets/Prefabs/Spark_B_Orb.prefab",
            "Assets/Prefabs/Spark_C_StarGlow.prefab",
        };

        var sheet = new Texture2D(tile * prefabs.Length, tile, TextureFormat.RGB24, false);
        var state = UnityEngine.Random.state;
        Shader.SetGlobalVector("_Time", new Vector4(time / 20f, time, time * 2f, time * 3f));

        for (int k = 0; k < prefabs.Length; k++)
        {
            // Each variant in its own isolated tile with identical framing (far away from
            // the others so nothing overlaps).
            Vector3 origin = new Vector3(1000f + k * 100f, 0f, 0f);
            var root = new GameObject("__CueVariantTile") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null; Texture2D tex = null;
            try
            {
                UnityEngine.Random.InitState(7);
                SpawnCompact(prefabs[k], root.transform, origin + new Vector3(0f, 1.3f, 2.0f));

                var camGo = new GameObject("cam") { hideFlags = HideFlags.HideAndDontSave };
                camGo.transform.SetParent(root.transform);
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = origin + new Vector3(0f, 1.35f, 0f);
                cam.transform.LookAt(origin + new Vector3(0f, 1.3f, 2.0f));
                cam.fieldOfView = 55f;

                rt = new RenderTexture(tile, tile, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(tile, tile, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, tile, tile), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                sheet.SetPixels(k * tile, 0, tile, tile, tex.GetPixels());
                cam.targetTexture = null;
            }
            finally
            {
                if (rt != null) Object.DestroyImmediate(rt);
                if (tex != null) Object.DestroyImmediate(tex);
                Object.DestroyImmediate(root);
            }
        }
        UnityEngine.Random.state = state;

        // Thin dark separators between tiles.
        for (int k = 1; k < prefabs.Length; k++)
            for (int y = 0; y < tile; y++)
                for (int x = -2; x <= 1; x++) sheet.SetPixel(k * tile + x, y, new Color(0.1f, 0.1f, 0.12f));
        sheet.Apply();
        File.WriteAllBytes(outPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        return outPath;
    }

    private static void SpawnCompact(string prefabPath, Transform parent, Vector3 centre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return;
        var hero = (GameObject)Object.Instantiate(prefab, parent);
        hero.hideFlags = HideFlags.HideAndDontSave;
        hero.transform.position = centre + new Vector3(0f, 0f, -0.6f);
        hero.transform.localScale = Vector3.one * 0.32f;
        for (int i = 0; i < 9; i++)
        {
            var o = (GameObject)Object.Instantiate(prefab, parent);
            o.hideFlags = HideFlags.HideAndDontSave;
            o.transform.position = centre + new Vector3(UnityEngine.Random.Range(-0.75f, 0.75f),
                UnityEngine.Random.Range(-0.55f, 0.55f), UnityEngine.Random.Range(0f, 1.0f));
            o.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.1f, 0.3f);
        }
    }

    private static void Spawn(string prefabPath, Transform parent, Vector3 centre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return;

        // One large "hero" instance up front for detail, plus a scattered cluster.
        var hero = (GameObject)Object.Instantiate(prefab, parent);
        hero.hideFlags = HideFlags.HideAndDontSave;
        hero.transform.position = centre + new Vector3(0f, 0f, -0.9f);
        hero.transform.localScale = Vector3.one * 0.45f;

        for (int i = 0; i < 16; i++)
        {
            var o = (GameObject)Object.Instantiate(prefab, parent);
            o.hideFlags = HideFlags.HideAndDontSave;
            o.transform.position = centre + new Vector3(
                UnityEngine.Random.Range(-0.8f, 0.8f),
                UnityEngine.Random.Range(-0.7f, 0.7f),
                UnityEngine.Random.Range(-0.2f, 1.2f));
            o.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.1f, 0.3f);
        }
    }
}
