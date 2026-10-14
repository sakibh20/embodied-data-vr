using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only (M54): builds the "spark star" mesh used by the semantic visual cue --
/// a tiny core with many sharp, thin, straight spikes shooting out in all directions,
/// like the sparks in Sources/spark_reference.png. Saved to Assets/Meshes/SparkStar.asset.
/// Menu: Tools > Study Visuals > Rebuild Spark Star Mesh.
///
/// Size convention matches the old sphere: longest spike tip at radius 0.5, so the
/// DensityController's sizeRange (object scale) means roughly the same overall size.
/// uv.x = 0 at the core .. 1 at a spike tip (colour gradient + length flicker in the
/// shader); uv.y = per-spike random 0..1 (so each spike flickers on its own).
/// </summary>
public static class SparkStarMeshBuilder
{
    public const string AssetPath = "Assets/Meshes/SparkStar.asset";

    [MenuItem("Tools/Study Visuals/Rebuild Spark Star Mesh")]
    public static void BuildMenu()
    {
        var mesh = Build();
        Debug.Log($"[SparkStar] Saved {AssetPath}: {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris");
    }

    public static Mesh Build(int spikes = 30, int sides = 4, int seed = 11)
    {
        var rnd = new System.Random(seed);
        float R() => (float)rnd.NextDouble();

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // Tiny core: octahedron (flat shaded).
        const float coreR = 0.09f;
        Vector3[] o = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        int[,] f = { {0,2,4},{4,2,1},{1,2,5},{5,2,0},{4,3,0},{1,3,4},{5,3,1},{0,3,5} };
        for (int i = 0; i < 8; i++)
            AddTri(verts, norms, uvs, tris, o[f[i,0]] * coreR, o[f[i,1]] * coreR, o[f[i,2]] * coreR, 0f, 0f, 0f, 0.5f);

        // Spikes: directions spread over the sphere (Fibonacci) with jitter, random lengths.
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int s = 0; s < spikes; s++)
        {
            float y = 1f - 2f * (s + 0.5f) / spikes;
            float rr = Mathf.Sqrt(1f - y * y);
            float th = golden * s;
            Vector3 dir = new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr);
            dir = (dir + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 0.45f).normalized;

            float len = Mathf.Lerp(0.3f, 0.5f, Mathf.Pow(R(), 0.7f));        // tip radius
            float baseR = Mathf.Lerp(0.028f, 0.05f, R());                     // needle thickness
            float baseDist = coreR * 0.6f;
            float spikeRand = R();

            Vector3 a = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(dir, a);
            float twist = R() * Mathf.PI * 2f;
            Vector3 tip = dir * len;
            Vector3 centre = dir * baseDist;
            float tipT = len / 0.5f;   // tips of shorter spikes are less "hot-to-orange"

            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float ang = twist + k * Mathf.PI * 2f / sides;
                ring[k] = centre + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * baseR;
            }
            for (int k = 0; k < sides; k++)
            {
                Vector3 p0 = ring[k], p1 = ring[(k + 1) % sides];
                AddTri(verts, norms, uvs, tris, p0, p1, tip, 0.05f, 0.05f, tipT, spikeRand);
            }
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = "SparkStar" };
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.2f);   // room for shader length flicker

        if (create) AssetDatabase.CreateAsset(mesh, AssetPath);
        else EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static void AddTri(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
                               Vector3 p0, Vector3 p1, Vector3 p2, float u0, float u1, float u2, float spikeRand)
    {
        Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0).normalized;
        // Keep normals pointing outward (away from the centre).
        if (Vector3.Dot(nrm, (p0 + p1 + p2) / 3f) < 0f) { var tmp = p1; p1 = p0; p0 = tmp; var tu = u1; u1 = u0; u0 = tu; nrm = -nrm; }
        int i = v.Count;
        v.Add(p0); v.Add(p1); v.Add(p2);
        n.Add(nrm); n.Add(nrm); n.Add(nrm);
        uv.Add(new Vector2(u0, spikeRand)); uv.Add(new Vector2(u1, spikeRand)); uv.Add(new Vector2(u2, spikeRand));
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }
}
