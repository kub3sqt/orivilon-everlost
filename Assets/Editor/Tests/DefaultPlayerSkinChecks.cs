using System.IO;
using System.Linq;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools.Tests
{
    /// <summary>Výchozí skin = schválený Cestovatel v004 (Resources/Characters/TravelerV004). Jen editor, nic neukládá.</summary>
    internal static class DefaultPlayerSkinChecks
    {
        [MenuItem("Tools/Everlost/Character/Check fixed default skin")]
        public static void Run()
        {
            var prefab = Resources.Load<GameObject>(DefaultPlayerSkin.ResourcePath);
            var clips = Resources.LoadAll<AnimationClip>(DefaultPlayerSkin.ResourcePath).Select(c => c.name).ToArray();
            string[] need = { "Idle", "Walk", "Run", "Swim", "Chop", "Mine" };
            bool ok = prefab != null && prefab.name == DefaultPlayerSkin.ModelName && need.All(n => clips.Contains(n))
                && Resources.Load<GameObject>(DefaultPlayerSkin.AxePath) != null && Resources.Load<GameObject>(DefaultPlayerSkin.PickaxePath) != null;
            string msg = (ok ? "OK" : "FAIL") + " default skin " + DefaultPlayerSkin.Id + " clips=" + string.Join(",", clips);
            Directory.CreateDirectory(TravelerV004Dump.OutDir);
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "default_skin_check.txt"), msg + "\n");
            if (ok) Debug.Log("[DefaultPlayerSkinChecks] " + msg); else Debug.LogError("[DefaultPlayerSkinChecks] " + msg);
        }
    }
}
