using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Orivilon.World.Spawning;

[CustomEditor(typeof(ObjectSpawner))]
public class ObjectSpawnerEditor : Editor
{
    private ObjectSpawner spawner;

    private SerializedProperty spawnablesProp;
    private SerializedProperty globalScaleMultiplierProp;
    private SerializedProperty maxObjectsPerChunkProp;
    private SerializedProperty maxGrassPerChunkProp; // ← NOVÉ
    private SerializedProperty seaLevelProp;
    private SerializedProperty spawnCollisionMaskProp;
    private SerializedProperty autoApplyGrassDefaultsProp;

    private int selectedTab
    {
        get { return EditorPrefs.GetInt("ObjectSpawner_SelectedTab", 0); }
        set { EditorPrefs.SetInt("ObjectSpawner_SelectedTab", value); }
    }

    private int selectedIndex
    {
        get { return EditorPrefs.GetInt("ObjectSpawner_SelectedIndex", -1); }
        set { EditorPrefs.SetInt("ObjectSpawner_SelectedIndex", value); }
    }

    private Vector2 scrollPos;

    private readonly string[] tabs = { "Trees", "Stones", "Grass", "Small Objects" };
    private const float thumbSize = 70f;

    private Dictionary<int, List<int>> categoryMap;
    private bool categoryIndexNeedsRebuild = true;

    private void OnEnable()
    {
        spawner = (ObjectSpawner)target;

        spawnablesProp = serializedObject.FindProperty("spawnables");
        globalScaleMultiplierProp = serializedObject.FindProperty("globalScaleMultiplier");
        maxObjectsPerChunkProp = serializedObject.FindProperty("maxObjectsPerChunk");
        maxGrassPerChunkProp = serializedObject.FindProperty("maxGrassPerChunk"); // ← NOVÉ
        seaLevelProp = serializedObject.FindProperty("seaLevel");
        spawnCollisionMaskProp = serializedObject.FindProperty("spawnCollisionMask");

        // Bezpečné načítání autoApplyGrassDefaultsProp - může být null pokud property neexistuje
        autoApplyGrassDefaultsProp = serializedObject.FindProperty("autoApplyGrassDefaults");

        // Inicializace categoryMap
        InitializeCategoryMap();
    }

    private void InitializeCategoryMap()
    {
        categoryMap = new Dictionary<int, List<int>>()
        {
            { (int)SpawnCategory.Trees, new List<int>() },
            { (int)SpawnCategory.Stones, new List<int>() },
            { (int)SpawnCategory.Grass, new List<int>() },
            { (int)SpawnCategory.SmallObjects, new List<int>() },
        };
        categoryIndexNeedsRebuild = true;
    }

    private void BuildCategoryIndex()
    {
        if (!categoryIndexNeedsRebuild && categoryMap != null) return;

        // Zajistíme, že categoryMap je inicializovaná
        if (categoryMap == null)
        {
            InitializeCategoryMap();
        }

        // Vyčistíme všechny seznamy
        foreach (var list in categoryMap.Values)
        {
            list.Clear();
        }

        // Naplníme indexy
        for (int i = 0; i < spawnablesProp.arraySize; i++)
        {
            SerializedProperty element = spawnablesProp.GetArrayElementAtIndex(i);
            if (element != null)
            {
                SerializedProperty categoryProp = element.FindPropertyRelative("category");
                if (categoryProp != null)
                {
                    int cat = categoryProp.enumValueIndex;
                    if (categoryMap.ContainsKey(cat))
                    {
                        categoryMap[cat].Add(i);
                    }
                }
            }
        }

        categoryIndexNeedsRebuild = false;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Object Spawner", EditorStyles.boldLabel);

        // Bezpečné zobrazení properties - kontrola null
        if (globalScaleMultiplierProp != null)
            EditorGUILayout.PropertyField(globalScaleMultiplierProp);

        GUILayout.Space(5);
        EditorGUILayout.LabelField("Object Limits", EditorStyles.boldLabel);

        if (maxObjectsPerChunkProp != null)
            EditorGUILayout.PropertyField(maxObjectsPerChunkProp);

        if (maxGrassPerChunkProp != null) // ← NOVÉ
            EditorGUILayout.PropertyField(maxGrassPerChunkProp);

        GUILayout.Space(5);
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);

        if (seaLevelProp != null)
            EditorGUILayout.PropertyField(seaLevelProp);

        if (spawnCollisionMaskProp != null)
            EditorGUILayout.PropertyField(spawnCollisionMaskProp);

        // Bezpečné zobrazení autoApplyGrassDefaultsProp
        if (autoApplyGrassDefaultsProp != null)
        {
            EditorGUILayout.PropertyField(autoApplyGrassDefaultsProp);
        }

        GUILayout.Space(10);

        int previousTab = selectedTab;
        selectedTab = GUILayout.Toolbar(selectedTab, tabs, GUILayout.Height(25));

