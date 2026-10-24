using Orivilon.Inventory.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon.Player
{
    /// <summary>
    /// Předmět v ruce postavy v004. Sekera a krumpáč používají nástroje z balíku v004 (Tool_Axe/Tool_Pickaxe):
    /// jejich počátek je přesně úchop pravé ruky na kosti prop.R, takže sedí v pěsti ve všech klipech i při chůzi.
    /// Ostatní předměty (jídlo, suroviny) se zmenší do dlaně podle svých mezí. Assety itemů se nemění.
    /// </summary>
    public static class TravelerHeldItem
    {
        /// <summary>Velikost nenástrojového předmětu v dlani (nejdelší strana, jednotky modelu).</summary>
        public const float PalmItemSize = .16f;
        /// <summary>Délka podlouhlého předmětu (meč…) v jednotkách modelu – zhruba jako násada sekery v004.</summary>
        public const float ElongatedSize = .62f;

        public static GameObject Spawn(InventoryItemData item, Transform grip, bool castShadows)
        {
            if (item == null || grip == null) return null;
            GameObject source = null;
            // Prefab vymodelovaný v prostoru úchopu (HeldItemGrip) má přednost i před nástroji v004.
            if (item.handPrefab != null && item.handPrefab.GetComponent<HeldItemGrip>() != null) source = item.handPrefab;
            else if (item.toolType == ToolType.Axe) source = Resources.Load<GameObject>(DefaultPlayerSkin.AxePath);
            else if (item.toolType == ToolType.Pickaxe) source = Resources.Load<GameObject>(DefaultPlayerSkin.PickaxePath);
            GameObject inst;
            if (source != null)
            {
                inst = Object.Instantiate(source, grip, false);
                // Import FBX přidává kořeni převod os (-90° X); vrcholy jsou už v prostoru kosti prop.R.
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
            }
            else
            {
                if (item.handPrefab == null) return null;
                inst = Object.Instantiate(item.handPrefab, grip, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                FitToPalm(inst, grip);
            }
            inst.name = "Held_" + item.id;
            Strip(inst);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (!castShadows) DisableShadowReceive(r);   // varianta pro první osobu
            }
            SetLayerRecursive(inst, grip.gameObject.layer);
            return inst;
        }

        /// <summary>Zmenší libovolný prefab do dlaně: střed mezí kousek nad úchopem, nejdelší strana PalmItemSize.</summary>
        private static void FitToPalm(GameObject inst, Transform grip)
        {
            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds b = default; bool first = true;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var g = grip.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (first) { b = new Bounds(g, Vector3.zero); first = false; } else b.Encapsulate(g);
                }
            }
            if (first) return;
            Vector3 size = b.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            float longest = size[axis];
            if (longest < 1e-5f) return;
            float second = Mathf.Max(axis == 0 ? 0f : size.x, Mathf.Max(axis == 1 ? 0f : size.y, axis == 2 ? 0f : size.z));
            if (longest > second * 2.5f)
            {
                // Podlouhlý předmět (meč, stavěcí nástroj): delší osa do směru násady (+Y prop.R), úchop ve čtvrtině délky.
                Quaternion rot = axis == 0 ? Quaternion.Euler(0f, 0f, 90f) : axis == 2 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
                float s = ElongatedSize / longest;
                Vector3 bottom = b.center; bottom[axis] = b.min[axis] + longest * .22f;
                inst.transform.localRotation = rot;
                inst.transform.localScale = Vector3.one * s;
                inst.transform.localPosition = -(rot * (bottom * s));
            }
            else
            {
                float s = PalmItemSize / longest;
                inst.transform.localScale = Vector3.one * s;
                inst.transform.localPosition = -b.center * s + new Vector3(0f, .03f, 0f);
            }
        }

        /// <summary>Má předmět vůbec model do ruky (nástroj v004 nebo handPrefab)?</summary>
        public static bool HasVisual(InventoryItemData item) =>
            item != null && (item.toolType == ToolType.Axe || item.toolType == ToolType.Pickaxe || item.handPrefab != null);

        private static readonly System.Collections.Generic.Dictionary<Material, Material> noShadow = new System.Collections.Generic.Dictionary<Material, Material>(), noShadow2 = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>
        /// Paže a nástroj z první osoby leží uvnitř stínu vlastního těla (stejné místo). URP se stíny v prostoru
        /// obrazovky ignoruje Renderer.receiveShadows, proto kopie materiálu s vypnutým příjmem stínu (sdílená cache).
        /// </summary>
        public static void DisableShadowReceive(Renderer r, bool doubleSided = false)
        {
            if (r == null) return;
            r.receiveShadows = false;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_ReceiveShadows")) continue;
                var cache = doubleSided ? noShadow2 : noShadow;
                if (!cache.TryGetValue(m, out var copy) || copy == null)
                {
                    copy = new Material(m) { name = m.name + (doubleSided ? " (FP2)" : " (FP)") };
                    copy.SetFloat("_ReceiveShadows", 0f);
                    copy.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                    MakeIgnoreScreenSpaceAO(copy);

                    // Paže končí u lokte: oboustranný materiál, aby řez nebyl průhledná trubka.
                    if (doubleSided && copy.HasProperty("_Cull")) { copy.SetFloat("_Cull", 0f); copy.doubleSidedGI = true; }
                    cache[m] = copy;
                }
                mats[i] = copy;
            }
            r.sharedMaterials = mats;
        }

        /// <summary>
        /// „Vyrážka" na pažích z 1. osoby: overlay kamera paží spouští i SSAO z URP rendereru, jenže v její hloubce
        /// a normálách jsou jen samotné paže. Poloměr AO (0,3 m) je vůči ruce vykreslené u kamery obrovský, takže
        /// shader Lit sáhl do zrnité AO textury a ruka dostala modročerné skvrny (nejvíc v dlani a v ohybu prstů).
        /// Lit nečte SSAO u povrchu typu Transparent (AmbientOcclusion.hlsl: !_SURFACE_TYPE_TRANSPARENT), proto
        /// kopie materiálu první osoby jsou „průhledné" jen formálně: míchání One/Zero a zápis hloubky – vypadají
        /// a třídí se jako neprůhledné. Svět, SSAO světa i materiály těla pro ostatní hráče zůstávají beze změny.
        /// Varianta shaderu se do buildu dostane přes M_FP_ViewModel_Variants (Resources).
        /// </summary>
        public static void MakeIgnoreScreenSpaceAO(Material m)
        {
            if (m == null || !m.HasProperty("_Surface")) return;
            m.SetFloat("_Surface", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            // Bez „preserve specular" (URP by jinak zapnulo _ALPHAPREMULTIPLY_ON) – stejná varianta jako materiál v Resources.
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static void Strip(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Object.Destroy(c); }
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; Object.Destroy(rb); }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
