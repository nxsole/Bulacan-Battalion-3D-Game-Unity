using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FallenDefendersSetup
{
    private const string ScenePath = "Assets/Scenes/Level 2 Ending Cutscene.unity";

    [InitializeOnLoadMethod]
    private static void ApplyRequestedSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath && !scene.GetRootGameObjects().Any(r => r.name == "FallenDefenders")) Create();
        };
    }

    [MenuItem("Tools/Tirad Pass/Create Fallen Defenders")]
    public static void Create()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("Open Level 2 Ending Cutscene outside Play Mode first."); return; }
        var existing = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "FallenDefenders");
        if (existing != null) { Selection.activeGameObject = existing; Debug.Log("FallenDefenders already exists; no duplicates created."); return; }
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var defenders = all.Where(t => (t.name.StartsWith("Filipino Soldier") || t.name.ToLowerInvariant() == "mateo")
            && t.GetComponentsInChildren<Renderer>(true).Length > 0 && !HasDefenderParent(t)).ToArray();
        if (defenders.Length == 0) { Debug.LogError("No Filipino defender models found; no scene changes made."); return; }
        var sandbags = all.Where(t => t.name.StartsWith("Sandbag") && t.GetComponent<Renderer>() != null).ToArray();
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Create static fallen defenders");
        int undoGroup = Undo.GetCurrentGroup();
        var parent = new GameObject("FallenDefenders");
        Undo.RegisterCreatedObjectUndo(parent, "Create fallen defenders group");
        const string meshFolder = "Assets/Entities/FallenDefenderMeshes";
        if (!AssetDatabase.IsValidFolder(meshFolder)) AssetDatabase.CreateFolder("Assets/Entities", "FallenDefenderMeshes");
        try
        {
            for (int index = 0; index < defenders.Length; index++)
            {
                var original = defenders[index];
                var copy = Object.Instantiate(original.gameObject, parent.transform);
                Undo.RegisterCreatedObjectUndo(copy, "Duplicate defender");
                copy.name = "Fallen_" + original.name + "_" + (index + 1);
                if (PrefabUtility.IsPartOfPrefabInstance(copy))
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                copy.SetActive(false);

                // Keep only highest-detail LOD renderers before converting skinning.
                foreach (var lod in copy.GetComponentsInChildren<LODGroup>(true))
                {
                    var levels = lod.GetLODs();
                    if (levels.Length == 0) continue;
                    var keep = levels[0].renderers;
                    foreach (var renderer in levels.Skip(1).SelectMany(l => l.renderers).Where(r => r != null && !keep.Contains(r)).Distinct())
                        Object.DestroyImmediate(renderer);
                    foreach (var renderer in keep.Where(r => r != null)) renderer.enabled = true;
                }
                foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) { Object.DestroyImmediate(skin); continue; }
                    var mesh = new Mesh { name = copy.name + "_" + skin.name };
                    skin.BakeMesh(mesh);
                    var materials = skin.sharedMaterials;
                    var host = skin.gameObject;
                    bool visible = skin.enabled;
                    var shadows = skin.shadowCastingMode;
                    bool receiveShadows = skin.receiveShadows;
                    Object.DestroyImmediate(skin);
                    var filter = host.GetComponent<MeshFilter>();
                    if (filter == null) filter = host.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = host.GetComponent<MeshRenderer>();
                    if (renderer == null) renderer = host.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials; renderer.enabled = visible;
                    renderer.shadowCastingMode = shadows; renderer.receiveShadows = receiveShadows;
                    var path = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/Defender_" + index + "_" + System.Guid.NewGuid().ToString("N") + ".asset");
                    AssetDatabase.CreateAsset(mesh, path);
                }
                // Visual props retain transforms and static mesh rendering only.
                foreach (var component in copy.GetComponentsInChildren<Component>(true).Reverse())
                {
                    if (component == null || component is Transform || component is MeshFilter || component is MeshRenderer) continue;
                    Object.DestroyImmediate(component);
                }
                foreach (var transform in copy.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.tag = "Untagged";
                    GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                }
                Vector3 position = original.position;
                var nearest = sandbags.OrderBy(t => (t.position - position).sqrMagnitude).FirstOrDefault();
                if (nearest != null && (nearest.position - position).sqrMagnitude < 225f)
                {
                    var near = Vector3.Lerp(position, nearest.position, 0.35f);
                    position.x = near.x; position.z = near.z;
                }
                float yaw = original.eulerAngles.y + (index % 5 - 2) * 28f;
                copy.transform.SetPositionAndRotation(position, Quaternion.Euler(index % 2 == 0 ? 88f : -88f, yaw, index % 3 == 0 ? 16f : -8f));
                copy.SetActive(true);
                var renderers = copy.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    float ground = SurfaceHeight(position, original.position.y, defenders);
                    copy.transform.position += Vector3.up * (ground + 0.025f - bounds.min.y);
                }
                Undo.RecordObject(original.gameObject, "Hide standing defender");
                original.gameObject.SetActive(false);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = parent;
            Debug.Log($"Created {defenders.Length} static fallen defenders; standing originals hidden. Scene saved.", parent);
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    private static bool HasDefenderParent(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
            if (p.name.StartsWith("Filipino Soldier") || p.name.ToLowerInvariant() == "mateo") return true;
        return false;
    }

    private static float SurfaceHeight(Vector3 position, float fallback, Transform[] originals)
    {
        var hits = Physics.RaycastAll(new Vector3(position.x, fallback + 3f, position.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits.OrderBy(h => h.distance))
        {
            if (originals.Any(t => hit.transform == t || hit.transform.IsChildOf(t))) continue;
            if (hit.normal.y < 0.5f || hit.point.y > fallback + 1f) continue;
            return hit.point.y;
        }
        return fallback;
    }
}
