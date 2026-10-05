using Orivilon.Core;
using Orivilon.Player;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Těžení a stavění ve voxelovém terénu – pověsit na hráče nebo na kameru.
    ///
    /// <para>Je to záměrně samostatná komponenta, ne zásah do <c>PlayerItemUse</c>: kopání
    /// se v survival hře váže na nářadí, vytrvalost a drop materiálu, a to jsou rozhodnutí
    /// o hře, ne o generátoru. Volají se odsud jen <see cref="VoxelTerrain.Dig"/>
    /// a <see cref="VoxelTerrain.Place"/>.</para>
    ///
    /// <para><b>Nástroj.</b> Bez krumpáče v ruce se neděje nic. Podmínka se nečte z vlastního
    /// odkazu na konkrétní item, ale z <see cref="PlayerEquipment.HoldingPickaxe"/> – tentýž
    /// zdroj pravdy, na který se ptá i ovladač kamery, když má potlačit zoom. Dva nezávislé
    /// testy téhož by se dřív nebo později rozešly a projevilo by se to jako „pravé tlačítko
    /// občas zoomuje uprostřed stavění".</para>
    /// </summary>
    [AddComponentMenu("Orivilon/Voxel Dig Tool")]
    public class VoxelDigTool : MonoBehaviour
    {
        [Tooltip("Odkud se míří. Prázdné = hlavní kamera.")]
        public Transform aim;

        [Tooltip("Dosah v metrech, měřeno OD KAMERY. Musí být větší než výška očí nad zemí, " +
                 "jinak se na zem pod sebou nedá dosáhnout, ať se míří kamkoli.")]
        [Range(1f, 30f)] public float reach = 12f;

        [Tooltip("Poloměr koule v metrech.")]
        [Range(0.5f, 8f)] public float radius = 2f;

        [Tooltip("Kolik ubere jeden úder. 1 = vykope naráz.")]
        [Range(0.05f, 1f)] public float strength = 0.5f;

        [Tooltip("Nejkratší prodleva mezi údery.")]
        [Range(0f, 1f)] public float cooldown = 0.15f;

        [Header("Nástroj")]
        [Tooltip("Kopat a stavět jde jen s krumpáčem v aktivním slotu hotbaru. " +
                 "Vypnutím se vrátí staré chování, kdy to šlo čímkoli.")]
        public bool requirePickaxe = true;

        public KeyCode digKey = KeyCode.Mouse0;
        public KeyCode placeKey = KeyCode.Mouse1;

        [Tooltip("Vrstvy, na kterých leží terén.")]
        public LayerMask terrainMask = ~0;

        private float nextUse;

        /// <summary>
        /// Zkontroluje, jestli je dosah vůbec větší než výška očí nad zemí.
        ///
        /// <para>Vypadá to jako zbytečná kontrola, ale stojí za ní celý zabitý večer: hráč
        /// má v téhle scéně měřítko 2 a kapsli vysokou 8 m, takže mu kamera sedí <b>7,7 m
        /// nad chodidly</b>. S dosahem 6 m paprsek na zem nedosáhl ani při pohledu přímo
        /// pod sebe – a navenek to vypadalo úplně stejně jako rozbitý collider nebo
        /// nefunkční těžba. Číslo, které to prozradí, se nedá uhodnout, musí se změřit.</para>
        /// </summary>
        private void Start()
        {
            Transform t = aim != null ? aim : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return;

            Collider body = GetComponentInParent<Collider>();
            if (body == null) return;

            float eye = t.position.y - body.bounds.min.y;
            if (eye <= reach) return;

            Debug.LogWarning($"[VoxelDigTool] Dosah {reach} m je kratší než výška očí nad zemí " +
                             $"({eye:0.0} m). Na zem pod sebou nedosáhneš, ať míříš kamkoli – " +
                             $"zvyš reach aspoň na {Mathf.Ceil(eye) + 2f:0}.", this);
        }

        /// <summary>
        /// Smí se právě teď kopat? Veřejné, aby se na to mohl zeptat i crosshair nebo
        /// animátor nářadí a nemusel si tu podmínku skládat znovu.
        /// </summary>
        public bool CanUse => (!requirePickaxe || PlayerEquipment.HoldingPickaxe) && !InputBlocked;

        /// <summary>
        /// Vstup patří někomu jinému – pauza, otevřené menu, psaní do konzole nebo
        /// načítání scény. Bez téhle stráže se kope i skrz otevřený inventář.
        /// </summary>
        private static bool InputBlocked
        {
            get
            {
                if (SceneLoader.InputBlocked || GameConsole.IsOpen) return true;

                GameManager gm = GameManager.instance;
                return gm != null && (gm.IsPaused || gm.isMenuOpen);
            }
        }

        private void Update()
        {
            if (Time.time < nextUse) return;
            if (VoxelTerrain.instance == null) return;
            if (!CanUse) return;

            bool dig = Input.GetKey(digKey);
            bool place = !dig && Input.GetKey(placeKey);
            if (!dig && !place) return;

            Transform t = aim != null ? aim : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return;

            if (!Physics.Raycast(t.position, t.forward, out RaycastHit hit, reach, terrainMask,
                                 QueryTriggerInteraction.Ignore)) return;

            // Střed se posune kousek pod povrch (kopání) nebo nad něj (přisypání), aby první
            // úder ubral hmotu a ne jen olízl hranu.
            Vector3 center = hit.point + hit.normal * (dig ? -radius * 0.35f : radius * 0.35f);

            bool changed = dig
                ? VoxelTerrain.instance.Dig(center, radius, strength)
                : VoxelTerrain.instance.Place(center, radius, strength);

            if (changed) nextUse = Time.time + cooldown;
        }
    }
}
