using UnityEngine;
using UnityEditor;
using System.IO;

public class FBXMaterialSettingsWindow : EditorWindow
{
    private ModelImporterMaterialImportMode importMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
    private ModelImporterMaterialName nameMode = ModelImporterMaterialName.BasedOnModelNameAndMaterialName;
    private ModelImporterMaterialSearch searchMode = ModelImporterMaterialSearch.Local;
    
    // Added the boolean for the global texture search setting
    private bool searchTexturesGlobally = false; 
    
    private bool extractMaterials = false;

    [MenuItem("Window/Tech Art/FBX Material Updater")]
    public static void ShowWindow()
    {
        GetWindow<FBXMaterialSettingsWindow>("FBX Material Updater");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("FBX Material Import Settings", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();

        importMode = (ModelImporterMaterialImportMode)EditorGUILayout.EnumPopup("Creation Mode", importMode);
        nameMode = (ModelImporterMaterialName)EditorGUILayout.EnumPopup("Naming Convention", nameMode);
        searchMode = (ModelImporterMaterialSearch)EditorGUILayout.EnumPopup("Material Search", searchMode);

        // Added the toggle for Search Textures Globally
        searchTexturesGlobally = EditorGUILayout.Toggle(new GUIContent("Search Textures Globally", "When enabled, textures are searched across the entire project if not found near the model."), searchTexturesGlobally);

        GUILayout.Space(15);
        GUILayout.Label("Post-Import Actions", EditorStyles.boldLabel);
        extractMaterials = EditorGUILayout.Toggle(new GUIContent("Extract Materials", "Extracts embedded materials into a 'Materials' folder next to the FBX."), extractMaterials);

        GUILayout.Space(20);

        Object[] selectedObjects = Selection.GetFiltered<Object>(SelectionMode.Assets);
        int fbxCount = 0;
        foreach (var obj in selectedObjects)
        {
            if (AssetDatabase.GetAssetPath(obj).ToLower().EndsWith(".fbx")) 
                fbxCount++;
        }

        GUI.enabled = fbxCount > 0;
        if (GUILayout.Button($"Apply and Save {fbxCount} Selected FBX(s)", GUILayout.Height(30)))
        {
            ProcessSelectedFBXs(selectedObjects);
        }
        GUI.enabled = true;
        
        if (fbxCount == 0)
        {
            EditorGUILayout.HelpBox("Select one or more FBX files in the Project window to use this tool.", MessageType.Info);
        }
    }

    private void ProcessSelectedFBXs(Object[] selectedObjects)
    {
        int updatedCount = 0;

        foreach (Object obj in selectedObjects)
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);

            if (!string.IsNullOrEmpty(assetPath) && assetPath.ToLower().EndsWith(".fbx"))
            {
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

                if (importer != null)
                {
                    importer.materialImportMode = importMode;
                    importer.materialName = nameMode;
                    importer.materialSearch = searchMode;
                    
                    // Apply the new global search setting to the importer
                    importer.searchTexturesGlobally = searchTexturesGlobally;
                    
                    importer.materialLocation = ModelImporterMaterialLocation.InPrefab; 

                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    
                    if (extractMaterials)
                    {
                        ExtractMaterials(assetPath);
                    }

                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            // Final global save to flush all remaining meta changes to disk
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Successfully processed and saved {updatedCount} FBX file(s).");
        }
    }

    private void ExtractMaterials(string assetPath)
    {
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        
        string folderPath = Path.GetDirectoryName(assetPath);
        string materialsFolderPath = Path.Combine(folderPath, "Materials").Replace("\\", "/");
        bool folderCreated = false;
        bool extractedAny = false;

        foreach (Object subAsset in allAssets)
        {
            if (subAsset is Material && !AssetDatabase.IsMainAsset(subAsset))
            {
                if (!folderCreated && !AssetDatabase.IsValidFolder(materialsFolderPath))
                {
                    AssetDatabase.CreateFolder(folderPath, "Materials");
                    folderCreated = true;
                }

                string matPath = $"{materialsFolderPath}/{subAsset.name}.mat";
                matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);

                string result = AssetDatabase.ExtractAsset(subAsset, matPath);
                
                if (!string.IsNullOrEmpty(result))
                {
                    Debug.LogWarning($"Failed to extract material: {subAsset.name}. Error: {result}");
                }
                else
                {
                    extractedAny = true;
                }
            }
        }

        // Hard save immediately after extracting from this specific FBX
        if (extractedAny)
        {
            AssetDatabase.SaveAssets();
        }
    }
}