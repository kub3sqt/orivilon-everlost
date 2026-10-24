using System.IO;
using System.Linq;
using System.Text;
using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.Multiplayer;
using Orivilon.Player;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Play Mode QA postavy v004 přes RemoteBridge (menu …): vybavení nástrojů, akce, režimy F5, falešný vzdálený
    /// hráč (stejná cesta jako síťový dodatek) a výpis stavu. Jen editor; používat v izolovaném QA světě.
    /// </summary>
    internal static class TravelerPlayQA
    {
        private const string M = "Tools/Everlost/Character v004/QA/";

        private static InventoryItemData Item(string id) => Resources.LoadAll<InventoryItemData>("").FirstOrDefault(i => i.id == id);

        private static void Select(string id)
        {
            var inv = InventoryData.Instance; var hot = HotbarController.Instance;
            if (inv == null || hot == null) { Out("ERR inventář/hotbar chybí"); return; }
            if (id == null)
            {
                for (int i = 0; i < InventoryData.HotbarSize; i++) if (inv.Slots[i].IsEmpty) { hot.SetActiveSlot(i); Out("slot prázdný " + i); return; }
                Out("ERR žádný prázdný slot"); return;
            }
            var it = Item(id);
            if (it == null) { Out("ERR item " + id); return; }
            int slot = -1;
            for (int i = 0; i < InventoryData.HotbarSize; i++) if (!inv.Slots[i].IsEmpty && inv.Slots[i].item == it) slot = i;
            if (slot < 0) { inv.AddItem(it, 1); for (int i = 0; i < InventoryData.HotbarSize; i++) if (!inv.Slots[i].IsEmpty && inv.Slots[i].item == it) slot = i; }
            if (slot < 0) { Out("ERR " + id + " není v hotbaru"); return; }
            hot.SetActiveSlot(slot);
            Out("vybráno " + id + " slot " + slot);
        }

        [MenuItem(M + "Select axe")] public static void Axe() => Select("everlost:stone_axe");
        [MenuItem(M + "Select pickaxe")] public static void Pick() => Select("everlost:stone_pickaxe");
        [MenuItem(M + "Select sword")] public static void Sword() => Select("everlost:stone_sword");
        [MenuItem(M + "Select berries")] public static void Berries() => Select("everlost:berries");
        [MenuItem(M + "Select empty")] public static void Empty() => Select(null);

        [MenuItem(M + "Ignore cursor lock ON")] public static void IgnOn() { TravelerAvatar.QaIgnoreCursor = true; Out("QaIgnoreCursor on"); }
        [MenuItem(M + "Ignore cursor lock OFF")] public static void IgnOff() { TravelerAvatar.QaIgnoreCursor = false; Out("QaIgnoreCursor off"); }
        [MenuItem(M + "Harvest ON")] public static void HarvestOn() { TravelerAvatar.Local?.SetHarvesting(true); Out("harvest on"); }
        [MenuItem(M + "Harvest OFF")] public static void HarvestOff() { TravelerAvatar.Local?.SetHarvesting(false); Out("harvest off"); }
        [MenuItem(M + "Pickup")] public static void Pickup() { TravelerAvatar.Local?.PlayAction(TravelerAction.Pickup); Out("pickup"); }
        [MenuItem(M + "Use")] public static void Use() { TravelerAvatar.Local?.PlayAction(TravelerAction.Use); Out("use"); }

        private static void Mode(TravelerViewMode m)
        {
            var v = Object.FindFirstObjectByType<TravelerCameraView>();
            if (v == null) { Out("ERR TravelerCameraView chybí"); return; }
            v.SetMode(m); Out("mode " + m);
        }
        [MenuItem(M + "View first person")] public static void Fp() => Mode(TravelerViewMode.FirstPerson);
        [MenuItem(M + "View third back")] public static void Back() => Mode(TravelerViewMode.ThirdBack);
        [MenuItem(M + "View third front")] public static void Front() => Mode(TravelerViewMode.ThirdFront);

        [MenuItem(M + "Turn 90")]
        public static void Turn()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            fpc.SetLook(fpc.transform.eulerAngles.y + 90f, 0f);
            Out("yaw " + fpc.transform.eulerAngles.y);
        }

        /// <summary>Postaví hráče k nejbližšímu stromu (HarvestableObject se sekerou) a namíří kameru na kmen.</summary>
        [MenuItem(M + "Face nearest tree")]
        public static void FaceTree()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            var pi = PlayerInteraction.Instance;
            if (fpc == null || pi == null) return;
            Orivilon.World.Objects.HarvestableObject best = null; float bd = 1e9f;
            foreach (var h in Object.FindObjectsByType<Orivilon.World.Objects.HarvestableObject>(FindObjectsSortMode.None))
            {
                if (h.requiredTool != ToolType.Axe || h.GetComponent<Collider>() == null) continue;
                float d = (h.transform.position - fpc.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }
            if (best == null) { Out("ERR žádný strom"); return; }
            var col = best.GetComponent<Collider>();
            Vector3 c = col.bounds.center; Vector3 away = fpc.transform.position - c; away.y = 0f; away.Normalize();
            float r = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
            Vector3 p = new Vector3(c.x, fpc.transform.position.y, c.z) + away * (r + pi.interactionDistance * .45f + 1.2f);
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~LayerMask.GetMask("Player", "Water"), QueryTriggerInteraction.Ignore)) p.y = hit.point.y + 2.05f;
            fpc.rb.position = p; fpc.transform.position = p; fpc.rb.linearVelocity = Vector3.zero;
            Vector3 eye = p + (fpc.playerCamera.transform.position - fpc.transform.position);
            Vector3 target = new Vector3(c.x, eye.y - 1.5f, c.z);
            Vector3 dir = target - eye;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            fpc.SetLook(yaw, pitch);
            Out("u stromu " + best.name + " vzdálenost " + dir.magnitude.ToString("F1") + " dosah " + pi.interactionDistance + " pitch " + pitch.ToString("F1"));
        }

        private static string HarvestInfo()
        {
            var pi = PlayerInteraction.Instance; if (pi == null) return "";
            var f = typeof(PlayerInteraction).GetField("currentHarvest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var h = f?.GetValue(pi) as Orivilon.World.Objects.HarvestableObject;
            if (h == null) return " target -";
            var hf = typeof(Orivilon.World.Objects.HarvestableObject).GetField("currentHealth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return " target " + h.name + " hp " + hf?.GetValue(h);
        }

        /// <summary>Namíří kameru na nejbližší sebratelný předmět (PickupItem s colliderem) – pro skutečné E.</summary>
        [MenuItem(M + "Face nearest pickup")]
        public static void FacePickup()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            Orivilon.World.Objects.PickupItem best = null; float bd = 1e9f;
            foreach (var p in Object.FindObjectsByType<Orivilon.World.Objects.PickupItem>(FindObjectsSortMode.None))
            {
                if (p.GetComponent<Collider>() == null) continue;
                float d = (p.transform.position - fpc.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) { Out("ERR žádný pickup"); return; }
            Vector3 eye = fpc.playerCamera.transform.position;
            Vector3 dir = best.GetComponent<Collider>().bounds.center - eye;
            fpc.SetLook(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg);
            Out("pickup " + best.name + " dist " + dir.magnitude.ToString("F1") + " dosah " + PlayerInteraction.Instance?.interactionDistance);
        }

        [MenuItem(M + "Face remote")]
        public static void FaceRemote()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            var r = Object.FindObjectsByType<TravelerAvatar>(FindObjectsSortMode.None).FirstOrDefault(x => !x.IsLocal);
            if (fpc == null || r == null) { Out("ERR remote"); return; }
            Vector3 d = r.transform.position - fpc.transform.position;
            fpc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 10f);
            Out("face remote " + r.name + " " + d.magnitude.ToString("F1"));
        }

        [MenuItem(M + "Face sun")]
        public static void FaceSun()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            var sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
            if (fpc == null || sun == null) { Out("ERR sun"); return; }
            Vector3 d = -sun.transform.forward;
            fpc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 5f);
            Out("face sun elev " + (Mathf.Asin(d.y) * Mathf.Rad2Deg).ToString("F0"));
        }

        [MenuItem(M + "Hungry")]
        public static void Hungry()
        {
            var n = Object.FindFirstObjectByType<PlayerNeeds>();
            if (n == null) { Out("ERR PlayerNeeds"); return; }
            n.food = n.maxFood * .4f; Out("food " + n.food);
        }

        [MenuItem(M + "Look level")] public static void LookLevel() => Look(0f);
        [MenuItem(M + "Look down 35")] public static void LookDown() => Look(35f);
        [MenuItem(M + "Look down 50")] public static void LookDown50() => Look(50f);
        private static void Look(float pitch)
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            var f = typeof(FirstPersonController).GetField("pitch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (f != null) f.SetValue(fpc, pitch);
            fpc.playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
            Out("pitch " + pitch + (f == null ? " (pole pitch nenalezeno)" : ""));
        }

        // ── falešný vzdálený hráč: stejná cesta jako síťový dodatek (ReceivePose) ──
        [MenuItem(M + "Fake remote spawn")]
        public static void FakeRemote()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) { Out("ERR hráč"); return; }
            var old = GameObject.Find("QA_FakeRemote"); if (old != null) Object.Destroy(old);
            var go = RemotePlayerController.CreateDefaultVisual();
            go.name = "QA_FakeRemote";
            var col = fpc.GetComponent<Collider>();
            Vector3 fwd = fpc.transform.forward;
            Vector3 p = fpc.transform.position + fwd * 18f;
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 120f, ~LayerMask.GetMask("Player", "Water"), QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 2f;
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Quaternion.Euler(0f, 60f, 0f) * -fwd, Vector3.up));
            go.GetComponent<RemotePlayerController>().SetTargetTransform(go.transform.position, go.transform.rotation);
            go.GetComponent<RemotePlayerController>().SetPlayerName("QA Remote");
            go.AddComponent<FakeRemoteDriver>();
            Out("fake remote at " + p);
        }

        [MenuItem(M + "Fake remote next state")]
        public static void FakeNext()
        {
            var d = Object.FindFirstObjectByType<FakeRemoteDriver>();
            if (d == null) { Out("ERR fake remote"); return; }
            d.state = (d.state + 1) % FakeRemoteDriver.Names.Length; d.t = 0f;
            Out("fake remote state " + FakeRemoteDriver.Names[d.state]);
        }

        private static double recEnd, recNext; private static int recIdx;
        /// <summary>Záznam 14 s: každých 0,4 s screenshot + řádek stavu (pro souběh se skutečným vstupem z klávesnice).</summary>
        [MenuItem(M + "Record 14s")]
        public static void Record()
        {
            recEnd = EditorApplication.timeSinceStartup + 14.0; recNext = 0; recIdx = 0;
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "rec.txt"), "");
            EditorApplication.update -= RecTick; EditorApplication.update += RecTick;
            Out("record start");
        }
        private static void RecTick()
        {
            double t = EditorApplication.timeSinceStartup;
            if (t > recEnd || !Application.isPlaying) { EditorApplication.update -= RecTick; return; }
            if (t < recNext) return;
            recNext = t + .4;
            string name = "rec_" + recIdx.ToString("00");
            ScreenCapture.CaptureScreenshot(Path.Combine(TravelerV004Dump.OutDir, name + ".png"));
            var a = TravelerAvatar.Local;
            string line = name + " mode " + TravelerCameraView.Mode + (a ? " action " + a.Action + " age " + a.ActionAge.ToString("F2") + " speed " + a.Speed.ToString("F2") + " phase " + a.Phase.ToString("F2") + " move " + a.MoveState + " bodyYaw " + (a.Body ? a.Body.transform.localEulerAngles.y.ToString("F0") : "-") : "") + HarvestInfo() + " t " + Time.time.ToString("F2");
            File.AppendAllText(Path.Combine(TravelerV004Dump.OutDir, "rec.txt"), line + "\n");
            recIdx++;
        }

        [MenuItem(M + "Dump state")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            var a = TravelerAvatar.Local;
            sb.AppendLine("time " + Time.time.ToString("F2") + " mode " + TravelerCameraView.Mode + " dist " + TravelerCameraView.CurrentDistance.ToString("F2") + " bodyVisible " + TravelerCameraView.BodyVisibleFromCamera + " crosshairHidden " + PlayerInteraction.HideCrosshair);
            if (a != null)
            {
                sb.AppendLine("local action " + a.Action + " age " + a.ActionAge.ToString("F2") + " speed " + a.Speed.ToString("F2") + " phase " + a.Phase.ToString("F2") + " move " + a.MoveState + " height " + a.BodyHeight.ToString("F2") + " feet " + a.FeetOffset.ToString("F2") + " item " + (a.HeldItem ? a.HeldItem.id : "-") + " scale " + a.ModelScale.ToString("F2"));
                if (a.Body) sb.AppendLine("body pos " + a.Body.transform.position.ToString("F2") + " lossy " + a.Body.transform.lossyScale.ToString("F2") + " actionW " + a.Body.ActionWeight.ToString("F2") + " renderers " + string.Join(",", a.Body.GetComponentsInChildren<Renderer>(true).Select(r => r.name + ":" + (r.enabled ? r.shadowCastingMode.ToString() : "off"))));
                if (a.View) sb.AppendLine("view lp " + a.View.transform.localPosition.ToString("F3") + " renderers " + string.Join(",", a.View.GetComponentsInChildren<Renderer>(true).Select(r => r.name + ":" + (r.enabled ? "on" : "off"))));
            }
            var hir = HandItemRenderer.Instance;
            if (hir != null) sb.AppendLine("HandItemRenderer parent " + (hir.transform.parent ? hir.transform.parent.name : "-") + " item " + (hir.CurrentItem ? hir.CurrentItem.id : "-") + " anim " + (hir.GetComponent<Animator>() ? hir.GetComponent<Animator>().enabled.ToString() : "-"));
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) sb.AppendLine("AudioListener " + l.name + " en " + l.enabled);
            var cam = Camera.main; if (cam) sb.AppendLine("Camera.main " + cam.name + " pos " + cam.transform.position.ToString("F2") + " rot " + cam.transform.eulerAngles.ToString("F1"));
            foreach (var r in Object.FindObjectsByType<TravelerAvatar>(FindObjectsSortMode.None).Where(x => !x.IsLocal))
                sb.AppendLine("remote " + r.name + " action " + r.Action + " move " + r.MoveState + " speed " + r.Speed.ToString("F2") + " item " + (r.HeldItem ? r.HeldItem.id : "-") + " body " + (r.Body ? r.Body.transform.lossyScale.ToString("F2") : "-"));
            Directory.CreateDirectory(TravelerV004Dump.OutDir);
            File.AppendAllText(Path.Combine(TravelerV004Dump.OutDir, "play_state.txt"), sb.ToString() + "\n");
            Out("dump ok");
        }

        private static void Out(string s) { Debug.Log("[TravelerPlayQA] " + s); }
    }

    /// <summary>Krmí falešného vzdáleného hráče stavem přes TravelerNetworkPose.Apply (jako zprávy 20 Hz).</summary>
    internal sealed class FakeRemoteDriver : MonoBehaviour
    {
        public static readonly string[] Names = { "idle-axe", "walk-axe", "run-pickaxe", "chop", "mine", "swim", "pickup", "air" };
        public int state; public float t;
        private float send, age;
        private void Update()
        {
            t += Time.deltaTime; send += Time.deltaTime; age += Time.deltaTime;
            if (send < .05f) return;
            send = 0f;
            var pose = new TravelerNetworkPose { height = 8f, feet = -2f, item = new FixedString64Bytes("everlost:stone_axe") };
            switch (Names[state])
            {
                case "walk-axe": pose.speed = 30f; break;
                case "run-pickaxe": pose.speed = 40f; pose.item = new FixedString64Bytes("everlost:stone_pickaxe"); break;
                case "chop": pose.action = (byte)TravelerAction.Harvest; pose.age = Mathf.Repeat(t, 1f); break;
                case "mine": pose.action = (byte)TravelerAction.Harvest; pose.age = Mathf.Repeat(t, 1f); pose.item = new FixedString64Bytes("everlost:stone_pickaxe"); break;
                case "swim": pose.speed = 11f; pose.move = (byte)PlayerMoveState.Swimming; pose.item = default; break;
                case "pickup": pose.action = (byte)TravelerAction.Pickup; pose.age = Mathf.Repeat(t, 1.5f); if (pose.age > TravelerAvatar.PickupDuration) pose.action = 0; pose.item = default; break;
                case "air": pose.move = (byte)PlayerMoveState.Airborne; pose.speed = 30f; break;
            }
            pose.Apply(GetComponent<RemotePlayerController>());
        }
    }
}