        // Pokud se změnila kategorie, zkontroluj jestli vybraná položka patří do nové kategorie
        if (selectedTab != previousTab)
        {
            BuildCategoryIndex();
            if (categoryMap.ContainsKey(selectedTab))
            {
                List<int> newCategoryList = categoryMap[selectedTab];

                // Pokud vybraná položka nepatří do nové kategorie, zruš výběr
                if (selectedIndex != -1 && !newCategoryList.Contains(selectedIndex))
                {
                    selectedIndex = -1;
                }
            }
        }

        GUILayout.Space(10);

        DrawCategory(selectedTab);

        // Aplikuj změny
        if (serializedObject.ApplyModifiedProperties())
        {
            categoryIndexNeedsRebuild = true;

            // Zkontroluj, jestli vybraná položka stále existuje
            if (selectedIndex >= spawnablesProp.arraySize)
            {
                selectedIndex = -1;
            }
        }
    }

    private void DrawCategory(int tab)
    {
        BuildCategoryIndex();

        // Bezpečná kontrola existence kategorie
        if (!categoryMap.ContainsKey(tab))
        {
            EditorGUILayout.HelpBox($"Category {tab} not found!", MessageType.Error);
            return;
        }

        List<int> list = categoryMap[tab];

        // Výška pro jednu řadu
        float viewHeight = thumbSize + 35f;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField($"{(SpawnCategory)tab} - {list.Count} items", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(
            scrollPos,
            GUILayout.Height(viewHeight)
        );

        EditorGUILayout.BeginHorizontal();

        if (list.Count == 0)
        {
            EditorGUILayout.LabelField("No items in this category", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(thumbSize));
        }
        else
        {
            foreach (int index in list)
            {
                DrawItemThumbnail(index);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // Tlačítko pro přidání nové položky
        if (GUILayout.Button($"Add new {(SpawnCategory)tab} item"))
        {
            int newIndex = spawnablesProp.arraySize;
            spawnablesProp.InsertArrayElementAtIndex(newIndex);

            SerializedProperty obj = spawnablesProp.GetArrayElementAtIndex(newIndex);

            // Bezpečné nastavení properties
            SerializedProperty nameProp = obj.FindPropertyRelative("name");
            SerializedProperty categoryProp = obj.FindPropertyRelative("category");
            SerializedProperty spawnChanceProp = obj.FindPropertyRelative("spawnChance");

            if (nameProp != null) nameProp.stringValue = $"New {(SpawnCategory)tab}";
            if (categoryProp != null) categoryProp.enumValueIndex = tab;
            if (spawnChanceProp != null) spawnChanceProp.floatValue = 1f;

            selectedIndex = newIndex;
            categoryIndexNeedsRebuild = true;
            serializedObject.ApplyModifiedProperties();
            return;
        }

        GUILayout.Space(10);

        // Zobraz detaily pouze pokud je vybraná položka z aktuální kategorie
        if (selectedIndex != -1 && list.Contains(selectedIndex))
        {
            DrawSelectedItem(selectedIndex);
        }
        else if (selectedIndex != -1)
        {
            // Pokud je vybraná položka z jiné kategorie, zruš výběr
            selectedIndex = -1;
        }
    }

    private void DrawItemThumbnail(int index)
    {
        if (index < 0 || index >= spawnablesProp.arraySize) return;

        SerializedProperty element = spawnablesProp.GetArrayElementAtIndex(index);
        if (element == null) return;

        SerializedProperty prefab = element.FindPropertyRelative("prefab");
        SerializedProperty name = element.FindPropertyRelative("name");

        Texture2D preview = Texture2D.grayTexture;
        string tooltip = "No name";
        string displayName = "No name";

        if (name != null && !string.IsNullOrEmpty(name.stringValue))
        {
            tooltip = name.stringValue;
            displayName = name.stringValue;
        }

        if (prefab != null && prefab.objectReferenceValue != null)
        {
            preview = AssetPreview.GetAssetPreview(prefab.objectReferenceValue);
            if (preview == null)
                preview = AssetPreview.GetMiniThumbnail(prefab.objectReferenceValue);

            tooltip = $"{displayName}\n({prefab.objectReferenceValue.name})";
        }

        GUILayout.BeginVertical(GUILayout.Width(thumbSize));

        // Označení vybrané položky
        GUI.color = (selectedIndex == index) ? Color.green : Color.white;

        GUIContent buttonContent = new GUIContent("", tooltip);
        if (GUILayout.Button(buttonContent, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize)))
        {
            selectedIndex = index;
            categoryIndexNeedsRebuild = true;
        }

        // Ruční vykreslení preview textury
        Rect buttonRect = GUILayoutUtility.GetLastRect();
        if (preview != null)
        {
            GUI.DrawTexture(buttonRect, preview);
        }

        GUI.color = Color.white;

        GUILayout.Label(displayName, EditorStyles.miniLabel, GUILayout.Width(thumbSize));

        GUILayout.EndVertical();
    }

    private void DrawSelectedItem(int index)
    {
        if (index < 0 || index >= spawnablesProp.arraySize)
        {
            selectedIndex = -1;
            return;
        }

        SerializedProperty element = spawnablesProp.GetArrayElementAtIndex(index);
        if (element == null)
        {
            selectedIndex = -1;
            return;
        }

        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Space(5);
        EditorGUILayout.LabelField("Item Settings", EditorStyles.boldLabel);

        // Ulož aktuální stav před změnami
        EditorGUI.BeginChangeCheck();

        // Bezpečné zobrazení properties
        SerializedProperty nameProp = element.FindPropertyRelative("name");
        SerializedProperty prefabProp = element.FindPropertyRelative("prefab");
        SerializedProperty categoryProp = element.FindPropertyRelative("category");
        SerializedProperty spawnChanceProp = element.FindPropertyRelative("spawnChance");
        SerializedProperty minHeightProp = element.FindPropertyRelative("minHeight");
        SerializedProperty maxHeightProp = element.FindPropertyRelative("maxHeight");
        SerializedProperty minSlopeProp = element.FindPropertyRelative("minSlope");
        SerializedProperty maxSlopeProp = element.FindPropertyRelative("maxSlope");
        SerializedProperty scaleRangeProp = element.FindPropertyRelative("scaleRange");
        SerializedProperty allowedBiomesProp = element.FindPropertyRelative("allowedBiomes");
        SerializedProperty rotateToTerrainProp = element.FindPropertyRelative("rotateToTerrain");
        SerializedProperty usePerlinDensityProp = element.FindPropertyRelative("usePerlinDensity");
        SerializedProperty densityScaleProp = element.FindPropertyRelative("densityScale");
        SerializedProperty densityThresholdProp = element.FindPropertyRelative("densityThreshold");

        if (nameProp != null) EditorGUILayout.PropertyField(nameProp);
        if (prefabProp != null) EditorGUILayout.PropertyField(prefabProp);

        // Ulož aktuální kategorii pro kontrolu změn
        int oldCategory = -1;
        int newCategory = -1;
        if (categoryProp != null)
        {
            oldCategory = categoryProp.enumValueIndex;
            EditorGUILayout.PropertyField(categoryProp);
            newCategory = categoryProp.enumValueIndex;
        }

        if (spawnChanceProp != null)
            EditorGUILayout.Slider(spawnChanceProp, 0f, 100f, "Spawn Chance");

        GUILayout.Space(5);

        // HEIGHT RANGE
        if (minHeightProp != null && maxHeightProp != null)
        {
            float minH = minHeightProp.floatValue;
            float maxH = maxHeightProp.floatValue;
            EditorGUILayout.LabelField("Height Range");
            EditorGUILayout.MinMaxSlider(ref minH, ref maxH, 0f, 1f);
            minHeightProp.floatValue = minH;
            maxHeightProp.floatValue = maxH;
        }

        GUILayout.Space(5);

        // SLOPE RANGE
        if (minSlopeProp != null && maxSlopeProp != null)
        {
            float minS = minSlopeProp.floatValue;
            float maxS = maxSlopeProp.floatValue;
            EditorGUILayout.LabelField("Slope Range");
            EditorGUILayout.MinMaxSlider(ref minS, ref maxS, 0f, 90f);
            minSlopeProp.floatValue = minS;
            maxSlopeProp.floatValue = maxS;
        }

        GUILayout.Space(5);

        if (scaleRangeProp != null) EditorGUILayout.PropertyField(scaleRangeProp);
        if (allowedBiomesProp != null) EditorGUILayout.PropertyField(allowedBiomesProp, true);

        GUILayout.Space(10);

        if (rotateToTerrainProp != null)
        {
            EditorGUILayout.LabelField("Rotation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rotateToTerrainProp);
        }

        GUILayout.Space(10);

        EditorGUILayout.LabelField("Noise Settings", EditorStyles.boldLabel);
        if (usePerlinDensityProp != null) EditorGUILayout.PropertyField(usePerlinDensityProp);

        if (usePerlinDensityProp != null && usePerlinDensityProp.boolValue)
        {
            if (densityScaleProp != null) EditorGUILayout.PropertyField(densityScaleProp);
            if (densityThresholdProp != null) EditorGUILayout.PropertyField(densityThresholdProp);
        }

        GUILayout.Space(10);

        GUI.color = Color.red;
        if (GUILayout.Button("Delete this item"))
        {
            string itemName = "this item";
            if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue))
            {
                itemName = $"'{nameProp.stringValue}'";
            }

            if (EditorUtility.DisplayDialog("Delete Item",
                $"Are you sure you want to delete {itemName}?",
                "Delete", "Cancel"))
            {
                spawnablesProp.DeleteArrayElementAtIndex(index);
                selectedIndex = -1;
                categoryIndexNeedsRebuild = true;
                serializedObject.ApplyModifiedProperties();
                return;
            }
        }
        GUI.color = Color.white;

        // Pokud došlo ke změně kategorie, přebuduj index
        if (EditorGUI.EndChangeCheck())
        {
            if (oldCategory != newCategory)
            {
                categoryIndexNeedsRebuild = true;
            }
        }

        EditorGUILayout.EndVertical();
    }
}