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
