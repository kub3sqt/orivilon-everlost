using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using Orivilon.World.Objects;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>QA: vyhoď sekeru (Q cesta), namiř na ni a seber ji stejnou cestou jako klávesa E; počty v inventáři před/po.</summary>
    internal static class TravelerPickupQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Pickup test near (no back-off)")]
        public static void RunNear() { PickupQARunner.backOff = false; Start(); }

        [MenuItem("Tools/Everlost/Character v004/QA/Pickup test (drop axe, E)")]
        public static void Run() { PickupQARunner.backOff = true; Start(); }

        private static void Start()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            fpc.gameObject.AddComponent<PickupQARunner>();
        }
    }

    internal sealed class PickupQARunner : MonoBehaviour
    {
        public static bool backOff = true;
        private float t; private int step; private readonly StringBuilder sb = new StringBuilder();
        private InventoryItemData axe; private DroppedItem drop;
        private static int Count(InventoryItemData it) => InventoryData.Instance.Slots.Where(s => !s.IsEmpty && s.item == it).Sum(s => s.amount);

        private void Start()
        {
            axe = Resources.LoadAll<InventoryItemData>("").First(i => i.id == "everlost:stone_axe");
            TravelerPlayQA_Select();
            sb.AppendLine("start: sekery v inventáři " + Count(axe) + ", v ruce " + (PlayerEquipment.HeldTool ? PlayerEquipment.HeldTool.id : "-") + ", aktivní slot " + HotbarController.Instance.GetType().GetField("currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(HotbarController.Instance));
            bool ok = HotbarController.Instance.DropActive(false);
            sb.AppendLine("Q (DropActive): " + ok + " → sekery " + Count(axe) + ", v ruce " + (PlayerEquipment.HeldTool ? PlayerEquipment.HeldTool.id : "-"));
        }

        private void TravelerPlayQA_Select() { TravelerPlayQA.Axe(); }

        private void Update()
        {
            t += Time.deltaTime;
            if (step == 0 && t > 3f)
            {
                drop = Object.FindObjectsByType<DroppedItem>(FindObjectsSortMode.None).Where(d => d.Pickup != null && d.Pickup.itemData == axe)
                    .OrderBy(d => (d.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
                sb.AppendLine("drop: " + (drop ? drop.name + " CanPickUp " + drop.CanPickUp + " settled " + drop.Settled + " vzdálenost " + (drop.transform.position - transform.position).magnitude.ToString("F1") : "nenalezen"));
                if (drop == null) { Finish(); return; }
                var fpc = GetComponent<FirstPersonController>();
                // Couvnout tak, aby šla sekera zaměřit v mezích sklonu pohledu (maxLookAngle).
                Vector3 away = transform.position - drop.transform.position; away.y = 0f;
                if (away.magnitude < 9f && backOff)
                {
                    Vector3 np = drop.transform.position + away.normalized * 9f; np.y = transform.position.y + 1f;
                    fpc.rb.position = np; transform.position = np; fpc.rb.linearVelocity = Vector3.zero;
                    sb.AppendLine("couvnuto na 9 m od sekery");
                }
                step = 5; t = 0f; return;
            }
            else if (step == 5 && t > 1f)
            {
                var fpc = GetComponent<FirstPersonController>();
                Vector3 dir = drop.GetComponent<Collider>().bounds.center - fpc.playerCamera.transform.position;
                fpc.SetLook(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Clamp(-Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -50, 50));
                sb.AppendLine("sklon pohledu " + (-Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg).ToString("F0") + "° (max " + fpc.maxLookAngle + ")");
                step = 1; t = 0f;
            }
            else if (step == 1 && t > .6f)
            {
                var pi = PlayerInteraction.Instance;
                var cur = typeof(PlayerInteraction).GetField("currentPickup", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pi) as PickupItem;
 var ray = Camera.main.ViewportPointToRay(new Vector3(.5f, .5f));
                var dc = drop.GetComponent<Collider>();
                sb.AppendLine("úhel paprsek↔sekera " + Vector3.Angle(ray.direction, dc.bounds.center - ray.origin).ToString("F1") + "°, collider " + dc.GetType().Name + " trig " + dc.isTrigger + " en " + dc.enabled + " vrstva " + LayerMask.LayerToName(dc.gameObject.layer) + " meze " + dc.bounds.size.ToString("F2") + ", pitch kamery " + Camera.main.transform.eulerAngles.x.ToString("F0"));
                sb.AppendLine("paprsek PlayerInteraction: " + (Physics.Raycast(ray, out var hh, pi.interactionDistance) ? hh.collider.name + " @" + hh.distance.ToString("F1") + " vrstva " + LayerMask.LayerToName(hh.collider.gameObject.layer) : "nic do " + pi.interactionDistance) + ", k sekeře " + (drop.GetComponent<Collider>().bounds.center - ray.origin).magnitude.ToString("F1"));
                sb.AppendLine("zaměřeno (Press E): " + (cur != null ? cur.name + (cur == drop.Pickup ? " = vyhozená sekera" : " (jiný předmět)") : "nic") + " | dosah " + pi.interactionDistance);
                if (cur != null)
                {
                    // Stejné volání jako větev klávesy E v PlayerInteraction.Update.
                    TravelerAvatar.Local?.PlayAction(TravelerAction.Pickup);
                    cur.PickUp();
                }
                step = 2; t = 0f;
            }
            else if (step == 2 && t > .5f)
            {
                sb.AppendLine("po E: sekery " + Count(axe) + ", v ruce " + (PlayerEquipment.HeldTool ? PlayerEquipment.HeldTool.id : "-") + ", drop existuje " + (drop != null) + ", akce " + TravelerAvatar.Local?.Action);
                Finish();
            }
        }

        private void Finish()
        {
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "pickup_qa.txt"), sb.ToString());
            Debug.Log("[PickupQA] " + sb.ToString().Replace("\n", " | "));
            Destroy(this);
        }
    }
}
