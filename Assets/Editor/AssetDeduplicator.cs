using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Security.Cryptography;

public class AssetDeduplicator : EditorWindow
{
    private string usedFolder = "Assets/_Consolidated/Used";
    private string unusedFolder = "Assets/_Consolidated/Unused";

    [MenuItem("Window/Tech Art/Asset Deduplicator")]
    public static void ShowWindow()
    {
        GetWindow<AssetDeduplicator>("Asset Deduplicator");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Project Asset Deduplicator (Hash-Based)", EditorStyles.boldLabel);
        GUILayout.Label("Warning: This is a destructive operation. Backup first!", EditorStyles.wordWrappedLabel);
        
        GUILayout.Space(10);
        usedFolder = EditorGUILayout.TextField("Used Folder", usedFolder);
        unusedFolder = EditorGUILayout.TextField("Unused Folder", unusedFolder);

        GUILayout.Space(20);
        if (GUILayout.Button("Deduplicate Textures (By Hash) & Materials", GUILayout.Height(40)))
        {
            ExecuteDeduplication();
        }
    }

    private void ExecuteDeduplication()
    {
        EnsureFoldersExist();

        try
        {
            var texRemapPaths = BuildTextureRemapByHash();
            var matRemapPaths = BuildMaterialRemapByName();

            EditorUtility.DisplayProgressBar("Deduplicating", "Remapping textures in materials...", 0.4f);
            RemapTexturesInMaterials(texRemapPaths);

            EditorUtility.DisplayProgressBar("Deduplicating", "Remapping materials in scenes...", 0.6f);
            RemapMaterialsInScenes(matRemapPaths);

            EditorUtility.DisplayProgressBar("Deduplicating", "Remapping materials in prefabs...", 0.7f);
            RemapMaterialsInPrefabs(matRemapPaths);

            EditorUtility.DisplayProgressBar("Deduplicating", "Remapping materials in FBX importers...", 0.8f);
            RemapMaterialsInFBXs(matRemapPaths);

            EditorUtility.DisplayProgressBar("Deduplicating", "Moving consolidated assets...", 0.9f);
            MoveAssets(texRemapPaths, "Textures");
            MoveAssets(matRemapPaths, "Materials");

            // Save Project Assets
            AssetDatabase.SaveAssets();
            
            // Force Save Loaded Scenes to lock in the remaps
            EditorSceneManager.SaveOpenScenes();
            
            AssetDatabase.Refresh();
            Debug.Log($"Deduplication Complete! Remapped {texRemapPaths.Count} textures and {matRemapPaths.Count} materials.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private Dictionary<string, string> BuildTextureRemapByHash()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
        Dictionary<string, string> remap = new Dictionary<string, string>();

        using (MD5 md5 = MD5.Create())
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);

                Match match = Regex.Match(fileName, @"(?:_|\s)(\d+)$");
                if (match.Success && match.Groups[1].Value != "1") continue;

                if (i % 50 == 0)
                {
                    EditorUtility.DisplayProgressBar("Hashing Textures", $"Processing {fileName}...", (float)i / guids.Length);
                }

                string absolutePath = Path.Combine(Application.dataPath.Replace("Assets", ""), path);
                
                if (File.Exists(absolutePath))
                {
                    string fileHash = ComputeMD5(absolutePath, md5);

                    if (!groups.ContainsKey(fileHash))
                        groups[fileHash] = new List<string>();
                    
                    groups[fileHash].Add(path);
                }
            }
        }

