using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Editor-only helpers for authoring the refreshed UI as saved assets: fonts, generated
    /// sprite PNGs, stencil materials, object creation and responsive placement.
    /// Nothing here runs in the game.
    /// </summary>
    public static class UiAuthoringKit
    {
        public const string Root = "Assets/UI/Refresh";
        public const string SpriteDir = Root + "/Sprites";
        public const string MaterialDir = Root + "/Materials";
        public const string PrefabDir = Root + "/Prefabs";
        public const string StylePath = Root + "/FrameStyle.asset";

        public static readonly Color Ink = new Color32(0xe9, 0xec, 0xe6, 0xff);
        public static readonly Color Muted = new Color32(0xa8, 0xb0, 0xa6, 0xff);

        // ── fonts ────────────────────────────────────────────────────────────

        private static TMP_FontAsset mono, monoRegular, heading;

        public static TMP_FontAsset MonoBold => mono != null ? mono : (mono = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/SpaceMono-Bold SDF.asset"));
        public static TMP_FontAsset MonoRegular => monoRegular != null ? monoRegular : (monoRegular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/SpaceMono-Regular SDF.asset"));

        public static TMP_FontAsset Heading
        {
            get
            {
                if (heading != null) return heading;
                foreach (var g in AssetDatabase.FindAssets("t:TMP_FontAsset"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (System.IO.Path.GetFileName(p).ToLowerInvariant().Contains("vipnagorgialla")) { heading = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p); break; }
                }
                return heading;
            }
        }

        public static void SetMono(TMP_Text t, bool bold = true, float size = -1f)
        {
            if (t == null) return;
            var f = bold ? MonoBold : MonoRegular;
            if (f == null) throw new System.Exception("Space Mono font asset missing");
            t.font = f; t.fontSharedMaterial = f.material;
            t.fontStyle &= ~FontStyles.Bold;
            if (size > 0f) t.fontSize = size;
            EditorUtility.SetDirty(t);
        }

        public static void SetHeading(TMP_Text t, float size = -1f)
        {
            if (t == null) return;
            if (Heading == null) throw new System.Exception("Heading font (vipnagorgialla) missing");
            t.font = Heading; t.fontSharedMaterial = Heading.material;
            if (size > 0f) t.fontSize = size;
            EditorUtility.SetDirty(t);
        }

        public static bool IsHeading(TMP_Text t) => t != null && t.font != null && t.font.name.ToLowerInvariant().Contains("vipnagorgialla");

        // ── objects ──────────────────────────────────────────────────────────

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image NewImage(string name, Transform parent, Sprite sprite, Color color, bool sliced, bool raycast = false)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
            if (sliced) img.type = Image.Type.Sliced;
            return img;
        }

        public static TextMeshProUGUI NewText(string name, Transform parent, string text, bool isHeading, float size, Color color, TextAlignmentOptions align, bool bold = true)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (isHeading) SetHeading(t, size); else SetMono(t, bold, size);
            t.text = text; t.color = color; t.alignment = align;
            t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        public static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        // ── responsive placement ─────────────────────────────────────────────
        //
        // A coordinate is (k, c): value = k * canvasSize + c, measured from the canvas'
        // bottom-left (reference px). L(c) = from left/bottom, R(c) = from right/top,
        // M(c) = from the centre. Place() converts such a rectangle into real anchors and
        // offsets relative to the element's actual parent (whatever its own anchors, pivot and
        // scale are) by evaluating the parent chain at two virtual canvas sizes.

        public struct C { public float k, c; public C(float k, float c) { this.k = k; this.c = c; } public float At(float size) => k * size + c; }
        public static C L(float c) => new C(0f, c);
        public static C R(float c) => new C(1f, c);
        public static C M(float c) => new C(0.5f, c);
        public static C K(float k, float c) => new C(k, c);

        public static readonly Vector2 SizeA = new Vector2(1920f, 1080f), SizeB = new Vector2(2000f, 1180f);

        /// <summary>Canvas-space rect (origin bottom-left) of a transform for a virtual canvas size.
        /// Returns the rect and the canvas units per local unit.</summary>
        public static Rect Virtual(Transform t, Vector2 canvasSize, out Vector2 unit)
        {
            var cv = t.GetComponent<Canvas>();
            if (cv != null && cv.isRootCanvas) { unit = Vector2.one; return new Rect(0f, 0f, canvasSize.x, canvasSize.y); }
            if (t.parent == null) { unit = Vector2.one; return new Rect(0f, 0f, canvasSize.x, canvasSize.y); }
            Rect P = Virtual(t.parent, canvasSize, out Vector2 pu);
            var prt = t.parent as RectTransform;
            var rt = t as RectTransform;
            Vector2 sc = new Vector2(t.localScale.x, t.localScale.y);
            if (rt == null || prt == null)
            {
                // plain Transform (or child of one): a zero-size point at its local position from the parent's pivot
                Vector2 origin = prt != null ? P.min + Vector2.Scale(prt.pivot, P.size) : P.center;
                if (rt == null)
                {
                    Vector2 o = origin + Vector2.Scale((Vector2)t.localPosition, pu);
                    unit = Vector2.Scale(pu, sc);
                    return new Rect(o, Vector2.zero);
                }
                // RectTransform under a plain Transform: anchors are ignored, rect around localPosition
                Vector2 ctr = origin + Vector2.Scale((Vector2)t.localPosition, pu);
                unit = Vector2.Scale(pu, sc);
                Vector2 size = Vector2.Scale(rt.sizeDelta, unit);
                return new Rect(ctr - Vector2.Scale(rt.pivot, size), size);
            }
            Vector2 c0 = P.min + Vector2.Scale(rt.anchorMin, P.size) + Vector2.Scale(rt.offsetMin, pu);
            Vector2 c1 = P.min + Vector2.Scale(rt.anchorMax, P.size) + Vector2.Scale(rt.offsetMax, pu);
            var mwf = t.GetComponent<MaxWidthFitter>();
            if (mwf != null)
            {
                // treat the fitter as plain stretch (both virtual sizes stay below its cap)
                c0.x = P.xMin + mwf.left * pu.x; c1.x = P.xMax - mwf.right * pu.x;
            }
            Vector2 pv = c0 + Vector2.Scale(rt.pivot, c1 - c0);
            Vector2 d0 = pv + Vector2.Scale(c0 - pv, sc), d1 = pv + Vector2.Scale(c1 - pv, sc);
            unit = Vector2.Scale(pu, sc);
            return Rect.MinMaxRect(Mathf.Min(d0.x, d1.x), Mathf.Min(d0.y, d1.y), Mathf.Max(d0.x, d1.x), Mathf.Max(d0.y, d1.y));
        }

        /// <summary>Place a RectTransform on a responsive canvas rectangle (see class comment).
        /// The pivot is kept unless one is given.</summary>
        public static void Place(RectTransform rt, C x0, C y0, C x1, C y1, Vector2? pivot = null)
        {
            if (rt == null) return;
            if (pivot.HasValue) rt.pivot = pivot.Value;
            var parent = rt.parent;
            var prt = parent as RectTransform;
            Rect PA = Virtual(parent, SizeA, out Vector2 uA), PB = Virtual(parent, SizeB, out Vector2 uB);
            Vector2 sc = new Vector2(rt.localScale.x, rt.localScale.y);
            Rect TA = Rect.MinMaxRect(x0.At(SizeA.x), y0.At(SizeA.y), x1.At(SizeA.x), y1.At(SizeA.y));
            Rect TB = Rect.MinMaxRect(x0.At(SizeB.x), y0.At(SizeB.y), x1.At(SizeB.x), y1.At(SizeB.y));
            // displayed rect → unscaled rect (scale is applied around the pivot)
            Rect UA = Unscale(TA, rt.pivot, sc), UB = Unscale(TB, rt.pivot, sc);
            if (prt == null)
            {
                // under a plain Transform anchors are ignored: size + local position only
                rt.sizeDelta = new Vector2(UA.width / uA.x, UA.height / uA.y);
                Vector2 piv = UA.min + Vector2.Scale(rt.pivot, UA.size);
                Vector2 origin = PA.center;   // zero-size point
                rt.localPosition = new Vector3((piv.x - origin.x) / uA.x, (piv.y - origin.y) / uA.y, rt.localPosition.z);
                if (!Mathf.Approximately(TA.x - PA.x, TB.x - PB.x) || !Mathf.Approximately(TA.y - PA.y, TB.y - PB.y))
                    Debug.LogWarning("[UiAuthoring] " + PathOf(rt) + " sits under a plain Transform and cannot follow screen size.");
                return;
            }
            Vector2 aMin, aMax, oMin, oMax;
            Solve(UA.xMin, UB.xMin, PA.xMin, PB.xMin, PA.width, PB.width, uA.x, rt.anchorMin.x, out aMin.x, out oMin.x, rt, "xMin");
            Solve(UA.xMax, UB.xMax, PA.xMin, PB.xMin, PA.width, PB.width, uA.x, rt.anchorMax.x, out aMax.x, out oMax.x, rt, "xMax");
            Solve(UA.yMin, UB.yMin, PA.yMin, PB.yMin, PA.height, PB.height, uA.y, rt.anchorMin.y, out aMin.y, out oMin.y, rt, "yMin");
            Solve(UA.yMax, UB.yMax, PA.yMin, PB.yMin, PA.height, PB.height, uA.y, rt.anchorMax.y, out aMax.y, out oMax.y, rt, "yMax");
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var tmp = rt.GetComponent<TMP_Text>();
            if (tmp != null) { tmp.margin = Vector4.zero; EditorUtility.SetDirty(tmp); }
            EditorUtility.SetDirty(rt);
        }

        private static Rect Unscale(Rect d, Vector2 pivot, Vector2 sc)
        {
            Vector2 pv = d.min + Vector2.Scale(pivot, d.size);
            Vector2 size = new Vector2(d.width / Mathf.Abs(sc.x), d.height / Mathf.Abs(sc.y));
            Vector2 mn = pv - Vector2.Scale(pivot, size);
            return new Rect(mn, size);
        }

        private static void Solve(float cA, float cB, float pminA, float pminB, float psA, float psB, float unit, float current, out float anchor, out float offset, Transform t, string what)
        {
            float relA = cA - pminA, relB = cB - pminB;
            if (Mathf.Abs(psB - psA) > 1e-3f)
            {
                anchor = (relB - relA) / (psB - psA);
                anchor = Mathf.Round(anchor * 1000f) / 1000f;
            }
            else
            {
                anchor = current;
                if (Mathf.Abs(relB - relA) > 0.05f) Debug.LogWarning("[UiAuthoring] " + PathOf(t) + " " + what + ": parent has a fixed size, element cannot follow screen size.");
            }
            offset = (relA - anchor * psA) / unit;
        }

        public static string PathOf(Transform t) => t == null ? "" : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name);

        /// <summary>Canvas-space rect at SizeA of an existing element (for measuring).</summary>
        public static Rect CanvasRectA(Transform t) => Virtual(t, SizeA, out _);

        // ── sprite & material assets ─────────────────────────────────────────

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>Writes a texture as PNG and imports it as a (sliced) sprite asset.</summary>
        public static Sprite SaveSprite(string name, Texture2D tex, Vector4 border, float ppu = 100f, bool mipmaps = false)
        {
            EnsureFolder(SpriteDir);
            string path = SpriteDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = ppu;
            imp.mipmapEnabled = mipmaps;
            imp.alphaIsTransparency = true;
            imp.sRGBTexture = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.maxTextureSize = 2048;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteBorder = border;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        /// <summary>Rounded rectangle with a vertical gradient and an inner border (same art as the
        /// approved revision-3 runtime sprites).</summary>
        public static Sprite Rounded(string name, float radius, Color top, Color bottom, float border, Color borderColor)
        {
            int size = Mathf.CeilToInt(radius * 2f + 8f);
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (half - radius), Mathf.Abs(p.y) - (half - radius));
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d);
                Color c = Color.Lerp(bottom, top, (y + 0.5f) / size);
                if (border > 0f)
                {
                    float b = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + border);
                    c = Color.Lerp(c, borderColor, b * borderColor.a);
                }
                c.a *= a;
                px[y * size + x] = c;
            }
            tex.SetPixels(px); tex.Apply(false, false);
            float bd = Mathf.Ceil(radius + 2f);
            return SaveSprite(name, tex, new Vector4(bd, bd, bd, bd));
        }

        /// <summary>White strokes with round caps (chevrons, cross) – 4× supersampled.</summary>
        public static Sprite Lines(string name, int w, int h, float width, params Vector4[] segs)
        {
            int s = 4;
            var tex = NewTex(w * s, h * s);
            var px = new Color[w * s * h * s];
            for (int y = 0; y < h * s; y++)
            for (int x = 0; x < w * s; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / s, h - (y + 0.5f) / s);
                float dmin = 1e9f;
                foreach (var g in segs)
                {
                    Vector2 a = new Vector2(g.x, g.y), b = new Vector2(g.z, g.w), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    dmin = Mathf.Min(dmin, (p - (a + ab * t)).magnitude);
                }
                px[y * w * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((width * 0.5f - dmin) * s + 0.5f));
            }
            tex.SetPixels(px); tex.Apply(false, false);
            return SaveSprite(name, tex, Vector4.zero, 100f * s, true);
        }

        /// <summary>The Arcanum / F3 panel art (black, thin grey inner edge, soft outer glow).</summary>
        public static Sprite ArcanumRounded(string name, int size, float radius, float stroke, bool glow)
        {
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float pad = glow ? 4f : 0f;
            float half = size * 0.5f - pad;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (half - radius), Mathf.Abs(p.y) - (half - radius));
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float a;
                byte g = 255;
                if (stroke > 0f) a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + stroke);
                else
                {
                    a = Mathf.Clamp01(0.5f - d);
                    if (glow)
                    {
                        float inner = Mathf.Clamp01(1f - Mathf.Abs(d + 1f) / 1.5f) * 0.55f;
                        float outer = d > 0f ? Mathf.Clamp01(1f - d / pad) * 0.45f : 0f;
                        a = Mathf.Max(a, outer);
                        float lum = Mathf.Max(inner, d > 0f ? 1f : 0f);
                        g = (byte)(255 * Mathf.Clamp01(lum * 0.62f));
                        px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
                        continue;
                    }
                }
                px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply(false, false);
            float b = Mathf.Ceil(radius + pad + 2f);
            return SaveSprite(name, tex, new Vector4(b, b, b, b));
        }

        public static Material StencilMaterial(string name, CompareFunction comp, int reference, StencilOp op, int readMask, int writeMask, bool colorWrite, bool alphaClip)
        {
            EnsureFolder(MaterialDir);
            string path = MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("UI/Default")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_StencilComp", (float)comp);
            m.SetFloat("_Stencil", reference);
            m.SetFloat("_StencilOp", (float)op);
            m.SetFloat("_StencilReadMask", readMask);
            m.SetFloat("_StencilWriteMask", writeMask);
            m.SetFloat("_ColorMask", colorWrite ? 15f : 0f);
            if (alphaClip) { m.EnableKeyword("UNITY_UI_ALPHACLIP"); m.SetFloat("_UseUIAlphaClip", 1f); }
            else { m.DisableKeyword("UNITY_UI_ALPHACLIP"); m.SetFloat("_UseUIAlphaClip", 0f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void ForceLayout(RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        public static RectTransform FirstActiveChild(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return t.GetChild(i) as RectTransform;
            return null;
        }

        public static List<T> All<T>(Transform root) where T : Component => new List<T>(root.GetComponentsInChildren<T>(true));
    }
}
