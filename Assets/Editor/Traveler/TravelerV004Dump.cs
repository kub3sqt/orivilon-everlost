using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>Diagnostika importu v004: hierarchie, meshe, materiály, klipy, pózy kostí. Jen editor, píše do _reports/traveler-v004.</summary>
    internal static class TravelerV004Dump
    {
        public static string OutDir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_reports", "traveler-v004");

        [MenuItem("Tools/Everlost/Character v004/Dump import")]
        public static void Dump()
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder();
            string p = TravelerV004Import.Folder + "Everlost_Traveler_Default.fbx";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            sb.AppendLine("== " + p + " loaded=" + (go != null));
            if (go != null)
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    sb.AppendLine(HPath(t, go.transform) + "  lp=" + V(t.localPosition) + " lr=" + V(t.localEulerAngles) + " ls=" + V(t.localScale) + " wp=" + V(t.position));
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r as SkinnedMeshRenderer;
                    Mesh m = smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    sb.AppendLine("R " + r.name + " " + r.GetType().Name + " mesh=" + (m ? m.name + " v" + m.vertexCount + " sub" + m.subMeshCount + " b=" + m.bounds : "-") +
                        (smr ? " bones=" + smr.bones.Length + " root=" + (smr.rootBone ? smr.rootBone.name : "-") : "") +
                        " mats=" + string.Join(",", r.sharedMaterials.Select(x => x ? x.name + "/" + x.shader.name + "/" + (x.HasProperty("_BaseColor") ? x.GetColor("_BaseColor").ToString() : "?") : "null")));
                }
                var anim = go.GetComponent<Animator>();
                sb.AppendLine("Animator=" + (anim != null) + " avatar=" + (anim && anim.avatar ? anim.avatar.name + " valid=" + anim.avatar.isValid : "-"));
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            foreach (var c in clips)
                sb.AppendLine("CLIP " + c.name + " len=" + c.length.ToString("F3") + " fr=" + c.frameRate + " loop=" + c.isLooping + " legacy=" + c.legacy + " curves=" + AnimationUtility.GetCurveBindings(c).Length);

            if (go != null && clips.Length > 0)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var inst = (GameObject)Object.Instantiate(go);
                    SceneManager_Move(inst, scene);
                    string[] watch = { "head", "hand.R", "hand.L", "prop.R", "foot.L", "foot.R", "spine.003", "neck" };
                    foreach (var c in clips)
                    {
                        foreach (float fr in new[] { 0f, 0.25f, 0.5f, 0.75f })
                        {
                            c.SampleAnimation(inst, c.length * fr);
                            sb.Append("POSE " + c.name + " t" + fr);
                            foreach (var w in watch)
                            {
                                var t = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == w);
                                if (t) sb.Append(" | " + w + " p=" + V(t.position) + " r=" + V(t.rotation.eulerAngles) + " up=" + V(t.up) + " right=" + V(t.right));
                            }
                            sb.AppendLine();
                        }
                    }
                    // vnější meze celé posed postavy pro Idle t0
                    clips.First(c => c.name == "Idle").SampleAnimation(inst, 0);
                    var b = new Bounds(inst.transform.position, Vector3.zero);
                    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var baked = new Mesh(); smr.BakeMesh(baked, true);
                        foreach (var v in baked.vertices) b.Encapsulate(smr.transform.TransformPoint(v));
                        sb.AppendLine("BAKED " + smr.name + " " + Bounds(baked, smr.transform));
                    }
                    sb.AppendLine("IDLE BOUNDS " + b);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }

            foreach (var tp in new[] { TravelerV004Import.Folder + "Tool_Axe.fbx", TravelerV004Import.Folder + "Tool_Pickaxe.fbx", "Assets/stone_axe.prefab", "Assets/stone_pickaxe.fbx" })
            {
                var t = AssetDatabase.LoadAssetAtPath<GameObject>(tp);
                sb.AppendLine("== TOOL " + tp + " " + (t != null));
                if (t == null) continue;
                foreach (var tr in t.GetComponentsInChildren<Transform>(true))
                    sb.AppendLine("  " + HPath(tr, t.transform) + " lp=" + V(tr.localPosition) + " lr=" + V(tr.localEulerAngles) + " ls=" + V(tr.localScale) + " comps=" + string.Join(",", tr.GetComponents<Component>().Select(c => c.GetType().Name)));
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh) sb.AppendLine("  MESH " + mf.name + " b=" + mf.sharedMesh.bounds + " v" + mf.sharedMesh.vertexCount + " world=" + Bounds(mf.sharedMesh, mf.transform, t.transform));
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine("  MAT " + r.name + " " + string.Join(",", r.sharedMaterials.Select(x => x ? x.name + "/" + x.shader.name : "null")));
            }
            File.WriteAllText(System.IO.Path.Combine(OutDir, "dump.txt"), sb.ToString());
            Debug.Log("[TravelerV004Dump] hotovo");
        }

        private static void SceneManager_Move(GameObject g, UnityEngine.SceneManagement.Scene s) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, s);
        private static string V(Vector3 v) => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";
        private static string HPath(Transform t, Transform root) { string s = t.name; while (t != root && t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
        private static string Bounds(Mesh m, Transform t, Transform rel = null)
        {
            var b = new Bounds(); bool first = true;
            foreach (var v in m.vertices)
            {
                var w = t.TransformPoint(v); if (rel) w = rel.InverseTransformPoint(w);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
            return "min" + V(b.min) + " max" + V(b.max);
        }
    }
}