        foreach (var kvp in groups)
        {
            List<string> paths = kvp.Value;
            if (paths.Count > 1)
            {
                // FIX: Sort strictly by the File Name length, ignoring the folder path depth
                paths.Sort((a, b) => Path.GetFileNameWithoutExtension(a).Length.CompareTo(Path.GetFileNameWithoutExtension(b).Length));
                
                string canonical = paths[0];
                for (int i = 1; i < paths.Count; i++)
                {
                    remap[paths[i]] = canonical;
                }
            }
        }
        return remap;
    }

    private string ComputeMD5(string filePath, MD5 md5)
    {
        using (var stream = File.OpenRead(filePath))
        {
            byte[] hashBytes = md5.ComputeHash(stream);
            return System.BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    private Dictionary<string, string> BuildMaterialRemapByName()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>();
        Dictionary<string, string> remap = new Dictionary<string, string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            
            string baseName = Regex.Replace(fileName, @"\s\d+$|\s\(\d+\)$", "");

            if (!groups.ContainsKey(baseName))
                groups[baseName] = new List<string>();
            
            groups[baseName].Add(path);
        }

        foreach (var kvp in groups)
        {
            List<string> paths = kvp.Value;
            if (paths.Count > 1)
            {
                // FIX: Sort strictly by the File Name length
                paths.Sort((a, b) => Path.GetFileNameWithoutExtension(a).Length.CompareTo(Path.GetFileNameWithoutExtension(b).Length));
                
                string canonical = paths[0];
                for (int i = 1; i < paths.Count; i++)
                {
                    remap[paths[i]] = canonical;
                }
            }
        }
        return remap;
    }

    private void RemapTexturesInMaterials(Dictionary<string, string> texRemapPaths)
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null || mat.shader == null) continue;

            bool modified = false;
            Shader shader = mat.shader;
            
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    string propName = shader.GetPropertyName(i);
                    Texture tex = mat.GetTexture(propName);
                    
                    if (tex != null)
                    {
                        string texPath = AssetDatabase.GetAssetPath(tex);
                        if (texRemapPaths.TryGetValue(texPath, out string canonicalPath))
                        {
                            mat.SetTexture(propName, AssetDatabase.LoadAssetAtPath<Texture>(canonicalPath));
                            modified = true;
                        }
                    }
                }
            }
            if (modified) EditorUtility.SetDirty(mat);
        }
    }

    private void RemapMaterialsInScenes(Dictionary<string, string> matRemapPaths)
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HashSet<Scene> dirtiedScenes = new HashSet<Scene>();

        foreach (Renderer r in renderers)
        {
            Material[] mats = r.sharedMaterials;
            bool modified = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    string matPath = AssetDatabase.GetAssetPath(mats[i]);
                    if (matRemapPaths.TryGetValue(matPath, out string canonicalPath))
                    {
                        mats[i] = AssetDatabase.LoadAssetAtPath<Material>(canonicalPath);
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
                
                // FIX: Record which scenes are being modified so we can tell Unity to save them
                dirtiedScenes.Add(r.gameObject.scene);
            }
        }

        foreach (Scene scene in dirtiedScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private void RemapMaterialsInPrefabs(Dictionary<string, string> matRemapPaths)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            bool modified = false;

            foreach (Renderer r in renderers)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        string matPath = AssetDatabase.GetAssetPath(mats[i]);
                        if (matRemapPaths.TryGetValue(matPath, out string canonicalPath))
                        {
                            mats[i] = AssetDatabase.LoadAssetAtPath<Material>(canonicalPath);
                            modified = true;
                        }
                    }
                }
                if (modified) r.sharedMaterials = mats;
            }

            if (modified) EditorUtility.SetDirty(prefab);
        }
    }

    private void RemapMaterialsInFBXs(Dictionary<string, string> matRemapPaths)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLower().EndsWith(".fbx")) continue;

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null)
            {
                bool modified = false;
                
                foreach (var kvp in matRemapPaths)
                {
                    string duplicateName = Path.GetFileNameWithoutExtension(kvp.Key);
                    Material canonicalMat = AssetDatabase.LoadAssetAtPath<Material>(kvp.Value);

                    if (canonicalMat != null)
                    {
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), duplicateName), canonicalMat);
                        modified = true;
                    }
                }

                if (modified)
                {
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }
            }
        }
    }

    private void MoveAssets(Dictionary<string, string> remapPaths, string subfolder)
    {
        string specificUsedFolder = $"{usedFolder}/{subfolder}";
        string specificUnusedFolder = $"{unusedFolder}/{subfolder}";

        if (!AssetDatabase.IsValidFolder(specificUsedFolder)) Directory.CreateDirectory(specificUsedFolder);
        if (!AssetDatabase.IsValidFolder(specificUnusedFolder)) Directory.CreateDirectory(specificUnusedFolder);

        HashSet<string> canonicals = new HashSet<string>(remapPaths.Values);

        foreach (string masterPath in canonicals)
        {
            string newPath = $"{specificUsedFolder}/{Path.GetFileName(masterPath)}";
            AssetDatabase.MoveAsset(masterPath, AssetDatabase.GenerateUniqueAssetPath(newPath));
        }

        foreach (string duplicatePath in remapPaths.Keys)
        {
            string newPath = $"{specificUnusedFolder}/{Path.GetFileName(duplicatePath)}";
            AssetDatabase.MoveAsset(duplicatePath, AssetDatabase.GenerateUniqueAssetPath(newPath));
        }
    }

    private void EnsureFoldersExist()
    {
        if (!AssetDatabase.IsValidFolder(usedFolder)) Directory.CreateDirectory(usedFolder);
        if (!AssetDatabase.IsValidFolder(unusedFolder)) Directory.CreateDirectory(unusedFolder);
        AssetDatabase.Refresh();
    }
}