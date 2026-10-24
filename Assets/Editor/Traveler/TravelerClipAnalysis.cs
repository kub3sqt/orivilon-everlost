using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>Analýza kroku klipů Walk/Run: výška a podélná poloha chodidel (prsty) v čase – fáze opory a rychlost chodidla.</summary>
    internal static class TravelerClipAnalysis
    {
        [MenuItem("Tools/Everlost/Character v004/Analyze gait clips")]
        public static void Run()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(TravelerV004Import.Folder + "Everlost_Traveler_Default.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath(TravelerV004Import.Folder + "Everlost_Traveler_Default.fbx").OfType<AnimationClip>().ToArray();
            var scene = EditorSceneManager.NewPreviewScene();
            var sb = new StringBuilder();
            try
            {
                var inst = (GameObject)Object.Instantiate(go);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(inst, scene);
                var all = inst.GetComponentsInChildren<Transform>();
                Transform B(string n) => all.First(t => t.name == n);
                var tl = B("toe.L"); var tr = B("toe.R"); var fl = B("foot.L"); var fr = B("foot.R"); var hips = B("hips");
                foreach (var name in new[] { "Walk", "Run" })
                {
                    var c = clips.First(x => x.name == name);
                    int n = 60;
                    sb.AppendLine("== " + name + " len " + c.length.ToString("F3", CultureInfo.InvariantCulture));
                    sb.AppendLine("i | footL y z | toeL y z | footR y z | toeR y z | hips y");
                    for (int i = 0; i < n; i++)
                    {
                        c.SampleAnimation(inst, c.length * i / n);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1:0.000} {2:0.000} | {3:0.000} {4:0.000} | {5:0.000} {6:0.000} | {7:0.000} {8:0.000} | {9:0.000}",
                            i, fl.position.y, fl.position.z, tl.position.y, tl.position.z, fr.position.y, fr.position.z, tr.position.y, tr.position.z, hips.position.y));
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "gait_clips.txt"), sb.ToString());
            Debug.Log("[ClipAnalysis] hotovo");
        }
    }
}
