using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.World.Biomes
{
    /// <summary>
    /// Ambientní zvuk okolí podle biomu, ve kterém hráč stojí – vítr v korunách,
    /// ptáci v lese, vlny na pláži, ticho na sněhu.
    ///
    /// <para><b>Proč vlastní komponenta a ne zvuk na kameře.</b> Stejný důvod jako u
    /// <see cref="BiomeAtmosphere"/> a <see cref="AmbientParticles"/>: systém se sám
    /// zakládá z kódu při startu hry, žádný prefab ve scéně se nedá omylem smazat
    /// nebo rozpojit od toho, co čeká tahle trojice komponent.</para>
    ///
    /// <para><b>Dva zdroje, křížový fade.</b> Jeden <see cref="AudioSource"/> by při
    /// přechodu mezi biomy buď řízl smyčku, nebo ji musel zastavit a spustit znovu –
    /// obojí je slyšet jako švih. Dva zdroje se překrývají: jeden dohrává starou
    /// náladu, zatímco druhý najíždí na novou, a hlasitost se mezi nimi plynule
    /// přelévá.</para>
    ///
    /// <para><b>Klipy se nastavují v Inspectoru, ne generují z kódu.</b> Na rozdíl od
    /// částic nejde zvuk slušně syntetizovat za běhu – potřebuje skutečné nahrávky.
    /// Dokud nejsou klipy přiřazené, komponenta mlčí a nic nehlásí jako chybu.</para>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BiomeAmbience : MonoBehaviour
    {
        public static BiomeAmbience Instance { get; private set; }

        /// <summary>Jak často se přepočítá biom pod hráčem (sekundy).</summary>
        private const float SampleInterval = 0.5f;

        /// <summary>Rychlost křížového fade mezi smyčkami (jednotky hlasitosti za sekundu).</summary>
        private const float FadeSpeed = 0.35f;

        [Header("Loops per biome group")]
        [Tooltip("Forest, Jungle, Taiga")]
        [SerializeField] private AudioClip forestLoop;
        [Tooltip("Desert, Savanna")]
        [SerializeField] private AudioClip desertLoop;
        [Tooltip("Plains")]
        [SerializeField] private AudioClip plainsLoop;
        [Tooltip("Ocean, Beach")]
        [SerializeField] private AudioClip coastLoop;
        [Tooltip("Tundra, Mountains, SnowPeaks")]
        [SerializeField] private AudioClip mountainLoop;

        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.6f;

        private AudioSource sourceA;
        private AudioSource sourceB;
        private bool aIsActive = true;

        private float nextSample;
        private Transform viewer;
        private AudioClip currentClip;

        /// <summary>Biom, podle kterého se právě vybírá smyčka. Čte ho konzolový příkaz /atmo.</summary>
        public VoxelBiome ActiveBiome { get; private set; } = VoxelBiome.Plains;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("Biome Ambience") { hideFlags = HideFlags.DontSave };
            go.AddComponent<BiomeAmbience>();
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

        private void Build()
        {
            sourceA = gameObject.AddComponent<AudioSource>();
            sourceB = gameObject.AddComponent<AudioSource>();

            foreach (AudioSource s in new[] { sourceA, sourceB })
            {
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f; // 2D – ambient okolí, ne bodový zdroj
                s.volume = 0f;
                s.priority = 200; // nižší priorita než hudba/efekty hráče
            }
        }

        private AudioClip ClipFor(VoxelBiome b)
        {
            switch (b)
            {
                case VoxelBiome.Forest:
                case VoxelBiome.Jungle:
                case VoxelBiome.Taiga:
                    return forestLoop;
                case VoxelBiome.Desert:
                case VoxelBiome.Savanna:
                    return desertLoop;
                case VoxelBiome.Ocean:
                case VoxelBiome.Beach:
                    return coastLoop;
                case VoxelBiome.Tundra:
                case VoxelBiome.Mountains:
                case VoxelBiome.SnowPeaks:
                    return mountainLoop;
                default: // Plains
                    return plainsLoop;
            }
        }

        private void Update()
        {
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + SampleInterval;
                Resample();
            }

            UpdateFade();
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
            ActiveBiome = t.BiomeAt(p.x, p.z, out _);

            AudioClip wanted = ClipFor(ActiveBiome);
            if (wanted == currentClip) return;

            currentClip = wanted;
            SwapTo(wanted);
        }

        /// <summary>Spustí novou smyčku na neaktivním zdroji a prohodí, který zdroj se teď fade-uje nahoru.</summary>
        private void SwapTo(AudioClip clip)
        {
            AudioSource incoming = aIsActive ? sourceB : sourceA;
            aIsActive = !aIsActive;

            incoming.clip = clip;
            if (clip != null)
            {
                incoming.time = 0f;
                incoming.Play();
            }
            else
            {
                incoming.Stop();
            }
        }

        private void UpdateFade()
        {
            AudioSource active = aIsActive ? sourceA : sourceB;
            AudioSource fading = aIsActive ? sourceB : sourceA;

            float step = FadeSpeed * Time.deltaTime;
            active.volume = Mathf.MoveTowards(active.volume, active.clip != null ? masterVolume : 0f, step);
            fading.volume = Mathf.MoveTowards(fading.volume, 0f, step);

            if (fading.volume <= 0.0001f && fading.isPlaying)
                fading.Stop();
        }

        /// <summary>Popis aktuálního stavu pro konzoli.</summary>
        public string Describe()
        {
            string clipName = currentClip != null ? currentClip.name : "(none assigned)";
            return $"{VoxelBiomes.Name(ActiveBiome)}: ambience '{clipName}'";
        }
    }
}
