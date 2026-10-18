using System;
using System.Collections.Generic;
using System.IO;
using Orivilon.Core;
using Orivilon.Inventory.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.World.Objects
{
    /// <summary>Uložený vyhozený předmět (world/dropped_items.json).</summary>
    [Serializable]
    public class DroppedItemSaveData
    {
        public string itemID;
        public int amount;
        public Vector3 position;
        public Vector3 euler;
    }

    /// <summary>Obsah world/dropped_items.json. Soubor chybí = svět bez vyhozených předmětů (starší savy).</summary>
    [Serializable]
    public class DroppedItemsSaveData
    {
        public List<DroppedItemSaveData> items = new List<DroppedItemSaveData>();

        /// <summary>
        /// Multiplayer (kolo MP 17.10.2026): hostovy účty inventářů klientů. Ve stejném souboru jako dropy,
        /// aby se stav „předmět ve světě ↔ kusy v účtu“ uložil jedním zápisem. Starší savy pole nemají (prázdné).
        /// </summary>
        public List<Orivilon.Multiplayer.MpPlayerLedger> players = new List<Orivilon.Multiplayer.MpPlayerLedger>();
    }

    /// <summary>
    /// Vyhazování předmětů do světa (Q / Ctrl+Q, klik mimo inventář, přebytek z těžby).
    /// Vyhozený předmět je obyčejný <see cref="PickupItem"/> (sbírá se E jako kamínky) s komponentou
    /// <see cref="DroppedItem"/>: ochrana proti okamžitému sebrání, dosednutí fyziky a persistence.
    /// Model: prefab sebratelného předmětu ze světa (ObjectSpawner) → handPrefab → ikona → kostka,
    /// vždy normalizovaný na <see cref="ItemSize"/>, takže žádný item nepotřebuje vlastní prefab.
    /// Volající odebere kusy ze zdroje AŽ po úspěšném spawnu – při selhání se nic nemění.
    /// Multiplayer (kolo MP 17.10.2026): o existenci a počtu rozhoduje host (NetworkWorldSync.Drops). Klient
    /// pošle žádost (kusy jdou do úschovy), host ověří a vytvoří předmět; klienti mají jen repliky. Ukládá
    /// je jen host do svého světa.
    /// </summary>
    public static class ItemDropService
    {
        /// <summary>Ochrana po vyhození hráčem (Minecraft: 40 ticků).</summary>
        public const float PlayerDropPickupDelay = 2f;
        /// <summary>Ochrana u přebytku z těžby (Minecraft: 10 ticků u dropu z bloku).</summary>
        public const float HarvestDropPickupDelay = 0.5f;
        /// <summary>Největší rozměr modelu v metrech (hráč je ve scéně ~8 m vysoký, viz hrac-meritko-dosah).</summary>
        public const float ItemSize = 1.6f;

        private const string FileName = "dropped_items.json";
        private static readonly List<DroppedItem> active = new List<DroppedItem>(32);
        private static readonly Dictionary<InventoryItemData, GameObject> modelCache = new Dictionary<InventoryItemData, GameObject>();
        private static readonly List<DroppedItemSaveData> unknownItems = new List<DroppedItemSaveData>();
        private static Dictionary<string, InventoryItemData> itemsById;
        private static uint nextNetId;

        /// <summary>Testy: náhrada spawnu (true = „vyhozeno“). QA: vynucené selhání spawnu.</summary>
        public static Func<InventoryItemData, int, float, bool> SpawnOverride;
        public static bool ForceFail;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear(); modelCache.Clear(); unknownItems.Clear(); itemsById = null; nextNetId = 0;
            SpawnOverride = null; ForceFail = false;
        }

        public static IReadOnlyList<DroppedItem> Active => active;
        internal static void Register(DroppedItem d) { if (!active.Contains(d)) active.Add(d); }
        internal static void Unregister(DroppedItem d) => active.Remove(d);

        /// <summary>Offline a host vždy; klient jen se spojením s hostem (žádost jde přes něj).</summary>
        public static bool DropAllowed => !Orivilon.Multiplayer.MultiplayerManager.IsClient
            || (Orivilon.Multiplayer.NetworkWorldSync.Instance != null && Orivilon.Multiplayer.NetworkWorldSync.Instance.CanRequestDrops);

        /// <summary>Kusy daného itemu ležící ve světě (pro invariant inventář + kurzor + svět).</summary>
        public static int CountInWorld(InventoryItemData item)
        {
            int n = 0;
            foreach (var d in active)
            {
                var p = d != null ? d.Pickup : null;
                if (p != null && p.itemCount > 0 && InventoryActions.SameItem(p.itemData, item)) n += p.itemCount;
            }
            return n;
        }

        /// <summary>
        /// Smí hráč při hraní (zavřený inventář) vyhazovat? Ne během loadingu, pauzy, menu, konzole
        /// ani při psaní do textového pole.
        /// </summary>
        public static bool GameplayInputAllowed()
        {
            var gm = GameManager.instance;
            if (gm == null || !gm.IsLoadingComplete || gm.IsPaused || gm.isMenuOpen) return false;
            if (SceneLoader.IsLoading || SceneLoader.InputBlocked) return false;
            if (Orivilon.UI.Refresh.SettingsScreen.BlocksInput) return false;
            return !GameManager.IsTextInputActive();
        }

        // ───────────────────────── spawn ─────────────────────────

        /// <summary>
        /// Vyhodí <paramref name="amount"/> kusů před hráče. Vrací false, když se nenašlo bezpečné
        /// místo nebo spawn selhal – volající pak zdroj NESMÍ měnit.
        /// </summary>
        public static bool TrySpawnInFrontOfPlayer(InventoryItemData item, int amount, float pickupDelay)
        {
            if (item == null || amount <= 0) return false;
            if (SpawnOverride != null) return SpawnOverride(item, amount, pickupDelay);
            if (ForceFail || !DropAllowed) return false;
            if (!TryFindPose(out var pos, out var fwd)) return false;
            // Klient: předmět vytvoří host (true = žádost odešla, kusy jsou v úschově).
            if (Orivilon.Multiplayer.MultiplayerManager.IsClient)
                return Orivilon.Multiplayer.NetworkWorldSync.Instance.RequestDrop(item, amount, pos, fwd, pickupDelay < PlayerDropPickupDelay);
            try
            {
                var d = Create(item, amount, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), pickupDelay, false);
                if (d == null) return false;
                d.Launch(fwd);
                Orivilon.Multiplayer.NetworkWorldSync.Instance?.OnHostDropCreated(d, fwd);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[ItemDrop] Spawn selhal: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Bezpečné místo před hráčem: koule se protáhne od trupu dopředu (stěny), paprsek dolů najde
        /// zem a místo se ověří, že není v geometrii ani v hráči. Když dopředu nejde, zkouší se odklon do stran.
        /// </summary>
        public static bool TryFindPose(out Vector3 pos, out Vector3 fwd)
        {
            pos = default; fwd = Vector3.forward;
            var player = GameObject.FindWithTag("Player");
            var cam = Camera.main;
            if (player == null) return false;
            var pcol = player.GetComponent<CapsuleCollider>() as Collider ?? player.GetComponentInChildren<Collider>();
            if (pcol == null) return false;
            Bounds pb = pcol.bounds;

            Vector3 look = cam != null ? cam.transform.forward : player.transform.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 1e-4f) { look = player.transform.forward; look.y = 0f; }
            if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
            look.Normalize();

            float radius = ItemSize * 0.5f;
            float bodyR = Mathf.Max(pb.extents.x, pb.extents.z);
            float minDist = bodyR + radius + 0.2f;
            float dist0 = bodyR + radius + 1.2f;
            Vector3 torso = new Vector3(pb.center.x, pb.min.y + Mathf.Min(pb.size.y * 0.4f, 3f), pb.center.z);
            int mask = Physics.DefaultRaycastLayers & ~LayerBits("Pickup", "Player", "UI", "Water");

            float[] angles = { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 120f, -120f, 180f };
            foreach (float ang in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * look;
                float dist = dist0;
                if (Physics.SphereCast(torso, radius, dir, out RaycastHit wall, dist0, mask, QueryTriggerInteraction.Ignore))
                    dist = wall.distance - 0.05f;
                if (dist < minDist) continue;

                Vector3 p = torso + dir * dist;
                if (!Physics.Raycast(p + Vector3.up * radius, Vector3.down, out RaycastHit ground, pb.size.y + 25f, mask, QueryTriggerInteraction.Ignore))
                    continue;
                if (ground.point.y > torso.y) continue;   // strmý svah/stěna – výš než trup
                Vector3 spawn = ground.point + Vector3.up * (radius + 0.15f);
                if (Physics.CheckSphere(spawn, radius * 0.85f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (pb.SqrDistance(spawn) < radius * radius) continue;
                pos = spawn; fwd = dir;
                return true;
            }
            return false;
        }

        private static int LayerBits(params string[] names)
        {
            int m = 0;
            foreach (var n in names) { int l = LayerMask.NameToLayer(n); if (l >= 0) m |= 1 << l; }
            return m;
        }

        /// <summary>Postaví vyhozený předmět. settled = načtený ze savu (rovnou kinematický, bez ochrany).</summary>
        public static DroppedItem Create(InventoryItemData item, int amount, Vector3 pos, Quaternion rot, float pickupDelay, bool settled)
        {
            var root = new GameObject("Drop_" + (string.IsNullOrEmpty(item.id) ? item.name : item.id));
            root.SetActive(false);
            var game = SceneManager.GetSceneByName("Game");
            if (game.IsValid() && game.isLoaded && root.scene != game) SceneManager.MoveGameObjectToScene(root, game);
            int layer = LayerMask.NameToLayer("Pickup");
            root.layer = layer >= 0 ? layer : 0;
            root.transform.SetPositionAndRotation(pos, rot);

            Bounds local = BuildVisual(item, root.transform, out Transform billboard);
            if (layer >= 0) SetLayer(root.transform, layer);

            var box = root.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = Vector3.Max(local.size, Vector3.one * (ItemSize * 0.3f));

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (billboard != null) rb.constraints = RigidbodyConstraints.FreezeRotation;

            var pickup = root.AddComponent<PickupItem>();
            pickup.itemData = item;
            pickup.itemCount = amount;

            var d = root.AddComponent<DroppedItem>();
            d.NetId = ++nextNetId;   // replika na klientovi ho přepíše ID od hosta
            d.Init(pickup, rb, billboard, pickupDelay, settled);
            root.SetActive(true);
            return d;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ───────────────────────── vizuál ─────────────────────────

        /// <summary>Model předmětu normalizovaný na ItemSize. Vrací lokální bounds; billboard = ikona otáčená ke kameře.</summary>
        private static Bounds BuildVisual(InventoryItemData item, Transform root, out Transform billboard)
        {
            billboard = null;
            var source = ModelSource(item);
            if (source != null)
            {
                var holder = new GameObject("DropModelHolder");
                holder.SetActive(false);
                try
                {
                    var inst = UnityEngine.Object.Instantiate(source, holder.transform);
                    inst.name = "Model";
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localRotation = Quaternion.identity;
                    inst.transform.localScale = Vector3.one;
                    StripToRenderers(inst);
                    if (MeshBounds(inst, out Bounds b))
                    {
                        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                        if (max > 1e-4f)
                        {
                            float s = ItemSize / max;
                            inst.transform.localScale = Vector3.one * s;
                            inst.transform.localPosition = -b.center * s;
                            inst.transform.SetParent(root, false);
                            return new Bounds(Vector3.zero, b.size * s);
                        }
                    }
                    UnityEngine.Object.Destroy(inst);
                }
                finally { UnityEngine.Object.Destroy(holder); }
            }

            if (item.icon != null)
            {
                var go = new GameObject("Icon");
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = item.icon;
                Vector2 sz = item.icon.bounds.size;
                float max = Mathf.Max(sz.x, sz.y);
                float s = max > 1e-4f ? ItemSize / max : 1f;
                go.transform.localScale = Vector3.one * s;
                go.transform.localPosition = -(Vector3)item.icon.bounds.center * s;
                billboard = go.transform;
                return new Bounds(Vector3.zero, new Vector3(ItemSize, ItemSize, ItemSize * 0.5f));
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
            cube.name = "Cube";
            cube.transform.SetParent(root, false);
            cube.transform.localScale = Vector3.one * (ItemSize * 0.5f);
            return new Bounds(Vector3.zero, Vector3.one * (ItemSize * 0.5f));
        }

        /// <summary>Prefab sebratelného předmětu ze světa se stejným itemem, jinak handPrefab.</summary>
        private static GameObject ModelSource(InventoryItemData item)
        {
            if (modelCache.TryGetValue(item, out var cached)) return cached;
            GameObject found = null;
            var spawner = Orivilon.World.Spawning.ObjectSpawner.Instance;
            if (spawner != null && spawner.spawnables != null)
            {
                foreach (var s in spawner.spawnables)
                {
                    if (s == null || s.prefab == null) continue;
                    var p = s.prefab.GetComponentInChildren<PickupItem>(true);
                    if (p != null && InventoryActions.SameItem(p.itemData, item)) { found = s.prefab; break; }
                }
            }
            if (found == null) found = item.handPrefab;
            modelCache[item] = found;
            return found;
        }

        /// <summary>Bounds z mesh dat (renderery v neaktivním držáku hlásí prázdné bounds).</summary>
        private static bool MeshBounds(GameObject inst, out Bounds b)
        {
            b = default; bool any = false;
            var toLocal = inst.transform.worldToLocalMatrix;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.gameObject.activeSelf) continue;
                Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m == null) continue;
                var mb = m.bounds;
                var mat = toLocal * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = mat.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            return any;
        }

        /// <summary>Z kopie modelu nechá jen Transform + vykreslování (bez skriptů, fyziky a vyšších LOD).</summary>
        private static void StripToRenderers(GameObject inst)
        {
            foreach (var lod in inst.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lod.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                    {
                        if (r == null) continue;
                        if (r.gameObject != inst) r.gameObject.SetActive(false);
                        else UnityEngine.Object.DestroyImmediate(r);
                    }
                UnityEngine.Object.DestroyImmediate(lod);
            }
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var c in inst.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    try { UnityEngine.Object.DestroyImmediate(c); } catch (Exception) { }
                }
            }
        }

        // ───────────────────────── persistence ─────────────────────────

        private static string FilePath =>
            GameManager.selectedWorld == null ? null : Path.Combine(GameManager.selectedWorld.folderPath, "world", FileName);

        /// <summary>Data pro save: všechny aktivní předměty s počtem > 0 + neznámé položky ze souboru (neztratit je).</summary>
        public static DroppedItemsSaveData BuildSaveData()
        {
            var data = new DroppedItemsSaveData();
            foreach (var d in active)
            {
                if (d == null || d.IsReplica || d.Pickup == null || d.Pickup.itemData == null || d.Pickup.itemCount <= 0) continue;
                var t = d.transform;
                data.items.Add(new DroppedItemSaveData
                {
                    itemID = d.Pickup.itemData.id,
                    amount = d.Pickup.itemCount,
                    position = d.RestPosition,
                    euler = t.rotation.eulerAngles
                });
            }
            data.items.AddRange(unknownItems);
            data.players = Orivilon.Multiplayer.MpLedgerStore.Snapshot();
            return data;
        }

        /// <summary>Uloží world/dropped_items.json (volá SaveSystem.SaveEverything). Prázdný seznam soubor smaže.</summary>
        public static void SaveAll()
        {
            string file = FilePath;
            if (file == null || Orivilon.Multiplayer.MultiplayerManager.IsClient) return;
            var data = BuildSaveData();
            if (data.items.Count == 0 && data.players.Count == 0)
            {
                Orivilon.SaveSystem.SaveTransaction.Delete(file);
                return;
            }
            // Kolo MP 3: součást uložení světa (SaveTransaction) – dropy, účty klientů, registr zničených objektů
            // a inventář hosta se zveřejní jedním commitem; mimo uložení světa jako samostatný atomický zápis.
            Orivilon.SaveSystem.SaveTransaction.WriteText(file, JsonUtility.ToJson(data, true));
        }

        /// <summary>
        /// Načte uložené předměty (při startu Game scény). Předchozí instance se nejdřív zruší, takže
        /// opakované volání nic nezdvojí. Předměty jsou kinematické – odstreamování terénu je neshodí.
        /// </summary>
        public static int LoadSaved()
        {
            foreach (var d in active.ToArray()) if (d != null) { d.gameObject.SetActive(false); UnityEngine.Object.Destroy(d.gameObject); }
            active.Clear();
            unknownItems.Clear();
            Orivilon.Multiplayer.MpLedgerStore.Load(null);
            string file = FilePath;
            if (GameManager.selectedWorld != null) Orivilon.SaveSystem.SaveTransaction.EnsureRecovered(GameManager.selectedWorld.folderPath);
            if (file == null || Orivilon.Multiplayer.MultiplayerManager.IsClient || !File.Exists(file)) return 0;
            DroppedItemsSaveData data;
            try { data = JsonUtility.FromJson<DroppedItemsSaveData>(File.ReadAllText(file)); }
            catch (Exception e) { Debug.LogWarning("[ItemDrop] dropped_items.json nejde přečíst: " + e.Message); return 0; }
            if (data == null) return 0;
            Orivilon.Multiplayer.MpLedgerStore.Load(data.players);   // účty klientů zůstávají i při hraní offline
            if (data.items == null) return 0;
            int n = 0;
            foreach (var e in data.items)
            {
                if (e == null || e.amount <= 0) continue;
                var item = ItemById(e.itemID);
                if (item == null) { unknownItems.Add(e); Debug.LogWarning("[ItemDrop] neznámé ID " + e.itemID + " – zachováno v savu"); continue; }
                Create(item, e.amount, e.position, Quaternion.Euler(e.euler), 0f, true);
                n++;
            }
            return n;
        }

        /// <summary>Item podle stabilního ID (síťové zprávy, save). Null = neznámé ID.</summary>
        public static InventoryItemData FindItem(string id) => ItemById(id);

        private static InventoryItemData ItemById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (itemsById == null)
            {
                itemsById = new Dictionary<string, InventoryItemData>();
                foreach (var it in Resources.LoadAll<InventoryItemData>(""))
                    if (it != null && !string.IsNullOrEmpty(it.id)) itemsById[it.id] = it;
            }
            return itemsById.TryGetValue(id, out var r) ? r : null;
        }
    }
}
