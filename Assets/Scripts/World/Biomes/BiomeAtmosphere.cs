using Orivilon.World.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orivilon.World.Biomes
{
    /// <summary>
    /// Atmosférické tónování podle biomu, ve kterém hráč stojí.
    ///
    /// <para><b>Proč to existuje.</b> Poušť a hluboký les měly doteď úplně stejný vzduch –
    /// stejnou barvu mlhy, stejný ambient, stejný grading. Barva terénu se sice mění, ale
    /// atmosféra je to, co dělá rozdíl mezi jinou trávou a jiným místem.</para>
    ///
    /// <para><b>Nesahá na autorský profil.</b> Zakládá si VLASTNÍ globální Volume s vyšší
    /// prioritou a runtime profilem. Projektový <c>URP_GlobalVolumeProfile</c> zůstává
    /// základem a nedotčený – kdyby se do něj psalo za běhu, v editoru by se změny zapsaly
    /// do assetu a zůstaly tam i po vypnutí hry.</para>
    ///
    /// <para><b>Mlhu nepíše sám.</b> Jediným vlastníkem <c>RenderSettings.fog*</c> zůstává
    /// <see cref="SunRotation"/>; odsud jde jen odstín a násobič dosahu přes statická pole.
    /// Dva vlastníci jednoho globálního nastavení už tenhle projekt jednou stáli večer
    /// hledání – mlha se přepisovala každý snímek a nastavení v Lighting nedělalo nic.</para>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BiomeAtmosphere : MonoBehaviour
    {
        public static BiomeAtmosphere Instance { get; private set; }

        /// <summary>Jak často se přepočítá, ve kterém biomu hráč je (sekundy).</summary>
        private const float SampleInterval = 0.3f;

        /// <summary>
        /// Rychlost přechodu na nové hodnoty. Musí být znatelně pomalejší než chůze přes
        /// hranici biomu, jinak je vidět, jak se obraz překlopí.
        /// </summary>
        private const float BlendSpeed = 0.4f;
        public static bool ValleyMistEnabled = true;
        private static readonly int ValleyMistId = Shader.PropertyToID("_EverlostValleyMist");
        private static readonly int ValleyMistColorId = Shader.PropertyToID("_EverlostValleyMistColor");

        /// <summary>Kolo 8: posun saturace a expozice uměleckého průchodu (jen za běhu).</summary>
        /// <para>24.10.2026: obraz byl celý studený – runtime volume přepisoval autorské +8 vyvážení bílé
        /// na −5 (u sněhu až −25) a mlha byla násobená sytou modří, takže obloha, voda i kameny
        /// splývaly do jedné modré. Teplota je teď mírně teplá, biomové odchylky poloviční a opar
        /// světle šedomodrý jako obzor oblohy.</para>
        public static float ArtSaturation = 2f, ArtExposure = -0.05f, ArtWhiteBalance = 2f;
        public static float ArtBiomeWbScale = 0.5f, ArtShadowTintAmount = 0.5f;
        /// <summary>Noc: méně syté barvy (měsíční světlo), jinak tráva a listí v noci svítily tyrkysově.</summary>
        public static float NightSaturation = -24f, NightWhiteBalance = 6f, NightExposure = 0f;
        public static Color ArtFogTint = new Color(0.95f, 0.98f, 1.0f, 1f);
        /// <summary>Kolo 9: horská mlha – modřejší (vzdušná perspektiva), začátek později, o chlup nižší expozice na sněhu.</summary>
        public static Color Art9FogTintMountain = new Color(0.88f, 0.94f, 1.02f, 1f);

        /// <summary>
        /// Údolní mlha (24.10.2026): kapsy podle tvaru terénu (<see cref="ValleyMistField"/>) a nízká vrstva nad mořem.
        /// Hustoty jsou extinkce na metr. Ráno, večer a v noci je mlhy víc než v poledne.
        /// </summary>
        public static float PocketDensity = 0.012f, PocketMaxOpacity = 0.72f, PocketLift = 8f, PocketMaxRay = 2200f;
        public static float SeaMistScale = 0.7f, DayMistScale = 0.65f, MistBrightness = 1.1f, MistWhiten = 0.45f, MistSunScatter = 0.55f;
        /// <summary>Stín trávy se zkracuje mezi těmito vzdálenostmi od kamery (m); konec 0 = bez omezení.</summary>
        public static float GrassShadowStart = 22f, GrassShadowEnd = 34f;
        /// <summary>HDR kamery: bez něj ACES dostává oříznuté hodnoty a bloom s prahem nad 1 nedělá nic.</summary>
        public static bool ForceHdr = true;

        private static readonly int ViewPosId = Shader.PropertyToID("_EverlostViewPos");
        private static readonly int GrassShadowId = Shader.PropertyToID("_EverlostGrassShadow");
        private static readonly int PocketId = Shader.PropertyToID("_EverlostValleyPocket");
        private static readonly int Pocket2Id = Shader.PropertyToID("_EverlostValleyPocket2");
        private static readonly int FloorId = Shader.PropertyToID("_EverlostValleyFloor");
        private static readonly int MistSunId = Shader.PropertyToID("_EverlostMistSun");
        private static readonly int MistSunColorId = Shader.PropertyToID("_EverlostMistSunColor");
        private ValleyMistField mistField;

        /// <summary>Stav mapy údolní mlhy pro diagnostiku (/atmo, visdiag).</summary>
        public string MistInfo => mistField == null ? "mist field: none"
            : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "mist field: ready {0}, origin {1:0},{2:0}, builds {3}, last build {4:0.0} ms (spread over frames)",
                mistField.Ready, mistField.Origin.x, mistField.Origin.y, mistField.Builds, mistField.LastBuildMs);
        public static float Art9FogNear = 1.0f, Art9FogNearMountain = 1.45f, Art9MountainExposure = -0.10f;

        /// <summary>Popis atmosféry jednoho biomu.</summary>
        private struct Grading
        {
            /// <summary>Násobí barvu mlhy z denního cyklu. Bílá = beze změny.</summary>
            public Color fogTint;

            /// <summary>Násobí vzdálenost mlhy. Pod 1 = dusno a blízký horizont.</summary>
            public float fogRange;

            /// <summary>Saturace v jednotkách URP (-100 az 100).</summary>
            public float saturation;

            /// <summary>Expozice v EV.</summary>
            public float exposure;

            /// <summary>Vyvážení bílé: kladné = teplejší.</summary>
            public float whiteBalance;

            /// <summary>Odstín stínů – tohle dělá největší část nálady.</summary>
            public Color shadowTint;

            /// <summary>Kolo 9: jak moc je místo „horské“ (0–1) – řídí hloubku mlhy art passu.</summary>
            public float mountain;

            public static Grading Lerp(Grading a, Grading b, float t) => new Grading
            {
                fogTint = Color.Lerp(a.fogTint, b.fogTint, t),
                fogRange = Mathf.Lerp(a.fogRange, b.fogRange, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                whiteBalance = Mathf.Lerp(a.whiteBalance, b.whiteBalance, t),
                shadowTint = Color.Lerp(a.shadowTint, b.shadowTint, t),
                mountain = Mathf.Lerp(a.mountain, b.mountain, t),
            };
        }

        /// <summary>
        /// Tabulka nálad. Hodnoty jsou schválně drobné – tónování se má poznat až při
        /// přechodu mezi biomy, ne působit jako barevný filtr přes obraz.
        /// </summary>
        private static Grading For(VoxelBiome b)
        {
            switch (b)
            {
                case VoxelBiome.Desert:
                    return new Grading { fogTint = new Color(1.06f, 0.99f, 0.84f), fogRange = 1.35f,
                        saturation = -4f, exposure = 0.12f, whiteBalance = 14f,
                        shadowTint = new Color(0.55f, 0.48f, 0.38f) };
                case VoxelBiome.Savanna:
                    return new Grading { fogTint = new Color(1.04f, 1.00f, 0.89f), fogRange = 1.20f,
                        saturation = 2f, exposure = 0.06f, whiteBalance = 9f,
                        shadowTint = new Color(0.52f, 0.47f, 0.40f) };
                case VoxelBiome.Jungle:
                    return new Grading { fogTint = new Color(0.86f, 1.00f, 0.90f), fogRange = 0.60f,
                        saturation = 14f, exposure = -0.08f, whiteBalance = -6f,
                        shadowTint = new Color(0.32f, 0.46f, 0.38f) };
                case VoxelBiome.Forest:
                    return new Grading { fogTint = new Color(0.93f, 0.99f, 0.95f), fogRange = 0.85f,
                        saturation = 6f, exposure = -0.03f, whiteBalance = -2f,
                        shadowTint = new Color(0.36f, 0.45f, 0.42f) };
                case VoxelBiome.Taiga:
                    return new Grading { fogTint = new Color(0.87f, 0.94f, 1.02f), fogRange = 0.80f,
                        saturation = -6f, exposure = -0.05f, whiteBalance = -14f,
                        shadowTint = new Color(0.33f, 0.41f, 0.52f) };
                case VoxelBiome.Tundra:
                    return new Grading { fogTint = new Color(0.91f, 0.96f, 1.03f), fogRange = 1.10f,
                        saturation = -14f, exposure = 0.06f, whiteBalance = -12f, mountain = 0.5f,
                        shadowTint = new Color(0.40f, 0.45f, 0.55f) };
                case VoxelBiome.Mountains:
                    return new Grading { fogTint = new Color(0.95f, 0.97f, 1.01f), fogRange = 1.25f,
                        saturation = -3f, exposure = 0.05f, whiteBalance = -6f, mountain = 1f,
                        shadowTint = new Color(0.40f, 0.44f, 0.52f) };
                case VoxelBiome.SnowPeaks:
                    return new Grading { fogTint = new Color(0.93f, 0.97f, 1.07f), fogRange = 1.30f,
                        saturation = -10f, exposure = 0.16f, whiteBalance = -20f, mountain = 1f,
                        shadowTint = new Color(0.42f, 0.50f, 0.64f) };
                case VoxelBiome.Beach:
                    return new Grading { fogTint = new Color(1.03f, 1.00f, 0.94f), fogRange = 1.15f,
                        saturation = 4f, exposure = 0.06f, whiteBalance = 7f,
                        shadowTint = new Color(0.48f, 0.49f, 0.46f) };
                case VoxelBiome.Ocean:
                    return new Grading { fogTint = new Color(0.86f, 0.94f, 1.05f), fogRange = 1.10f,
                        saturation = -2f, exposure = 0.02f, whiteBalance = -8f,
                        shadowTint = new Color(0.34f, 0.43f, 0.56f) };
                default:   // Plains – neutrální střed, proti kterému se ostatní poměřují
                    return new Grading { fogTint = Color.white, fogRange = 1f,
                        saturation = 0f, exposure = 0f, whiteBalance = 0f,
                        shadowTint = new Color(0.45f, 0.47f, 0.50f) };
            }
        }

        private Grading current = For(VoxelBiome.Plains);
        private Grading target = For(VoxelBiome.Plains);

        private Volume volume;
        private ColorAdjustments color;
        private WhiteBalance white;
        private ShadowsMidtonesHighlights shadows;

        private float nextSample;
        private Transform viewer;

        /// <summary>
        /// Kolo 30: výhled z výšky. Opar je hustší dole – z údolí zůstává blízký horizont, z hřebene
        /// je vidět dál. Rozhoduje nadmořská výška oka a jeho výška nad okolím (průměr terénu
        /// v kruhu <see cref="VistaRingRadius"/>, moře = hladina), takže výškové údolí uvnitř
        /// masivu zůstane zamlžené víc než vrchol ve stejné výšce.
        /// </summary>
        public static float VistaAltLow = 70f, VistaAltHigh = 450f, VistaAltWeight = 0.65f;
        /// <inheritdoc cref="VistaAltLow"/>
        public static float VistaRelLow = 15f, VistaRelHigh = 160f, VistaRelWeight = 0.6f;
        private const float VistaRingRadius = 250f;
        /// <summary>Rychlost náběhu výhledu (1/s). Pomalá – při stoupání se opar rozplývá postupně, ne skokem.</summary>
        private const float VistaBlendSpeed = 0.5f;
        private float vistaTarget, vista, vistaAlt, vistaRel;

        /// <summary>Biom, podle kterého se právě tónuje. Čte ho konzolový příkaz /atmo.</summary>
        public VoxelBiome ActiveBiome { get; private set; } = VoxelBiome.Plains;

        /// <summary>Mikro-biom pod hráčem, pokud nějaký je.</summary>
        public MicroBiome ActiveMicro { get; private set; } = MicroBiome.None;

        /// <summary>Je kamera pod hladinou?</summary>
        public bool Submerged { get; private set; }

        /// <summary>Hladina, pod kterou se hráč ponořil. Platné jen při <see cref="Submerged"/>.</summary>
        private float waterLine;

        /// <summary>
        /// Jak hluboko musí kamera klesnout pod hladinu, než se to počítá za ponoření –
        /// a o kolik výš musí vystoupat, aby se vynořila.
        ///
        /// <para>Hystereze tu není kvůli eleganci. Hladina se ve vlnách houpe o víc než
        /// deset centimetrů a kamera s hráčem taky; s jedním prahem by na rozhraní přeskakoval
        /// celý grading i mlha několikrát za vteřinu.</para>
        /// </summary>
        private const float SubmergeDepth = 0.18f;

        /// <summary>Jak rychle se přechází na podvodní stav a zpět. Vyšší = tvrdší.</summary>
        private const float DiveBlendSpeed = 3.5f;

        /// <summary>Míra ponoření 0–1. Náběh je oddělený od biomového blendu, protože je rychlý.</summary>
        private float dive;

        /// <summary>
        /// Nálada mikro-biomu. Je schválně silnější než u klimatických biomů – tady
        /// nejde o nádech kraje, ale o to, aby hráč poznal, že stojí někde jinde.
        /// </summary>
        private static Grading ForMicro(MicroBiome m)
        {
            if (m == MicroBiome.GoldenGrove)
                return new Grading { fogTint = new Color(1.08f, 0.99f, 0.80f), fogRange = 0.80f,
                                     saturation = 9f, exposure = 0.07f, whiteBalance = 16f,
                                     shadowTint = new Color(0.50f, 0.43f, 0.30f) };

            // Čedič: studeno, odsycené, a ve stínech fialový nádech. Právě ten fialový
            // posun dělá z černé skály kámen – čistě neutrální černá vypadá na
            // nízkopolygonovém modelu jako chybějící materiál.
            return new Grading { fogTint = new Color(0.87f, 0.88f, 0.99f), fogRange = 1.05f,
                                 saturation = -12f, exposure = -0.05f, whiteBalance = -12f,
                                 shadowTint = new Color(0.34f, 0.31f, 0.45f) };
        }

        /// <summary>
        /// Podvodní grading. Není to biom – je to jiný živel, takže hodnoty jsou řádově
        /// silnější než u všeho ostatního: barvy se skoro vytratí, expozice spadne a
        /// všechno se posune do modrozelené.
        /// </summary>
        private static Grading Underwater() => new Grading
        {
            fogTint = new Color(0.10f, 0.42f, 0.44f),
            fogRange = 1f,
            saturation = -30f,

            // Expozice a tlumení světla níž by byly „realističtější", ale hráč pak nevidí,
            // kam plave. Osmnáct metrů pod hladinou má být šero, ne slepota – hloubkové
            // ztmavení stejně dělá mlha, která se do dvaadvaceti metrů zavře úplně.
            exposure = -0.26f,
            whiteBalance = -26f,
            shadowTint = new Color(0.16f, 0.34f, 0.44f),
        };

        /// <summary>Barva a dosah podvodní mlhy. Dohled 22 m – dál se ztrácí i velký balvan.</summary>
        private static readonly Color UnderwaterFog = new Color(0.055f, 0.20f, 0.24f);
        private const float UnderwaterFogNear = 0.5f;
        private const float UnderwaterFogFar = 22f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Biome Atmosphere") { hideFlags = HideFlags.DontSave };
            go.AddComponent<BiomeAtmosphere>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildVolume();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            SunRotation.BiomeFogTint = Color.white;
            SunRotation.BiomeFogRange = 1f;
            SunRotation.FogOverride = false;
            SunRotation.LightDamp = 1f;
            SunRotation.VistaFactor = 0f;
            Shader.SetGlobalVector(ValleyMistId, Vector4.zero);
            Shader.SetGlobalVector(Pocket2Id, Vector4.zero);
            Shader.SetGlobalVector(GrassShadowId, Vector4.zero);
            mistField?.Release();
            mistField = null;
            Instance = null;
        }

        /// <summary>
        /// Postaví vlastní globální Volume s runtime profilem. Priorita 100 znamená,
        /// že se skládá NAD projektový profil, který zůstává základem.
        /// </summary>
        private void BuildVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Biome Atmosphere (runtime)";
            profile.hideFlags = HideFlags.DontSave;

            color = profile.Add<ColorAdjustments>(true);
            color.saturation.overrideState = true;
            color.postExposure.overrideState = true;

            white = profile.Add<WhiteBalance>(true);
            white.temperature.overrideState = true;

            shadows = profile.Add<ShadowsMidtonesHighlights>(true);
            shadows.shadows.overrideState = true;

            // Soft highlights without washing out the low-poly color planes.
            var bloom = profile.Add<Bloom>(false);
            bloom.intensity.Override(0.25f);
            bloom.threshold.Override(1.1f);
            bloom.highQualityFiltering.Override(true);

            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 1f;
            volume.profile = profile;
        }

        private void Update()
        {
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + SampleInterval;
                Resample();
            }

            // Ponoření se testuje KAŽDÝ snímek, na rozdíl od biomu. Skok pod hladinu trvá
            // zlomek vteřiny a zpoždění tří desetin by bylo vidět jako díra v efektu.
            UpdateSubmersion();

            Grading want = Submerged || dive > 0.001f
                ? Grading.Lerp(target, Underwater(), dive)
                : target;

            float speed = dive > 0.001f ? DiveBlendSpeed : BlendSpeed;
            current = Grading.Lerp(current, want, 1f - Mathf.Exp(-speed * Time.deltaTime));
            vista = Mathf.Lerp(vista, vistaTarget, 1f - Mathf.Exp(-VistaBlendSpeed * Time.deltaTime));
            SunRotation.VistaFactor = vista;
            Apply();
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                if (ForceHdr && !cam.allowHDR) cam.allowHDR = true;
                Shader.SetGlobalVector(ViewPosId, cam.transform.position);
            }
            Shader.SetGlobalVector(GrassShadowId, new Vector4(GrassShadowStart, GrassShadowEnd, 0f, 0f));

            var terrain = VoxelTerrain.instance;
            if (terrain == null || !RenderSettings.fog || !ValleyMistEnabled || cam == null)
            {
                Shader.SetGlobalVector(ValleyMistId, Vector4.zero);
                Shader.SetGlobalVector(Pocket2Id, Vector4.zero);
                return;
            }

            // World anchored: moving the camera never lifts the fog out of a valley.
            // Existing distance fog and underwater fog retain their own ownership.
            float night = Mathf.Clamp01(SunRotation.nightBlendStatic);
            float twilight = 1f - Mathf.Abs(night * 2f - 1f);
            float timeScale = Mathf.Lerp(DayMistScale, 1f, Mathf.Max(night, twilight)) * (1f - dive);
            float density = Mathf.Lerp(0.0014f, 0.0021f, current.mountain) * SeaMistScale * timeScale;
            Shader.SetGlobalVector(ValleyMistId, new Vector4(terrain.SeaLevel + 12f, 18f, density, 0.48f));

            if (mistField == null) mistField = new ValleyMistField();
            mistField.Tick(terrain, cam.transform.position);
            Shader.SetGlobalTexture(FloorId, mistField.Texture);
            Shader.SetGlobalVector(PocketId, new Vector4(PocketDensity * timeScale, PocketMaxOpacity, mistField.Origin.x, mistField.Origin.y));
            Shader.SetGlobalVector(Pocket2Id, new Vector4(1f / mistField.Span, PocketLift, PocketMaxRay, mistField.Ready ? 1f : 0f));

            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            // Mlha je bělejší a světlejší než dálkový opar (kapky rozptylují všechny barvy stejně).
            Color fogC = RenderSettings.fogColor;
            float lum = fogC.r * 0.2126f + fogC.g * 0.7152f + fogC.b * 0.0722f;
            Color tint = Color.Lerp(fogC, new Color(lum, lum, lum), MistWhiten) * MistBrightness;
            tint.a = 1f;
            if (linear) tint = tint.linear;
            Shader.SetGlobalColor(ValleyMistColorId, tint);

            // Rozptyl vpřed: mlha proti nízkému slunci teple svítí. Jen když je slunce nad obzorem.
            Vector3 toSun = -SunRotation.SkySunForward;
            Light sun = RenderSettings.sun;
            Color sunCol = sun != null ? sun.color * Mathf.Min(sun.intensity, 1.5f) : Color.white;
            sunCol = Color.Lerp(RenderSettings.fogColor, sunCol, 0.75f);
            sunCol.a = 1f;
            if (linear) sunCol = sunCol.linear;
            float sunUp = Mathf.Clamp01(toSun.y * 6f + 0.3f) * (1f - night);
            Shader.SetGlobalVector(MistSunId, new Vector4(toSun.x, toSun.y, toSun.z, MistSunScatter * sunUp));
            Shader.SetGlobalColor(MistSunColorId, sunCol);
        }

        /// <summary>
        /// Je kamera pod hladinou? Ptáme se na pozici KAMERY, ne hráče – pod vodou je hlava
        /// a nohy na různých stranách rozhraní a rozhoduje to, čím se dívá.
        /// </summary>
        private void UpdateSubmersion()
        {
            VoxelTerrain t = VoxelTerrain.instance;
            Camera cam = Camera.main;

            if (t == null || cam == null)
            {
                Submerged = false;
            }
            else
            {
                Vector3 eye = cam.transform.position;

                if (t.WaterLevelAt(eye.x, eye.z, out float level))
                {
                    // Dva prahy: ponořit se musí hlouběji, než stačí k vynoření.
                    float threshold = Submerged ? level + SubmergeDepth : level - SubmergeDepth;
                    Submerged = eye.y < threshold;
                    waterLine = level;
                }
                else
                {
                    Submerged = false;
                }
            }

            dive = Mathf.MoveTowards(dive, Submerged ? 1f : 0f, Time.deltaTime * DiveBlendSpeed);
        }

        /// <summary>Zjistí biom pod hráčem. Analytický dotaz, žádný chunk ani collider.</summary>
        private void Resample()
        {
            VoxelTerrain t = VoxelTerrain.instance;
            if (t == null) return;

            if (viewer == null)
            {
                viewer = t.viewer != null ? t.viewer : (Camera.main != null ? Camera.main.transform : null);
                if (viewer == null) return;
            }

            Vector3 p = viewer.position;
            ActiveBiome = t.BiomeAt(p.x, p.z, out _);
            ResampleVista(t, p);
            target = For(ActiveBiome);

            // Mikro-biom se do gradingu nepřidává, ale MÍCHÁ podle své síly. Na okraji
            // skvrny se tím atmosféra mění postupně spolu se vzhledem terénu, ne skokem
            // na hranici – a uvnitř klima přebije, protože to je celý smysl toho místa.
            ActiveMicro = t.MicroAt(p.x, p.z, out float mw);
            if (ActiveMicro != MicroBiome.None && mw > 0.01f)
                target = Grading.Lerp(target, ForMicro(ActiveMicro), Mathf.Clamp01(mw));
        }

        /// <summary>Kolo 30: cílový výhled z oka kamery (8 analytických dotazů na výšku za 0,3 s, bez chunků a colliderů).</summary>
        private void ResampleVista(VoxelTerrain t, Vector3 p)
        {
            Camera cam = Camera.main;
            if (cam != null) p = cam.transform.position;
            float sea = t.SeaLevel, sum = 0f;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI * 0.25f;
                sum += Mathf.Max(sea, t.SurfaceHeight(p.x + Mathf.Sin(ang) * VistaRingRadius, p.z + Mathf.Cos(ang) * VistaRingRadius));
            }
            vistaAlt = p.y - sea;
            vistaRel = p.y - sum / 8f;
            vistaTarget = Mathf.Clamp01(VistaAltWeight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(VistaAltLow, VistaAltHigh, vistaAlt))
                                      + VistaRelWeight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(VistaRelLow, VistaRelHigh, vistaRel)));
        }

        private void Apply()
        {
            // Mlha jde přes SunRotation – ta je jediný vlastník RenderSettings.fog*.
            // Kolo 8: mlha o kus modřejší – bledý pás mlhy proti syté obloze dělal na obzoru
            // bílou zeď, za kterou se ztrácely hory i moře.
            bool art = Orivilon.World.Generation.WorldGenSettings.ArtPassEnabled;
            bool art9 = art && Orivilon.World.Generation.WorldGenSettings.Art9Enabled;
            Color fogArt = art9 ? Color.Lerp(ArtFogTint, Art9FogTintMountain, current.mountain) : ArtFogTint;
            SunRotation.BiomeFogTint = art ? current.fogTint * fogArt : current.fogTint;
            // Kolo 9: v horách začíná mlha později (hřebeny ve středním plánu čitelné), konec
            // dohledu zůstává schovaný stejně jako dřív. Mimo hory jen nepatrně.
            SunRotation.BiomeFogNearScale = art9 ? Mathf.Lerp(Art9FogNear, Art9FogNearMountain, current.mountain) : 1f;
            SunRotation.BiomeFogRange = current.fogRange;

            // Pod vodou se mlha nepřibarvuje, ale přebírá: z dohledu na kilometry se stane
            // dvacet metrů. Náběh jde přes vzdálenost, ne přes zapnutí – skok z 1800 m na
            // 22 m během jednoho snímku vypadá jako porucha zobrazení.
            if (dive > 0.001f)
            {
                ResolveDayFog(out float dayNear, out float dayFar);

                SunRotation.FogOverride = true;
                SunRotation.FogOverrideColor = Color.Lerp(RenderSettings.fogColor, UnderwaterFog, dive);
                SunRotation.FogOverrideNear = Mathf.Lerp(dayNear, UnderwaterFogNear, dive);
                SunRotation.FogOverrideFar = Mathf.Lerp(dayFar, UnderwaterFogFar, dive);
                SunRotation.LightDamp = Mathf.Lerp(1f, 0.34f, dive);
            }
            else if (SunRotation.FogOverride)
            {
                SunRotation.FogOverride = false;
                SunRotation.LightDamp = 1f;
            }

            if (color != null)
            {
                // Kolo 8: umělecký průchod – o kus sytější a o chlup tmavší obraz. Globální
                // profil má ACES a teplé světlé tóny, které osluněnou trávu a písek slévaly
                // do stejné bledě béžové; biomy se pak nedaly rozeznat.
                float night = Mathf.Clamp01(SunRotation.nightBlendStatic);
                color.saturation.value = current.saturation + (art ? ArtSaturation + NightSaturation * night : 0f);
                color.postExposure.value = current.exposure + (art ? ArtExposure + NightExposure * night : 0f)
                    + (art9 ? Art9MountainExposure * current.mountain : 0f);
            }

            if (white != null) white.temperature.value = art
                ? current.whiteBalance * ArtBiomeWbScale + ArtWhiteBalance + NightWhiteBalance * Mathf.Clamp01(SunRotation.nightBlendStatic)
                : current.whiteBalance;

            if (shadows != null)
            {
                // ShadowsMidtonesHighlights bere barvu jako Vector4 (rgb + expozice).
                float k = art ? ArtShadowTintAmount : 1f;
                shadows.shadows.value = new Vector4(Mathf.Lerp(1f, current.shadowTint.r * 2f, k),
                                                    Mathf.Lerp(1f, current.shadowTint.g * 2f, k),
                                                    Mathf.Lerp(1f, current.shadowTint.b * 2f, k), 0f);
            }
        }

        /// <summary>
        /// Dohled mlhy, jaký by platil nad vodou. Bere se z toho, co je právě nastavené –
        /// SunRotation to spočítala ve svém Update ještě před námi, takže se nemusí
        /// duplikovat vzorec z dohledu terénu.
        /// </summary>
        private static void ResolveDayFog(out float near, out float far)
        {
            near = RenderSettings.fogStartDistance;
            far = RenderSettings.fogEndDistance;
        }

        /// <summary>Popis aktuálního stavu pro konzoli.</summary>
        public string Describe()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: fog tint {1:0.00}/{2:0.00}/{3:0.00}, fog range {4:0.00}x, " +
                "saturation {5:0.0}, exposure {6:0.00}, white balance {7:0.0}",
                VoxelBiomes.Name(ActiveBiome) +
                (ActiveMicro != MicroBiome.None ? " + " + ActiveMicro : "") +
                (dive > 0.01f ? string.Format(" + underwater {0:0}% (line {1:0.0} m)", dive * 100f, waterLine) : ""),
                current.fogTint.r, current.fogTint.g, current.fogTint.b,
                current.fogRange, current.saturation, current.exposure, current.whiteBalance)
                + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    ", vyhled {0:0.00} (cil {1:0.00}, vyska {2:0} m, nad okolim {3:0} m, {4}), mlha {5:0}-{6:0} m",
                    vista, vistaTarget, vistaAlt, vistaRel, SunRotation.VistaEnabled ? "ZAP" : "VYP",
                    RenderSettings.fogStartDistance, RenderSettings.fogEndDistance);
        }
    }
}
