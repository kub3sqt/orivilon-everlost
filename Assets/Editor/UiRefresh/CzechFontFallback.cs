using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Kolo MP 3: nadpisové písmo <c>vipnagorgialla</c> je statický TMP asset jen s 98 znaky ASCII, ačkoli zdrojové
    /// OTF (Assets/vipnagorgialla.bold.otf) má celou češtinu. Chybějící „á“ v nadpisu dialogu proto TMP kreslil
    /// výchozím fallbackem (LiberationSans) – jiným písmem. Oprava: z TÉHOŽ OTF se se stejnými parametry
    /// (velikost vzorku 59, padding 5, SDFAA, atlas 512) upeče statický fallback s českými a běžnými typografickými
    /// znaky a zařadí se do fallbacků hlavního assetu. Hlavní asset, jeho GUID a znaky zůstávají; nic za běhu
    /// negeneruje atlas (statický režim, nezávislý na zdrojovém fontu v buildu). Idempotentní.
    /// </summary>
    public static class CzechFontFallback
    {
        public const string MainPath = "Assets/TextMesh Pro/Fonts/vipnagorgialla.asset";
        public const string SourcePath = "Assets/vipnagorgialla.bold.otf";
        public const string FallbackPath = "Assets/TextMesh Pro/Fonts/vipnagorgialla CZ.asset";
        public const string Chars = "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽäöüÄÖÜôÔĺľŕĹĽŔ„“”‚‘’…–—°×";

        [MenuItem("Tools/Everlost/UI/Czech glyphs for title font")]
        private static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var main = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (main == null || font == null) return "ERR czfont: chybí " + (main == null ? MainPath : SourcePath);

            var fb = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            bool created = false;
            if (fb == null)
            {
                fb = TMP_FontAsset.CreateFontAsset(font, main.creationSettings.pointSize > 0 ? main.creationSettings.pointSize : 59,
                    main.atlasPadding, GlyphRenderMode.SDFAA, main.atlasWidth, main.atlasHeight, AtlasPopulationMode.Dynamic, false);
                fb.name = "vipnagorgialla CZ";
                AssetDatabase.CreateAsset(fb, FallbackPath);
                fb.atlasTexture.name = fb.name + " Atlas";
                fb.material.name = fb.name + " Material";
                AssetDatabase.AddObjectToAsset(fb.atlasTexture, fb);
                AssetDatabase.AddObjectToAsset(fb.material, fb);
                created = true;
            }
            else fb.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            bool ok = fb.TryAddCharacters(Chars, out string missing);
            // Upečené znaky zůstanou v atlasu; statický režim = žádné generování za běhu ani mazání při buildu.
            fb.atlasPopulationMode = AtlasPopulationMode.Static;
            fb.isMultiAtlasTexturesEnabled = false;
            EditorUtility.SetDirty(fb);
            EditorUtility.SetDirty(fb.atlasTexture);

            bool linked = main.fallbackFontAssetTable != null && main.fallbackFontAssetTable.Contains(fb);
            if (!linked)
            {
                if (main.fallbackFontAssetTable == null) main.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                main.fallbackFontAssetTable.Add(fb);
                EditorUtility.SetDirty(main);
            }
            AssetDatabase.SaveAssets();
            return "czfont " + (created ? "vytvořen" : "aktualizován") + " " + FallbackPath + " znaků " + fb.characterTable.Count
                   + (ok ? "" : " (chybí: " + missing + ")") + ", fallback hlavního písma " + (linked ? "už byl" : "přidán") + " | " + Check();
        }

        /// <summary>Které písmo kreslí každý český znak nadpisů (musí to být rodina vipnagorgialla, ne LiberationSans).</summary>
        public static string Check()
        {
            var main = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainPath);
            if (main == null) return "ERR";
            var sb = new StringBuilder(); int bad = 0;
            foreach (char c in "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ„“…–")
            {
                var ch = TMP_FontAssetUtilities.GetCharacterFromFontAsset(c, main, true, FontStyles.Normal, FontWeight.Regular, out bool _);
                var src = ch != null ? ch.textAsset as TMP_FontAsset : null;
                bool good = ch != null && src != null && src.name.StartsWith("vipnagorgialla");
                if (!good) { bad++; sb.Append(c).Append('→').Append(src != null ? src.name : "nic").Append(' '); }
            }
            return bad == 0 ? "všech 34 znaků z rodiny vipnagorgialla" : "CHYBÍ " + bad + ": " + sb;
        }
    }
}
