using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Orivilon.World.Spawning;

/// <summary>
/// Custom inspector pro ObjectSpawner.
/// Cíle: přehled o tom, co spawnuje v jakém biomu (matice biom × kategorie),
/// rychlé filtrování záznamů podle biomu/kategorie/jména a pohodlná editace
/// biomů přes zaškrtávací mřížku místo rozklikávacího seznamu.
///
/// DŮLEŽITÉ: Pořadí záznamů v seznamu spawnables je součást determinismu světa
/// (index záznamu vstupuje do hashe objektů a do pořadí vyhodnocování).
/// Proto editor nové/duplikované záznamy přidává VÝHRADNĚ na konec seznamu
/// a neumožňuje přesouvání. Mazání záznamů posune indexy všech následujících –
/// v existujících světech se tím změní rozmístění objektů.
/// </summary>
[CustomEditor(typeof(ObjectSpawner))]
public class ObjectSpawnerEditor : Editor
{
    // ---- serializované properties ----
    private SerializedProperty useDebugSeedProp;
    private SerializedProperty debugSeedValueProp;
    private SerializedProperty globalScaleMultiplierProp;
    private SerializedProperty spawnablesProp;
    private SerializedProperty maxObjectsPerChunkProp;
    private SerializedProperty maxGrassPerChunkProp;
    private SerializedProperty seaLevelProp;
    private SerializedProperty forestNoiseScaleProp;
    private SerializedProperty forestThresholdProp;
    private SerializedProperty grassDensityMultiplierProp;
    private SerializedProperty spawnCollisionMaskProp;

    // ---- stav UI (přežije přepnutí inspektoru v rámci session) ----
    private static bool showSettings = false;
    private static bool showMatrix = true;
    private static bool showValidation = true;
    private static string searchText = "";
    private static int categoryFilter = -1;   // -1 = vše
    private static int biomeFilter = 0;       // 0 (None) = vše

    // Odložené strukturální operace – provádí se až po vykreslení celého GUI,
    // protože změna počtu prvků uprostřed vykreslování rozbije IMGUI layout.
    private int pendingDeleteIndex = -1;
    private int pendingDuplicateIndex = -1;

    private const float ThumbSize = 40f;

    /// <summary>Biomy, ve kterých se reálně spawnuje (bez None a oceánů).</summary>
    private static readonly BiomeType[] LandBiomes =
    {
        BiomeType.Grasslands, BiomeType.Hills, BiomeType.RainyFields,
        BiomeType.Coldlands, BiomeType.SnowyLands, BiomeType.IceLands,
        BiomeType.Desert, BiomeType.Savanna, BiomeType.Badlands,
        BiomeType.Swamps, BiomeType.ColdOcean, BiomeType.TemperateOcean,
    };

    private static readonly Color[] CategoryColors =
    {
        new Color(0.45f, 0.75f, 0.40f), // Trees
        new Color(0.65f, 0.65f, 0.70f), // Stones
        new Color(0.55f, 0.85f, 0.55f), // Grass
        new Color(0.85f, 0.70f, 0.45f), // SmallObjects
    };

