using UnityEngine;
using UnityEditor;
using System.Text;
public static class MtnDiag
{
    [MenuItem("Tools/Mountain Environment/Diag")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var t = Object.FindFirstObjectByType<Terrain>();
        var td = t.terrainData;
        sb.AppendLine("pos=" + t.transform.position + " size=" + td.size + " detailRes=" + td.detailResolution + " patch=" + td.detailResolutionPerPatch + " dist=" + t.detailObjectDistance + " dens=" + t.detailObjectDensity + " drawInstanced=" + t.drawInstanced + " drawTreesAndFoliage=" + t.drawTreesAndFoliage + " scatter=" + td.detailScatterMode);
        for (int i = 0; i < td.detailPrototypes.Length; i++)
        {
            var p = td.detailPrototypes[i];
            sb.AppendLine("proto " + i + " mesh=" + p.usePrototypeMesh + " obj=" + (p.prototype ? p.prototype.name + " path=" + AssetDatabase.GetAssetPath(p.prototype) : "null") + " inst=" + p.useInstancing + " render=" + p.renderMode + " h=" + p.minHeight + "-" + p.maxHeight + " w=" + p.minWidth + "-" + p.maxWidth + " density=" + p.density + " healthy=" + p.healthyColor + " noise=" + p.noiseSpread + " holeEdge=" + p.holeEdgePadding);
            if (p.prototype)
                foreach (var r in p.prototype.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) sb.AppendLine("   mat " + (m ? m.name + " shader=" + m.shader.name + " instancing=" + m.enableInstancing : "null"));
            long sum = 0; var d = td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, i);
            foreach (var v in d) sum += v;
            sb.AppendLine("   detail sum=" + sum);
        }
        string l1 = AssetDatabase.GUIDToAssetPath("02bc0e2176f6a4e4ea3faf0c622664cf");
        var ld = AssetDatabase.LoadAssetAtPath<TerrainData>(l1);
        sb.AppendLine("L1 path=" + l1);
        if (ld != null) for (int i = 0; i < ld.detailPrototypes.Length; i++) { var p = ld.detailPrototypes[i]; sb.AppendLine("L1 proto " + i + " mesh=" + p.usePrototypeMesh + " obj=" + (p.prototype ? p.prototype.name : "null") + " tex=" + (p.prototypeTexture ? p.prototypeTexture.name : "null") + " inst=" + p.useInstancing + " render=" + p.renderMode + " h=" + p.minHeight + "-" + p.maxHeight + " w=" + p.minWidth + "-" + p.maxWidth); }
        foreach (var tt in Object.FindObjectsByType<Terrain>()) sb.AppendLine("terrain " + tt.name + " active=" + tt.gameObject.activeInHierarchy);
        sb.AppendLine("QS detail: " + QualitySettings.GetQualityLevel() + " lodBias=" + QualitySettings.lodBias);
        System.IO.File.WriteAllText("_diag2.txt", sb.ToString());
    }
}
