using Orivilon.World.Generation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Srovná prezentaci otevřené scény s tím, co předpokládá kód generátoru.
    ///
    /// <para><b>Proč to musí být příkaz a ne výchozí hodnota.</b> Paleta i dohled jsou
    /// serializované na komponentě ve scéně. Unity použije uloženou hodnotu a změna
    /// výchozí hodnoty v kódu se do světa nikdy nedostane. Jednou už se to stalo:
    /// svět se přeladil na štíty ve 480 m, ale scéna si dál držela sněžnou čáru 185 m,
    /// takže konzole hlásila „hory" už v 72 m. Z chování se to nepozná – proto tenhle
    /// příkaz, který to srovná najednou a vypíše, co změnil.</para>
    ///
    /// <para><b>Mlha se počítá z dohledu, ne z pevného čísla.</b> To je celý smysl:
    /// dřív byla lineární mlha zavřená ve 280 m, zatímco terén se streamoval do 512 m.
    /// Hráč tedy viděl 280 m bez ohledu na to, jak dramatický terén za tím ležel.
    /// Když se teď změní počet LOD úrovní, mlha se posune s ním.</para>
    /// </summary>
    public static class WorldPresentationSetup
    {
        /// <summary>Kolik úrovní detailu. Každá další zdvojnásobí voxel i dosah prstence.</summary>
        private const int LodCount = 5;

        /// <summary>Poloměr hrubších prstenců ve vlastních chuncích.</summary>
        private const int LodRing = 4;

        /// <summary>Kde mlha začíná, jako podíl dohledu.</summary>
        private const float FogStartFraction = 0.22f;

        /// <summary>
        /// Kde je mlha plná, jako podíl dohledu. Musí být POD 1: terén na okraji posledního
        /// prstence prostě končí a kdyby tam mlha ještě nebyla plná, byla by vidět hrana.
        /// </summary>
        private const float FogEndFraction = 0.88f;

        [MenuItem("Orivilon/Scéna: srovnat prezentaci světa")]
        public static void Apply()
        {
            VoxelTerrain terrain = Object.FindFirstObjectByType<VoxelTerrain>(FindObjectsInactive.Include);
            if (terrain == null)
            {
                EditorUtility.DisplayDialog("Prezentace světa",
                    "V otevřené scéně není žádný VoxelTerrain.", "OK");
                return;
            }

            Undo.RecordObject(terrain, "Srovnat prezentaci světa");

            int oldLod = terrain.lodCount, oldRing = terrain.lodRing;
            float oldSnow = terrain.palette.snowTop;

            terrain.lodCount = LodCount;
            terrain.lodRing = LodRing;

            TerrainPalette p = TerrainPalette.Default;
            p.EnsureClimate();
            terrain.palette = p;

            EditorUtility.SetDirty(terrain);

            float view = ViewDistance(terrain);

            // Za běhu mlhu stejně přepíše SunRotation z téhož dohledu; tohle je pro
            // editor a pro scény bez denního cyklu, ať Scene view nelže.
            ApplyFog(view);

            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            EditorSceneManager.SaveScene(terrain.gameObject.scene);

            Debug.Log(
                $"[Prezentace] LOD {oldLod}→{terrain.lodCount}, prstenec {oldRing}→{terrain.lodRing}, " +
                $"dohled {view:0} m (bylo {OldView(oldLod, oldRing, terrain):0} m). " +
                $"Mlha {RenderSettings.fogStartDistance:0}–{RenderSettings.fogEndDistance:0} m. " +
                $"Sněžná čára {oldSnow:0}→{terrain.palette.snowTop:0} m, " +
                $"tráva do {terrain.palette.grassTop:0} m, mikro-variace {terrain.palette.microStrength}, " +
                $"břehový písek {terrain.palette.shoreSandRise} m. Scéna uložena.", terrain);
        }

        /// <summary>
        /// Dohled v metrech: šířka chunku nejhrubší úrovně krát poloměr prstence.
        /// Stejný vzorec, jaký hlásí <c>/teren</c>.
        /// </summary>
        private static float ViewDistance(VoxelTerrain t) => t.ViewDistance;

        private static float OldView(int lodCount, int lodRing, VoxelTerrain t)
            => VoxelWorld.ChunkDim * t.voxelSize * (1 << (lodCount - 1)) * lodRing;

        /// <summary>
        /// Lineární mlha navázaná na dohled.
        ///
        /// <para>Lineární schválně, ne exponenciální: u nízkopolygonového světa je potřeba
        /// mít jistotu, kde přesně mlha dosáhne plné síly, aby zakryla konec terénu.
        /// U exponenciální se to dá jen odhadovat a hrana se objeví při každé změně dohledu.</para>
        /// </summary>
        private static void ApplyFog(float viewDistance)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = viewDistance * FogStartFraction;
            RenderSettings.fogEndDistance = viewDistance * FogEndFraction;
        }
    }
}
