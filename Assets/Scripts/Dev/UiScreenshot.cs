using System.IO;
using UnityEngine;

/// <summary>
/// DEV ONLY, Play mode (docs only): renders the session panel straight-on with its own
/// orthographic camera to a PNG in "Claude outputs/ui_screenshots", so the texts can be
/// reviewed outside VR. Only the panel's layer is drawn, on a plain dark background.
/// </summary>
public static class UiScreenshot
{
    public static string Capture(string name, int width = 2400)
    {
        var view = Object.FindFirstObjectByType<SessionCanvasView>();
        if (view == null || !view.gameObject.activeInHierarchy) return name + ": panel not visible";
        Canvas.ForceUpdateCanvases();
        var rt = (RectTransform)view.transform;
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        float w = Vector3.Distance(c[0], c[3]), h = Vector3.Distance(c[0], c[1]);
        Vector3 centre = (c[0] + c[2]) * 0.5f, fwd = view.transform.forward;
        int H = Mathf.RoundToInt(width * (h / w) * 1.06f);

        var go = new GameObject("UiScreenshotCam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        cam.orthographic = true; cam.orthographicSize = h * 0.53f; cam.aspect = (float)width / H;
        cam.nearClipPlane = 0.01f; cam.farClipPlane = 1.5f;
        cam.transform.SetPositionAndRotation(centre - fwd * 1f, Quaternion.LookRotation(fwd, view.transform.up));
        cam.cullingMask = 1 << view.gameObject.layer;
        var tex = new RenderTexture(width, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = tex; cam.Render();
        RenderTexture.active = tex;
        var img = new Texture2D(width, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, width, H), 0, 0); img.Apply();
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(go); tex.Release(); Object.DestroyImmediate(tex);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Claude outputs/ui_screenshots"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(path, img.EncodeToPNG());
        Object.DestroyImmediate(img);
        return name;
    }
}
