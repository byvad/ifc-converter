using UnityEngine;
using System.IO;
using Conversion.Unity;

// The actual script that defines which object is rendered

public class IfcTest : MonoBehaviour
{
    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Ifc2x3_SampleCastle.ifc");
        var loader = gameObject.AddComponent<IfcRuntimeLoader>();

        loader.Load(path, new IfcLoadOptions { MinAlpha = 0.25 },
            r =>
            {
                if (!r.Succeeded) { Debug.LogError($"[IFC] {r.Error}"); return; }
                Debug.Log($"[IFC] {r.Nodes} nodes, {r.Renderers} renderers, {r.Triangles} tris, " +
                          $"{r.Materials} materials, {r.OpeningsCut} openings cut � " +
                          $"parse {r.ParseMilliseconds}ms, mesh {r.MeshMilliseconds}ms, " +
                          $"instantiate {r.InstantiateMilliseconds}ms");
            },
            (f, stage) => { if (f >= 1f) Debug.Log($"[IFC] {stage}"); });
    }
}