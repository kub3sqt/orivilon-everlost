using System.Collections.Generic;
using Orivilon.Inventory.Inventory;
using Orivilon.World.Objects;
using UnityEngine;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Sebrání instancované trávy (meziúkol).
    ///
    /// <para>Tráva se od kola 4 kreslí přes <see cref="VegetationRenderer"/> bez GameObjectů,
    /// takže na ni raycast hráče nenarazil a přestala jít sebrat. GameObjecty se kvůli tomu
    /// nevracejí: hráč se dívá paprskem, spawner v dosahu projde svá stébla (jen sloupce,
    /// jejichž obálku paprsek protíná) a vrátí nejbližší. Sebrání jde stejnou cestou jako
    /// <see cref="PickupItem.PickUp"/>: inventář, registr zničených objektů podle
    /// deterministického hashe (formát savu beze změny), multiplayer broadcast – a dávka
    /// trávy sloupce se přestaví bez sebraného stébla.</para>
    /// </summary>
    public partial class ObjectSpawner
    {
        /// <summary>Výsledek hledání instancovaného sebratelného objektu.</summary>
        public struct InstancedPickup
        {
            public ObjectSpawner spawner;
            public long hash;
            public PickupItem template;
            public float distance;
            public bool Valid => spawner != null && template != null && template.itemData != null;
        }

        /// <summary>Sloupce s postavenou trávou (v dosahu hráče).</summary>
        private static readonly List<ObjectSpawner> liveGrass = new List<ObjectSpawner>(32);
        private static readonly Dictionary<GameObject, PickupItem> pickupTemplates = new Dictionary<GameObject, PickupItem>(8);

        [System.NonSerialized] private bool grassListed;
        [System.NonSerialized] private bool grassBoundsSet;
        [System.NonSerialized] private Bounds grassPickBounds;
        [System.NonSerialized] private float grassMaxDistance;

        /// <summary>Vzdálenost stébla od paprsku, do které se ještě počítá jako zamířené (m).</summary>
        public static float GrassPickRadius = 0.45f;

        private void AddGrassPickupBounds(Bounds b)
        {
            if (!grassBoundsSet) { grassPickBounds = b; grassBoundsSet = true; }
            else grassPickBounds.Encapsulate(b);
        }

        private void ListGrassPickups()
        {
            if (grassListed || grassBatches.Count == 0) return;
            liveGrass.Add(this);
            grassListed = true;
        }

        private void UnlistGrassPickups()
        {
            grassBoundsSet = false;
            if (!grassListed) return;
            liveGrass.Remove(this);
            grassListed = false;
        }

        private static PickupItem TemplateFor(GameObject prefab)
        {
            if (prefab == null) return null;
            if (!pickupTemplates.TryGetValue(prefab, out PickupItem pi))
            {
                pi = prefab.GetComponentInChildren<PickupItem>(true);
                pickupTemplates[prefab] = pi;
            }
            return pi;
        }

        /// <summary>
        /// Najde instancované sebratelné stéblo, na které míří paprsek, nejdál
        /// <paramref name="maxDistance"/> od počátku. Bez alokací.
        /// </summary>
        public static bool FindInstancedPickup(Ray ray, float maxDistance, out InstancedPickup best)
        {
            best = default;
            float bestPerp = GrassPickRadius * GrassPickRadius;
            Vector3 o = ray.origin, d = ray.direction;

            for (int k = liveGrass.Count - 1; k >= 0; k--)
            {
                ObjectSpawner sp = liveGrass[k];
                if (sp == null) { liveGrass.RemoveAt(k); continue; }
                if (!sp.grassBoundsSet) continue;
                Bounds b = sp.grassPickBounds;
                if (!b.Contains(o) && (!b.IntersectRay(ray, out float enter) || enter > maxDistance)) continue;

                List<PendingDetailData> list = sp.pendingDetails;
                for (int i = 0; i < list.Count; i++)
                {
                    PendingDetailData data = list[i];
                    // Kotva stébla kousek nad zemí – paprsek míří do trsu, ne pod něj.
                    Vector3 v = data.position - o;
                    v.y += 0.2f * data.scale.y;
                    float t = v.x * d.x + v.y * d.y + v.z * d.z;
                    if (t < 0f || t > maxDistance) continue;
                    float px = v.x - d.x * t, py = v.y - d.y * t, pz = v.z - d.z * t;
                    float perp = px * px + py * py + pz * pz;
                    if (perp >= bestPerp) continue;

                    int si = data.spawnableIndex;
                    if (si < 0 || si >= sp.spawnables.Count) continue;
                    SpawnableObject s = sp.spawnables[si];
                    if (s == null || s.category != SpawnCategory.Grass || !sp.grassBatches.ContainsKey(si)) continue;
                    PickupItem tpl = TemplateFor(s.prefab);
                    if (tpl == null || tpl.itemData == null) continue;
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(data.objectHash)) continue;

                    bestPerp = perp;
                    best = new InstancedPickup { spawner = sp, hash = data.objectHash, template = tpl, distance = t };
                }
            }
            return best.Valid;
        }

        /// <summary>
        /// Sebere instancované stéblo: přidá item do inventáře, zapíše hash do registru
        /// zničených objektů, oznámí ho ostatním hráčům a přestaví dávku trávy sloupce.
        /// Při plném inventáři nechá stéblo být (stejně jako <see cref="PickupItem.PickUp"/>).
        /// </summary>
        public static bool PickUpInstanced(InstancedPickup p)
        {
            if (!p.Valid) return false;
            if (InventoryData.Instance == null)
            {
                Debug.LogError("InventoryData.Instance je NULL");
                return false;
            }
            if (SaveSystem.SaveSystem.IsObjectDestroyed(p.hash)) return false;

            if (!InventoryData.Instance.AddItem(p.template.itemData, p.template.itemCount))
            {
                Debug.Log("Inventář je plný – item nelze sebrat");
                return false;
            }

            SaveSystem.SaveSystem.MarkObjectDestroyed(p.hash);
            Orivilon.Multiplayer.NetworkWorldSync.Instance?.BroadcastObjectDestroyed(p.hash);
            p.spawner.RebuildGrass();
            return true;
        }

        /// <summary>
        /// Zničení podle hashe zvenku (multiplayer), když objekt nemá GameObject:
        /// najde stéblo v načtené trávě, zapíše ho a přestaví dávku. Vrací, jestli ho našel.
        /// </summary>
        public static bool DestroyInstancedByHash(long hash)
        {
            for (int k = 0; k < liveGrass.Count; k++)
            {
                ObjectSpawner sp = liveGrass[k];
                if (sp == null) continue;
                List<PendingDetailData> list = sp.pendingDetails;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].objectHash != hash) continue;
                    SaveSystem.SaveSystem.MarkObjectDestroyed(hash);
                    sp.RebuildGrass();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Přestaví dávky trávy (vynechá zničená stébla).</summary>
        private void RebuildGrass()
        {
            float md = grassMaxDistance;
            ReleaseGrassBatches();
            BuildGrassBatches(md);
        }

        /// <summary>Diagnostika: nejbližší sebratelné instancované stéblo k bodu (kotva, jak ji míří paprsek).</summary>
        public static bool NearestInstancedGrass(Vector3 pos, float maxDist, out Vector3 anchor, out long hash)
        {
            anchor = default; hash = 0;
            float best = maxDist * maxDist;
            for (int k = 0; k < liveGrass.Count; k++)
            {
                ObjectSpawner sp = liveGrass[k];
                if (sp == null) continue;
                List<PendingDetailData> list = sp.pendingDetails;
                for (int i = 0; i < list.Count; i++)
                {
                    PendingDetailData data = list[i];
                    float d2 = (data.position - pos).sqrMagnitude;
                    if (d2 >= best) continue;
                    int si = data.spawnableIndex;
                    if (si < 0 || si >= sp.spawnables.Count || !sp.grassBatches.ContainsKey(si)) continue;
                    SpawnableObject s = sp.spawnables[si];
                    if (s == null || s.category != SpawnCategory.Grass || TemplateFor(s.prefab) == null) continue;
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(data.objectHash)) continue;
                    best = d2;
                    anchor = data.position + Vector3.up * (0.2f * data.scale.y);
                    hash = data.objectHash;
                }
            }
            return hash != 0;
        }

        /// <summary>Diagnostika: kolik stébel se v načtené trávě kreslí (bez zničených).</summary>
        public static int LiveGrassDrawn()
        {
            int n = 0;
            for (int k = 0; k < liveGrass.Count; k++)
            {
                ObjectSpawner sp = liveGrass[k];
                if (sp == null) continue;
                List<PendingDetailData> list = sp.pendingDetails;
                for (int i = 0; i < list.Count; i++)
                {
                    int si = list[i].spawnableIndex;
                    if (si < 0 || si >= sp.spawnables.Count || !sp.grassBatches.ContainsKey(si)) continue;
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(list[i].objectHash)) continue;
                    n++;
                }
            }
            return n;
        }

        /// <summary>Počet sloupců s instancovanou trávou v seznamu pro sebrání (diagnostika).</summary>
        public static int LiveGrassColumns => liveGrass.Count;
    }
}
