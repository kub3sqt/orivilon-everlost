using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PhysicsPerformanceSettings
{
    private const string Request = "/tmp/everlost-physics-settings.request";
    static PhysicsPerformanceSettings()
    {
        if (File.Exists(Request)) EditorApplication.delayCall += Run;
    }
    [MenuItem("Tools/Everlost/Apply streaming physics settings")]
    internal static void Run()
    {
        if (File.Exists(Request)) File.Delete(Request);
        var report = new StringBuilder();
        foreach (string path in new[] { "ProjectSettings/TimeManager.asset", "ProjectSettings/DynamicsManager.asset" })
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets.Length == 0) throw new Exception("Settings asset missing: " + path);
            var settings = new SerializedObject(assets[0]);
            var property = settings.GetIterator();
            while (property.Next(true))
            {
                if (property.name == "m_BroadphaseType")
                {
                    report.AppendLine(path + ": " + property.name + " " + property.intValue + " -> 2 (Automatic Box Pruning)");
                    property.intValue = 2;
                }
                if (property.name == "Maximum Allowed Timestep" || property.name == "m_MaximumAllowedTimestep")
                {
                    report.AppendLine(path + ": " + property.name + " " + property.floatValue + " -> 0.06");
                    property.floatValue = 0.06f;
                }
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(assets, path, true);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(Application.dataPath, "../_reports/physics-settings.txt"), report.ToString());
    }
}
