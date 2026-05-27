using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automaticky zvýší PATCH verzi až po úspěšném dokončení buildu.
/// Build samotný použije původní verzi nastavenou v Player Settings.
/// </summary>
public class AutoVersionIncrement : IPostprocessBuildWithReport
{
    /// <summary>
    /// Pořadí provedení callbacku v rámci build pipeline.
    /// </summary>
    public int callbackOrder => 0;

    /// <summary>
    /// Metoda volaná po dokončení buildu.
    /// Zvýší verzi pouze v případě úspěšného buildu.
    /// </summary>
    /// <param name="report">Report o průběhu buildu</param>
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogWarning("[Build] Build nebyl úspěšný – verze se nezvyšuje.");
            return;
        }

        string currentVersion = PlayerSettings.bundleVersion;

        // Rozdělení na MAJOR.MINOR.PATCH
        string[] parts = currentVersion.Split('.');
        if (parts.Length != 3)
        {
            Debug.LogError("[Build] Verze musí být ve formátu MAJOR.MINOR.PATCH (např. 1.1.1)");
            return;
        }

        int major = int.Parse(parts[0]);
        int minor = int.Parse(parts[1]);
        int patch = int.Parse(parts[2]);

        patch++; // zvýšení PATCH verze

        string newVersion = $"{major}.{minor}.{patch}";
        PlayerSettings.bundleVersion = newVersion;

        Debug.Log($"[Build] Build dokončen. Verze zvýšena: {currentVersion} → {newVersion}");
    }
}