    private void OnEnable()
    {
        useDebugSeedProp = serializedObject.FindProperty("useDebugSeed");
        debugSeedValueProp = serializedObject.FindProperty("debugSeedValue");
        globalScaleMultiplierProp = serializedObject.FindProperty("globalScaleMultiplier");
        spawnablesProp = serializedObject.FindProperty("spawnables");
        maxObjectsPerChunkProp = serializedObject.FindProperty("maxObjectsPerChunk");
        maxGrassPerChunkProp = serializedObject.FindProperty("maxGrassPerChunk");
        seaLevelProp = serializedObject.FindProperty("seaLevel");
        forestNoiseScaleProp = serializedObject.FindProperty("forestNoiseScale");
        forestThresholdProp = serializedObject.FindProperty("forestThreshold");
        grassDensityMultiplierProp = serializedObject.FindProperty("grassDensityMultiplier");
        spawnCollisionMaskProp = serializedObject.FindProperty("spawnCollisionMask");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSettings();
        EditorGUILayout.Space(6);
        DrawMatrix();
        EditorGUILayout.Space(6);
        DrawValidation();
        EditorGUILayout.Space(6);
        DrawSpawnablesList();

        // Odložené mazání/duplikace – až po vykreslení, jinak IMGUI vyhodí
        // layout výjimku a změny by se neaplikovaly.
        if (pendingDeleteIndex >= 0)
        {
            if (pendingDeleteIndex < spawnablesProp.arraySize)
                spawnablesProp.DeleteArrayElementAtIndex(pendingDeleteIndex);
            pendingDeleteIndex = -1;
        }
        if (pendingDuplicateIndex >= 0)
        {
            if (pendingDuplicateIndex < spawnablesProp.arraySize)
                DuplicateToEnd(pendingDuplicateIndex);
            pendingDuplicateIndex = -1;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // =====================================================================
    // Globální nastavení
    // =====================================================================
    private void DrawSettings()
    {
        showSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showSettings, "Nastavení spawneru");
        if (showSettings)
        {
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useDebugSeedProp);
            if (useDebugSeedProp.boolValue)
            {
                EditorGUILayout.PropertyField(debugSeedValueProp);
                EditorGUILayout.HelpBox("Debug seed je zapnutý – ignoruje se seed ze save souboru!", MessageType.Warning);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Měřítko a limity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(globalScaleMultiplierProp);
            EditorGUILayout.PropertyField(maxObjectsPerChunkProp);
            EditorGUILayout.PropertyField(maxGrassPerChunkProp);
            EditorGUILayout.PropertyField(seaLevelProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Rozložení vegetace", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(forestNoiseScaleProp);
            EditorGUILayout.PropertyField(forestThresholdProp);
            EditorGUILayout.PropertyField(grassDensityMultiplierProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Kolize", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(spawnCollisionMaskProp);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // =====================================================================
    // Matice biom × kategorie
    // =====================================================================
    private void DrawMatrix()
    {
        showMatrix = EditorGUILayout.BeginFoldoutHeaderGroup(showMatrix, "Přehled: biom × kategorie");
        if (!showMatrix)
        {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        string[] catNames = Enum.GetNames(typeof(SpawnCategory));
        int catCount = catNames.Length;

        // spočítat matici
        var counts = new Dictionary<BiomeType, int[]>();
        foreach (BiomeType b in LandBiomes) counts[b] = new int[catCount];

        for (int i = 0; i < spawnablesProp.arraySize; i++)
        {
            SerializedProperty el = spawnablesProp.GetArrayElementAtIndex(i);
            int cat = el.FindPropertyRelative("category").enumValueIndex;
            SerializedProperty biomes = el.FindPropertyRelative("allowedBiomes");
            for (int bi = 0; bi < biomes.arraySize; bi++)
            {
                var b = (BiomeType)biomes.GetArrayElementAtIndex(bi).intValue;
                if (counts.TryGetValue(b, out int[] row) && cat >= 0 && cat < catCount)
                    row[cat]++;
            }
        }

        EditorGUILayout.BeginVertical(GUI.skin.box);

        // hlavička
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Biom", EditorStyles.miniBoldLabel, GUILayout.Width(110));
        for (int c = 0; c < catCount; c++)
            GUILayout.Label(catNames[c], EditorStyles.miniBoldLabel, GUILayout.Width(62));
        GUILayout.Label("Σ", EditorStyles.miniBoldLabel, GUILayout.Width(36));
        EditorGUILayout.EndHorizontal();

        foreach (BiomeType b in LandBiomes)
        {
            int[] row = counts[b];
            int total = 0;
            foreach (int v in row) total += v;

            bool isOcean = b == BiomeType.ColdOcean || b == BiomeType.TemperateOcean;

            EditorGUILayout.BeginHorizontal();

            // kliknutím na název biomu se nastaví filtr seznamu
            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniLabel);
            if ((int)b == biomeFilter) nameStyle.fontStyle = FontStyle.Bold;
            if (GUILayout.Button(b.ToString(), nameStyle, GUILayout.Width(110)))
                biomeFilter = (biomeFilter == (int)b) ? 0 : (int)b;

            for (int c = 0; c < catCount; c++)
            {
                GUI.color = row[c] > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                GUILayout.Label(row[c].ToString(), EditorStyles.miniLabel, GUILayout.Width(62));
            }
            GUI.color = (total == 0 && !isOcean) ? new Color(1f, 0.45f, 0.45f) : Color.white;
            GUILayout.Label(total.ToString(), EditorStyles.miniBoldLabel, GUILayout.Width(36));
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField("Tip: kliknutím na název biomu vyfiltruješ seznam níže.", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // =====================================================================
    // Validace
    // =====================================================================
    private void DrawValidation()
    {
        var problems = new List<string>();
        for (int i = 0; i < spawnablesProp.arraySize; i++)
        {
            SerializedProperty el = spawnablesProp.GetArrayElementAtIndex(i);
            string nm = el.FindPropertyRelative("name").stringValue;
            string label = string.IsNullOrEmpty(nm) ? $"[{i}]" : $"[{i}] {nm}";

            if (el.FindPropertyRelative("prefab").objectReferenceValue == null)
                problems.Add($"{label}: chybí prefab");
            if (el.FindPropertyRelative("allowedBiomes").arraySize == 0)
                problems.Add($"{label}: žádný povolený biom (nikdy se nespawne)");
            if (el.FindPropertyRelative("minHeight").floatValue > el.FindPropertyRelative("maxHeight").floatValue)
                problems.Add($"{label}: minHeight > maxHeight");
            if (el.FindPropertyRelative("minSlope").floatValue > el.FindPropertyRelative("maxSlope").floatValue)
                problems.Add($"{label}: minSlope > maxSlope");
            if (el.FindPropertyRelative("spawnChance").floatValue <= 0f)
                problems.Add($"{label}: spawnChance je 0");
        }

        if (problems.Count == 0) return;

        showValidation = EditorGUILayout.BeginFoldoutHeaderGroup(showValidation, $"Problémy ({problems.Count})");
        if (showValidation)
        {
            int shown = Mathf.Min(problems.Count, 20);
            EditorGUILayout.HelpBox(string.Join("\n", problems.GetRange(0, shown))
                + (problems.Count > shown ? $"\n… a dalších {problems.Count - shown}" : ""), MessageType.Warning);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // =====================================================================
    // Seznam spawnovatelných objektů
    // =====================================================================
    private void DrawSpawnablesList()
    {
        EditorGUILayout.LabelField($"Spawnovatelné objekty ({spawnablesProp.arraySize})", EditorStyles.boldLabel);

        // ---- filtry ----
        EditorGUILayout.BeginHorizontal();
        searchText = EditorGUILayout.TextField(GUIContent.none, searchText, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) searchText = "";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        string[] catNames = Enum.GetNames(typeof(SpawnCategory));
        var catOptions = new string[catNames.Length + 1];
        catOptions[0] = "Všechny kategorie";
        Array.Copy(catNames, 0, catOptions, 1, catNames.Length);
        int catSel = EditorGUILayout.Popup(categoryFilter + 1, catOptions);
        categoryFilter = catSel - 1;

        var biomeOptions = new string[LandBiomes.Length + 1];
        biomeOptions[0] = "Všechny biomy";
        for (int i = 0; i < LandBiomes.Length; i++) biomeOptions[i + 1] = LandBiomes[i].ToString();
        int currentBiomeSel = 0;
        for (int i = 0; i < LandBiomes.Length; i++)
            if ((int)LandBiomes[i] == biomeFilter) { currentBiomeSel = i + 1; break; }
        int biomeSel = EditorGUILayout.Popup(currentBiomeSel, biomeOptions);
        biomeFilter = biomeSel == 0 ? 0 : (int)LandBiomes[biomeSel - 1];
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // ---- položky ----
        int visible = 0;
        for (int i = 0; i < spawnablesProp.arraySize; i++)
        {
            if (!PassesFilter(i)) continue;
            visible++;
            DrawItemRow(i);
        }

        if (visible == 0)
            EditorGUILayout.LabelField("Filtru neodpovídá žádný záznam.", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.Space(4);

        if (GUILayout.Button("+ Přidat nový záznam (na konec seznamu)"))
        {
            int newIndex = spawnablesProp.arraySize;
            spawnablesProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty el = spawnablesProp.GetArrayElementAtIndex(newIndex);
            ApplyDefaults(el, categoryFilter >= 0 ? categoryFilter : 0);
            el.isExpanded = true;
        }

        EditorGUILayout.HelpBox(
            "Pořadí záznamů ovlivňuje determinismus existujících světů – nové záznamy se proto přidávají na konec. "
            + "Smazání záznamu posune indexy všech následujících (v uložených světech se přeskládají objekty).",
            MessageType.Info);
    }

    private bool PassesFilter(int index)
    {
        SerializedProperty el = spawnablesProp.GetArrayElementAtIndex(index);

        if (categoryFilter >= 0 && el.FindPropertyRelative("category").enumValueIndex != categoryFilter)
            return false;

        if (biomeFilter != 0)
        {
            SerializedProperty biomes = el.FindPropertyRelative("allowedBiomes");
            bool found = false;
            for (int bi = 0; bi < biomes.arraySize; bi++)
                if (biomes.GetArrayElementAtIndex(bi).intValue == biomeFilter) { found = true; break; }
            if (!found) return false;
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            string nm = el.FindPropertyRelative("name").stringValue ?? "";
            var prefab = el.FindPropertyRelative("prefab").objectReferenceValue;
            string pf = prefab != null ? prefab.name : "";
            if (nm.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                && pf.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private void DrawItemRow(int index)
    {
        SerializedProperty el = spawnablesProp.GetArrayElementAtIndex(index);
        SerializedProperty nameProp = el.FindPropertyRelative("name");
        SerializedProperty prefabProp = el.FindPropertyRelative("prefab");
        SerializedProperty categoryProp = el.FindPropertyRelative("category");
        SerializedProperty biomesProp = el.FindPropertyRelative("allowedBiomes");

        EditorGUILayout.BeginVertical(GUI.skin.box);

        // ---- hlavička řádku ----
        EditorGUILayout.BeginHorizontal();

        // náhled
        Texture2D preview = null;
        if (prefabProp.objectReferenceValue != null)
        {
            preview = AssetPreview.GetAssetPreview(prefabProp.objectReferenceValue)
                      ?? AssetPreview.GetMiniThumbnail(prefabProp.objectReferenceValue) as Texture2D;
        }
        Rect thumbRect = GUILayoutUtility.GetRect(ThumbSize, ThumbSize, GUILayout.Width(ThumbSize));
        if (preview != null) GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);
        else EditorGUI.DrawRect(thumbRect, new Color(0, 0, 0, 0.2f));

        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal();
        string displayName = string.IsNullOrEmpty(nameProp.stringValue) ? "(bez názvu)" : nameProp.stringValue;
        el.isExpanded = EditorGUILayout.Foldout(el.isExpanded, $"[{index}] {displayName}", true);
        GUILayout.FlexibleSpace();

        int cat = categoryProp.enumValueIndex;
        if (cat >= 0 && cat < CategoryColors.Length)
        {
            GUI.color = CategoryColors[cat];
            GUILayout.Label(((SpawnCategory)cat).ToString(), EditorStyles.miniButton, GUILayout.Width(84));
            GUI.color = Color.white;
        }
        EditorGUILayout.EndHorizontal();

        // souhrn: šance + dohled + biomy
        float chance = el.FindPropertyRelative("spawnChance").floatValue;
        string biomeSummary = BiomeSummary(biomesProp);
        int viewDist = el.FindPropertyRelative("maxViewDistanceChunks").intValue;
        string viewSummary = (cat != (int)SpawnCategory.Grass && viewDist > 0)
            ? $"   •   dohled {viewDist} ch."
            : "";
        EditorGUILayout.LabelField($"{EffectiveChance(chance) * 100f:0.##} % / buňka{viewSummary}   •   {biomeSummary}",
            EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        // ---- detail ----
        if (el.isExpanded)
        {
            EditorGUILayout.Space(2);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(nameProp);
            EditorGUILayout.PropertyField(prefabProp);
            EditorGUILayout.PropertyField(categoryProp);

            SerializedProperty chanceProp = el.FindPropertyRelative("spawnChance");
            EditorGUILayout.Slider(chanceProp, 0f, 100f, "Spawn Chance");
            EditorGUILayout.LabelField(" ",
                $"efektivně {EffectiveChance(chanceProp.floatValue) * 100f:0.###} % na testovaný bod (hodnoty ≤ 1 jsou zlomek, > 1 procenta)",
                EditorStyles.miniLabel);

            DrawMinMax(el, "minHeight", "maxHeight", "Výška (noise 0–1)", 0f, 1f);
            DrawMinMax(el, "minSlope", "maxSlope", "Sklon (°)", 0f, 90f);

            EditorGUILayout.PropertyField(el.FindPropertyRelative("scaleRange"),
                new GUIContent("Scale Range (min/max)"));
            EditorGUILayout.PropertyField(el.FindPropertyRelative("rotateToTerrain"));

            // Dohled objektu (v chuncích). Tráva má globální vzdálenost na EndlessTerrain.
            if (cat == (int)SpawnCategory.Grass)
            {
                EditorGUILayout.LabelField("View Distance",
                    "tráva se řídí globálním EndlessTerrain.grassDistance", EditorStyles.miniLabel);
            }
            else
            {
                SerializedProperty viewDistProp = el.FindPropertyRelative("maxViewDistanceChunks");
                EditorGUILayout.PropertyField(viewDistProp, new GUIContent("View Distance (chunky)",
                    "Max. vzdálenost od hráče (v chuncích), do které je objekt fyzicky ve scéně. "
                    + "0 = neomezeno (objekt je ve všech viditelných chuncích). "
                    + "Vhodné pro malé objekty – klacky, kamínky apod."));
                if (viewDistProp.intValue < 0) viewDistProp.intValue = 0;
                if (viewDistProp.intValue == 0)
                    EditorGUILayout.LabelField(" ", "0 = neomezeno – objekt existuje ve všech viditelných chuncích",
                        EditorStyles.miniLabel);
            }

            SerializedProperty perlinProp = el.FindPropertyRelative("usePerlinDensity");
            EditorGUILayout.PropertyField(perlinProp, new GUIContent("Use Perlin Density",
                "Shlukování podle Perlin noise. Platí jen pro kategorie Stones a SmallObjects – Trees řídí lesní noise, Grass hustotní násobitel."));
            if (perlinProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(el.FindPropertyRelative("densityScale"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("densityThreshold"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Povolené biomy", EditorStyles.boldLabel);
            DrawBiomeGrid(biomesProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Duplikovat (na konec)", GUILayout.Width(150)))
            {
                pendingDuplicateIndex = index;
            }
            GUI.color = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button("Smazat", GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("Smazat záznam",
                    $"Opravdu smazat '{displayName}'?\n\nPozor: posunou se indexy následujících záznamů, "
                    + "což v existujících světech přeskládá spawnuté objekty.",
                    "Smazat", "Zrušit"))
                {
                    pendingDeleteIndex = index;
                }
            }
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        EditorGUILayout.EndVertical();
    }

    // =====================================================================
    // Pomocné kreslení
    // =====================================================================
    private static void DrawMinMax(SerializedProperty el, string minField, string maxField,
        string label, float rangeMin, float rangeMax)
    {
        SerializedProperty minProp = el.FindPropertyRelative(minField);
        SerializedProperty maxProp = el.FindPropertyRelative(maxField);
        float minV = minProp.floatValue;
        float maxV = maxProp.floatValue;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        minV = EditorGUILayout.FloatField(minV, GUILayout.Width(52));
        EditorGUILayout.MinMaxSlider(ref minV, ref maxV, rangeMin, rangeMax);
        maxV = EditorGUILayout.FloatField(maxV, GUILayout.Width(52));
        EditorGUILayout.EndHorizontal();

        minProp.floatValue = Mathf.Clamp(minV, rangeMin, rangeMax);
        maxProp.floatValue = Mathf.Clamp(maxV, minProp.floatValue, rangeMax);
    }

    /// <summary>Mřížka toggle tlačítek pro výběr biomů (místo výchozího seznamu).</summary>
    private void DrawBiomeGrid(SerializedProperty biomesProp)
    {
        var current = new HashSet<int>();
        for (int i = 0; i < biomesProp.arraySize; i++)
            current.Add(biomesProp.GetArrayElementAtIndex(i).intValue);

        bool changed = false;
        const int perRow = 3;
        for (int start = 0; start < LandBiomes.Length; start += perRow)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = start; j < Mathf.Min(start + perRow, LandBiomes.Length); j++)
            {
                int val = (int)LandBiomes[j];
                bool on = current.Contains(val);
                bool newOn = GUILayout.Toggle(on, LandBiomes[j].ToString(), EditorStyles.miniButton);
                if (newOn != on)
                {
                    changed = true;
                    if (newOn) current.Add(val);
                    else current.Remove(val);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Žádný", EditorStyles.miniButton, GUILayout.Width(70)))
        {
            current.Clear();
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        if (changed)
        {
            // zápis zpět se zachováním pořadí podle LandBiomes
            biomesProp.ClearArray();
            int idx = 0;
            foreach (BiomeType b in LandBiomes)
            {
                if (!current.Contains((int)b)) continue;
                biomesProp.InsertArrayElementAtIndex(idx);
                biomesProp.GetArrayElementAtIndex(idx).intValue = (int)b;
                idx++;
            }
        }
    }

    private string BiomeSummary(SerializedProperty biomesProp)
    {
        int n = biomesProp.arraySize;
        if (n == 0) return "⚠ žádný biom";
        var names = new List<string>();
        for (int i = 0; i < Mathf.Min(n, 4); i++)
            names.Add(((BiomeType)biomesProp.GetArrayElementAtIndex(i).intValue).ToString());
        string s = string.Join(", ", names);
        if (n > 4) s += $" +{n - 4}";
        return s;
    }

    /// <summary>Efektivní šance – stejná logika jako ObjectSpawner.GetSpawnChance.</summary>
    private static float EffectiveChance(float raw)
    {
        return raw > 1f ? Mathf.Clamp01(raw / 100f) : Mathf.Clamp01(raw);
    }

    private void ApplyDefaults(SerializedProperty el, int category)
    {
        el.FindPropertyRelative("name").stringValue = $"New {(SpawnCategory)category}";
        el.FindPropertyRelative("prefab").objectReferenceValue = null;
        el.FindPropertyRelative("category").enumValueIndex = category;
        el.FindPropertyRelative("spawnChance").floatValue = 0.05f;
        el.FindPropertyRelative("minHeight").floatValue = 0f;
        el.FindPropertyRelative("maxHeight").floatValue = 1f;
        el.FindPropertyRelative("minSlope").floatValue = 0f;
        el.FindPropertyRelative("maxSlope").floatValue = category == (int)SpawnCategory.Trees ? 7f : 45f;
        el.FindPropertyRelative("scaleRange").vector2Value = new Vector2(0.09f, 0.2f);
        el.FindPropertyRelative("allowedBiomes").ClearArray();
        el.FindPropertyRelative("usePerlinDensity").boolValue = false;
        el.FindPropertyRelative("densityScale").floatValue = 0.05f;
        el.FindPropertyRelative("densityThreshold").floatValue = 0.5f;
        el.FindPropertyRelative("rotateToTerrain").boolValue = true;
        el.FindPropertyRelative("maxViewDistanceChunks").intValue = 0;
    }

    /// <summary>Zkopíruje záznam na konec seznamu (bezpečné pro determinismus).</summary>
    private void DuplicateToEnd(int sourceIndex)
    {
        int newIndex = spawnablesProp.arraySize;
        spawnablesProp.InsertArrayElementAtIndex(newIndex);

        SerializedProperty src = spawnablesProp.GetArrayElementAtIndex(sourceIndex);
        SerializedProperty dst = spawnablesProp.GetArrayElementAtIndex(newIndex);

        dst.FindPropertyRelative("name").stringValue = src.FindPropertyRelative("name").stringValue + " (kopie)";
        dst.FindPropertyRelative("prefab").objectReferenceValue = src.FindPropertyRelative("prefab").objectReferenceValue;
        dst.FindPropertyRelative("category").enumValueIndex = src.FindPropertyRelative("category").enumValueIndex;
        dst.FindPropertyRelative("spawnChance").floatValue = src.FindPropertyRelative("spawnChance").floatValue;
        dst.FindPropertyRelative("minHeight").floatValue = src.FindPropertyRelative("minHeight").floatValue;
        dst.FindPropertyRelative("maxHeight").floatValue = src.FindPropertyRelative("maxHeight").floatValue;
        dst.FindPropertyRelative("minSlope").floatValue = src.FindPropertyRelative("minSlope").floatValue;
        dst.FindPropertyRelative("maxSlope").floatValue = src.FindPropertyRelative("maxSlope").floatValue;
        dst.FindPropertyRelative("scaleRange").vector2Value = src.FindPropertyRelative("scaleRange").vector2Value;
        dst.FindPropertyRelative("usePerlinDensity").boolValue = src.FindPropertyRelative("usePerlinDensity").boolValue;
        dst.FindPropertyRelative("densityScale").floatValue = src.FindPropertyRelative("densityScale").floatValue;
        dst.FindPropertyRelative("densityThreshold").floatValue = src.FindPropertyRelative("densityThreshold").floatValue;
        dst.FindPropertyRelative("rotateToTerrain").boolValue = src.FindPropertyRelative("rotateToTerrain").boolValue;
        dst.FindPropertyRelative("maxViewDistanceChunks").intValue = src.FindPropertyRelative("maxViewDistanceChunks").intValue;

        SerializedProperty srcBiomes = src.FindPropertyRelative("allowedBiomes");
        SerializedProperty dstBiomes = dst.FindPropertyRelative("allowedBiomes");
        dstBiomes.ClearArray();
        for (int i = 0; i < srcBiomes.arraySize; i++)
        {
            dstBiomes.InsertArrayElementAtIndex(i);
            dstBiomes.GetArrayElementAtIndex(i).intValue = srcBiomes.GetArrayElementAtIndex(i).intValue;
        }

        dst.isExpanded = true;
    }
}
