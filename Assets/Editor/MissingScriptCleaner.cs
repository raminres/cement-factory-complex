using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class MissingScriptCleaner
{
    // ---------------------------------------------------------
    // 1. CLEAN ALL PREFABS IN PROJECT
    // ---------------------------------------------------------
    [MenuItem("Window/Tech Art/Remove Missing Scripts/From All Prefabs in Project")]
    public static void CleanAllPrefabs()
    {
        if (!EditorUtility.DisplayDialog("Remove Missing Scripts", 
            "This will search ALL prefabs in your project and completely strip any missing script references. This is a destructive operation. Are you sure?", 
            "Yes, Strip Them", "Cancel"))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int cleanedPrefabsCount = 0;
        int totalScriptsRemoved = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                
                if (i % 20 == 0)
                {
                    EditorUtility.DisplayProgressBar("Cleaning Prefabs", $"Processing {path}...", (float)i / prefabGuids.Length);
                }

                GameObject prefabInstance = PrefabUtility.LoadPrefabContents(path);
                int removedCount = 0;
                
                Transform[] allTransforms = prefabInstance.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allTransforms)
                {
                    removedCount += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                }

                if (removedCount > 0)
                {
                    totalScriptsRemoved += removedCount;
                    cleanedPrefabsCount++;
                    PrefabUtility.SaveAsPrefabAsset(prefabInstance, path);
                }

                PrefabUtility.UnloadPrefabContents(prefabInstance);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"Project Cleanup Complete! Removed {totalScriptsRemoved} missing script(s) across {cleanedPrefabsCount} prefab(s).");
    }

    // ---------------------------------------------------------
    // 2. CLEAN ACTIVE SCENE
    // ---------------------------------------------------------
    [MenuItem("Window/Tech Art/Remove Missing Scripts/From Current Scene")]
    public static void CleanCurrentScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        
        // Grab all root objects in the current scene to ensure we don't accidentally pull from DontDestroyOnLoad or hidden editor scenes
        GameObject[] rootObjects = activeScene.GetRootGameObjects();
        
        int totalScriptsRemoved = 0;
        int modifiedObjectsCount = 0;

        foreach (GameObject root in rootObjects)
        {
            // Traverse down into every child of the root object (including inactive ones)
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                // This safely handles prefab instances and FBXs in the scene by recording the removal as a prefab override
                int removedCount = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                
                if (removedCount > 0)
                {
                    totalScriptsRemoved += removedCount;
                    modifiedObjectsCount++;
                }
            }
        }

        if (totalScriptsRemoved > 0)
        {
            // Tell Unity the scene has unsaved changes so the user can press Ctrl+S
            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log($"Scene Cleanup Complete! Removed {totalScriptsRemoved} missing script(s) from {modifiedObjectsCount} GameObject(s) in '{activeScene.name}'.");
        }
        else
        {
            Debug.Log($"No missing scripts were found in the scene '{activeScene.name}'.");
        }
    }
}