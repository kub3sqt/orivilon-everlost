using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.World.Biomes
{
    /// <summary>
    /// Částice v okolí hráče podle mikro-biomu: padající listí ve zlatém háji,
    /// vznášející se popílek nad čedičovým polem.
    ///
    /// <para><b>Proč vlastní komponenta a ne prefab ve scéně.</b> Efekt nepatří žádnému
    /// místu ve světě – putuje s hráčem a zapíná se podle toho, kde stojí. Prefab ve scéně
    /// by znamenal další objekt, který někdo musí ručně nastavit a který se dá omylem
    /// smazat nebo vypnout; takhle se systém postaví z kódu a nemá se co rozejít s tím,
    /// co čeká <see cref="BiomeAtmosphere"/>.</para>
    ///
    /// <para><b>Emitor jde s hráčem, simulace zůstává ve světě.</b> To je celý trik: kdyby
    /// se simulovalo v lokálním prostoru, letěly by částice s kamerou jako sníh za oknem
    /// vlaku a hráč by okamžitě poznal, že jsou nalepené na něm. Takhle emitor jen
    /// přesazuje místo vzniku, ale samotné listí padá v pevném světě.</para>
    ///
    /// <para><b>Jeden systém, dvě nálady.</b> Dva systémy vedle sebe by znamenaly dvojí
    /// materiál i dvojí draw call kvůli efektu, který nikdy neběží současně – háj a čedič
    /// se nepotkají. Přepínají se proto moduly jednoho systému a při změně se staré
    /// částice nechají dožít, aby listí nezmizelo skokem na hranici.</para>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class AmbientParticles : MonoBehaviour
    {
        public static AmbientParticles Instance { get; private set; }

        /// <summary>Jak často se ptáme na mikro-biom. Stejně jako u gradingu stačí zvolna.</summary>
        private const float SampleInterval = 0.4f;

        /// <summary>Hrana krychle, ve které částice vznikají (m). Musí být větší než dohled na detail.</summary>
        private const float BoxSize = 30f;

        /// <summary>Výška emitoru nad hráčem – listí padá shora, popílek stoupá zdola.</summary>
        private const float BoxHeight = 13f;

        /// <summary>Strop počtu živých částic. Drobný efekt nemá stát víc než pár desetin ms.</summary>
        private const int MaxParticles = 220;

        private ParticleSystem system;
        private ParticleSystemRenderer psRenderer;
        private Transform viewer;

        private float nextSample;
        private MicroBiome active = MicroBiome.None;

        /// <summary>
        /// Kolo 19: střídmý popílek nad spáleništěm (1) a vulkanickou oblastí (2) – stejný levný systém
        /// a nálada jako nad čedičovým polem, jen nižší hustota. Žádný nový kouř ani mlha.
        /// </summary>
        private int regionAsh;

        /// <summary>Kolo 19: A/B přepínač popílku v regionech (konzole).</summary>
        public static bool RegionAshEnabled = true;

        /// <summary>Co se právě sype. Čte konzolový příkaz /atmo.</summary>
        public MicroBiome ActiveMood => active;

        /// <summary>Kolik částic zrovna žije. Taky jen pro konzoli.</summary>
        public int LiveParticles => system != null ? system.particleCount : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Ambient Particles") { hideFlags = HideFlags.DontSave };
            go.AddComponent<AmbientParticles>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Postaví systém i materiál. Volá se jednou za běh hry.</summary>
        private void Build()
        {
            system = gameObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.playOnAwake = false;
            main.loop = true;

            // Bez tohohle systém NIC NEVYSÍLÁ a nedá se poznat proč: hlásí playing=true,
            // emitting=true, správnou rychlost – a přitom má nula částic. Výchozí režim
            // Automatic totiž zastaví simulaci, dokud nejsou hranice emitoru na obrazovce,
            // jenže emitor je krychle nad hlavou hráče, kam se skoro nikdy nedívá. Listí
            // tak čekalo, až se na ně podívá, a ono nikdy nevzniklo.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(BoxSize, 1f, BoxSize);

            // Krychle vysílá podél svého +Z. Otočením o 90° míří dolů, takže listí opravdu
            // padá; bez toho by odlétalo vodorovně na sever a gravitace by ho dohnala až
            // někde za obzorem.
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            // Rotace za letu: bez ní je list placka, která se otáčí jen kolem své osy.
            ParticleSystem.RotationOverLifetimeModule rot = system.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = FadeInOut();

            // Šum dělá z padání listí let listí. Je to jediný důvod, proč efekt nevypadá
            // jako déšť teček – bez něj letí všechno po přímce a oko to okamžitě pozná.
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.35f;

            psRenderer = GetComponent<ParticleSystemRenderer>();
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.alignment = ParticleSystemRenderSpace.View;
            psRenderer.sharedMaterial = BuildMaterial();
            psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            psRenderer.sortingFudge = 0f;
        }

        /// <summary>
        /// Materiál se staví z kódu: průhledný unlit s měkkou kulatou texturou.
        ///
        /// <para>Textura se generuje, aby efekt nezávisel na assetu, který nikdo nevidí
        /// v projektu a snadno se ztratí. 32×32 pixelů stačí – při velikosti listu na
        /// obrazovce by ostřejší kresba stejně zanikla.</para>
        /// </summary>
        private static Material BuildMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");

            var m = new Material(sh) { name = "Ambient Particle (runtime)", hideFlags = HideFlags.DontSave };
            m.SetTexture("_BaseMap", SoftDot());
            m.mainTexture = SoftDot();

            // Průhlednost se u URP shaderu NEZAPNE tím, že se nastaví _Surface na 1.
            // Ta hodnota jen říká inspektoru, co ukazovat; o skutečném míchání rozhodují
            // klíčové slovo _SURFACE_TYPE_TRANSPARENT a blend faktory. Bez nich se materiál
            // chová jako neprůhledný a z měkké kulaté skvrny je ostrý oranžový čtverec –
            // přesně tak to poprvé i vypadalo.
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return m;
        }

        private static Texture2D softDot;

        /// <summary>Měkká kulatá skvrna. Vyrobí se jednou a sdílí.</summary>
        private static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;

            const int size = 32;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Ambient Particle Dot",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.45f, 1f, d));
                softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            softDot.Apply();
            return softDot;
        }

        private static ParticleSystem.MinMaxGradient FadeInOut()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            return new ParticleSystem.MinMaxGradient(g);
        }

        private void Update()
        {
            if (system == null) return;

            if (Time.time >= nextSample)
            {
                nextSample = Time.time + SampleInterval;
                Resample();
            }

            if (viewer == null) return;

            // Emitor se posadí nad hráče (listí) nebo pod něj (popílek stoupá).
            Vector3 p = viewer.position;
            transform.position = new Vector3(p.x, p.y + (active == MicroBiome.GoldenGrove ? BoxHeight : -2f), p.z);
        }

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
            MicroBiome m = t.MicroAt(p.x, p.z, out float w);
            if (w < 0.3f) m = MicroBiome.None;

            // Kolo 19: region jen tam, kde nerozhodl mikro-biom (analyticky, bez hydrologie; 1× za 0,4 s).
            int ash = 0;
            if (m == MicroBiome.None && RegionAshEnabled && WorldGenSettings.BiomesEnabled)
            {
                RegionWeights rw = t.RegionAt(p.x, p.z, false, out _, out _);
                ash = rw.volcanic > 0.5f ? 2 : rw.burnt > 0.5f ? 1 : rw.geothermal > 0.5f ? 3 : rw.mushroom > 0.5f ? 4 : rw.alabaster > 0.5f ? 5 : 0;   // kolo 28: 5 = jemný bílý prach alabastru; kolo 20: 3 = střídmá pára; kolo 21: 4 = výtrusy a opar houbového lesa
            }

            if (m != active || ash != regionAsh)
            {
                active = m;
                regionAsh = ash;
                if (m == MicroBiome.None && ash > 0) ApplyRegionAsh(ash);
                else ApplyMood(m);
            }
        }

        /// <summary>
        /// Přepne moduly systému na danou náladu. Staré částice se NEMAŽOU – když hráč
        /// vyjde z háje, dopadne poslední listí a teprve pak je ticho.
        /// </summary>
        private void ApplyMood(MicroBiome m)
        {
            ParticleSystem.MainModule main = system.main;
            ParticleSystem.EmissionModule emission = system.emission;
            ParticleSystem.ShapeModule shape = system.shape;
            ParticleSystem.RotationOverLifetimeModule rot = system.rotationOverLifetime;
            ParticleSystem.NoiseModule noise = system.noise;

            switch (m)
            {
                case MicroBiome.GoldenGrove:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                    main.gravityModifier = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.95f, 0.62f, 0.16f, 0.95f), new Color(0.78f, 0.34f, 0.07f, 0.95f));

                    shape.position = Vector3.zero;
                    rot.x = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
                    rot.y = new ParticleSystem.MinMaxCurve(-2.0f, 2.0f);
                    rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

                    noise.strength = 0.9f;
                    noise.frequency = 0.3f;

                    emission.rateOverTime = 16f;
                    system.Play();
                    break;

                case MicroBiome.BasaltField:
                    // Popílek: skoro beztížný, drobný, stoupá. Delší život než listí, protože
                    // se nemá kam snést – vyhasne až tím, že zmizí v mlze.
                    main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                    main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.012f, -0.004f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.55f, 0.54f, 0.58f, 0.55f), new Color(0.28f, 0.27f, 0.31f, 0.7f));

                    shape.position = Vector3.zero;
                    rot.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                    rot.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                    rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

                    noise.strength = 0.45f;
                    noise.frequency = 0.2f;

                    // 14/s × život 9–16 s se ustálí kolem 170 živých částic. Vyšší hodnota
                    // narazí na strop 220 a systém pak přestane vysílat u hráče, zatímco
                    // starý popílek ještě dožívá opodál – hustota by kolísala podle toho,
                    // odkud hráč přišel.
                    emission.rateOverTime = 14f;
                    system.Play();
                    break;

                default:
                    // Jen přestat vysílat. Stop s Clear by uťal i to, co je ve vzduchu.
                    emission.rateOverTime = 0f;
                    break;
            }

        }

        /// <summary>Kolo 19: popílek čedičového pole se sníženou hustotou (spáleniště tmavší, uhelný).</summary>
        private void ApplyRegionAsh(int level)
        {
            ParticleSystem.MainModule main = system.main;
            ParticleSystem.EmissionModule emission = system.emission;
            ParticleSystem.ShapeModule shape = system.shape;
            ParticleSystem.RotationOverLifetimeModule rot = system.rotationOverLifetime;
            ParticleSystem.NoiseModule noise = system.noise;
            if (level == 5)
            {
                // Kolo 28: alabastrové plato – řídký jemný bílý prach unášený větrem nízko nad zemí (stejný systém, žádná mlha).
                main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.004f, 0.004f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.93f, 0.86f, 0.30f), new Color(0.88f, 0.84f, 0.76f, 0.50f));
                shape.position = Vector3.zero;
                rot.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                rot.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                noise.strength = 0.5f;
                noise.frequency = 0.2f;
                // 5/s × 7–12 s ≈ 50 živých zrnek (strop 220) – střídmě.
                emission.rateOverTime = 5f;
                system.Play();
                return;
            }
            if (level == 4)
            {
                // Kolo 21: houbový les – místní levný dojem mlhy: velké velmi průsvitné chomáče oparu nízko u země a mezi nimi
                // drobné světlé výtrusy. Stejný systém a strop částic, žádná globální mlha ani postprocess.
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.006f, 0.002f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.80f, 0.74f, 0.92f, 0.10f), new Color(0.70f, 0.95f, 0.85f, 0.35f));
                shape.position = Vector3.zero;
                rot.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                rot.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                noise.strength = 0.25f;
                noise.frequency = 0.15f;
                // 6/s × 6–10 s ≈ 50 živých částic (strop 220) – střídmě.
                emission.rateOverTime = 6f;
                system.Play();
                return;
            }
            if (level == 3)
            {
                // Kolo 20: geotermální pole – řídké chomáče páry stoupající od země (stejný systém, žádná mlha ani postprocess).
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.035f, -0.015f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.96f, 0.97f, 0.22f), new Color(0.88f, 0.90f, 0.92f, 0.32f));
                shape.position = Vector3.zero;
                rot.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                rot.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                noise.strength = 0.35f;
                noise.frequency = 0.25f;
                // 5/s × 4–7 s ≈ 30 živých chomáčů (strop 220) – střídmě.
                emission.rateOverTime = 5f;
                system.Play();
                return;
            }
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.012f, -0.004f);
            main.startColor = level == 2
                ? new ParticleSystem.MinMaxGradient(new Color(0.50f, 0.48f, 0.47f, 0.5f), new Color(0.24f, 0.22f, 0.22f, 0.65f))
                : new ParticleSystem.MinMaxGradient(new Color(0.36f, 0.33f, 0.31f, 0.5f), new Color(0.14f, 0.13f, 0.12f, 0.65f));
            shape.position = Vector3.zero;
            rot.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            rot.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            noise.strength = 0.45f;
            noise.frequency = 0.2f;
            // 6–9/s × 9–16 s ≈ 75–110 živých částic (strop 220) – polovina hustoty čedičového pole.
            emission.rateOverTime = level == 2 ? 9f : 6f;
            system.Play();
        }

        /// <summary>Kolo 19: aktivní popílek regionu (0 žádný, 1 spáleniště, 2 vulkán) – pro konzoli.</summary>
        public int RegionAsh => regionAsh;

        /// <summary>Popis pro konzoli.</summary>
        public string Describe()
            => active == MicroBiome.None
                ? (regionAsh > 0 ? string.Format("particles: region ash {0}, {1} alive", regionAsh == 5 ? "alabaster dust" : regionAsh == 4 ? "mushroom spores" : regionAsh == 3 ? "geothermal steam" : regionAsh == 2 ? "volcanic" : "burnt", LiveParticles)
                                 : string.Format("particles: idle ({0} still falling)", LiveParticles))
                : string.Format("particles: {0}, {1} alive", active, LiveParticles);
    }
}
