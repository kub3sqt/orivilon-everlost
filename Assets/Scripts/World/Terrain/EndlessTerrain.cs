using Orivilon.Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// Singleton systému nekonečného terénu.
    /// Sleduje pohyb hráče (vieweru) a dynamicky načítá/uvolňuje terénní chunky
    /// v kruhovém okruhu renderDistance kolem hráče. Vzdálené chunky se vracejí
    /// do PoolManageru pro recyklaci. Blízké chunky dostávají nižší LOD (vyšší detail).
    /// Podporuje dva režimy načítání: optimalizovaný (vše najednou) a postupný (N chunků/snímek).
    /// </summary>
    public class EndlessTerrain : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static EndlessTerrain instance;

        private void Awake()
        {
            instance = this;
        }

        /// <summary>Vzdálenost zobrazení terénu v počtu chunků od hráče (kruhový okruh).</summary>
        [Header("Terrain Settings")]
        [Range(1, 50)]
        public int renderDistance = 10;

        /// <summary>Krok změny LOD – každých lodStep chunků od středu se LOD zvýší o 1.</summary>
        public int lodStep = 3;

        /// <summary>
        /// Vzdálenost (v chuncích) od hráče, do které mají chunky aktivní MeshCollider.
        /// Vzdálenější chunky fyziku nepotřebují – PhysX cooking všech chunků byl velký žrout CPU.
        /// </summary>
        [Range(1, 10)]
        public int colliderDistance = 3;

        /// <summary>
        /// Vzdálenost (v chuncích) od hráče, do které se instancuje tráva.
        /// Tráva dál od hráče není okem rozlišitelná, ale stála CPU/GPU výkon.
        /// </summary>
        [Range(1, 10)]
        public int grassDistance = 2;

        /// <summary>Transform sledovaného objektu (hráče/kamery) pro výpočet aktivních chunků.</summary>
        public Transform viewer;

        /// <summary>Velikost jednoho chunku v jednotkách (musí souhlasit s MapGenerator).</summary>
        public int chunkSize = 100;

        /// <summary>
        /// Celkový počet LOD úrovní. Pokrývá celou render distance (dříve při malé
        /// renderDistance vycházela 1 úroveň a vše se kreslilo v plném detailu).
        /// Zároveň je omezen dělitelností chunkSize krokem 2^lod – krok, který chunkSize
        /// nedělí beze zbytku, by vytvořil mezery na okrajích chunků.
        /// </summary>
        public int LODCount
        {
            get
            {
                int wanted = Mathf.Max(1, (renderDistance + 1) / Mathf.Max(1, lodStep) + 1);

                int maxByChunkSize = 1;
                while (maxByChunkSize < wanted && chunkSize % (1 << maxByChunkSize) == 0)
                    maxByChunkSize++;

                return Mathf.Min(wanted, maxByChunkSize);
            }
        }

        /// <summary>Vzdálenost mezi vrcholy meshe (1 = každý vrchol na 1 jednotku).</summary>
        public float vertexSpacing = 1f;

        /// <summary>Materiál aplikovaný na všechny terrain chunky.</summary>
        [Header("Materials")]
        public Material terrainMaterial;

        /// <summary>Pokud true, chunky se načítají postupně (maxChunksPerFrame za snímek).</summary>
        [Header("Performance")]
        public bool useGradualLoading = false;

        /// <summary>Aktuální XZ pozice vieweru (aktualizuje se každý snímek).</summary>
        [Header("Debug")]
        public Vector2 viewerPosition;

        /// <summary>Předchozí XZ pozice vieweru (pro detekci pohybu).</summary>
        public Vector2 viewerPositionOld;

        /// <summary>Odkaz na MapGenerator pro asynchronní generování dat.</summary>
        public static MapGenerator mapGenerator;

        /// <summary>Slovník aktivních terénních chunků (souřadnice → TerrainChunk).</summary>
        public Dictionary<Vector2Int, TerrainChunk> terrainChunks = new Dictionary<Vector2Int, TerrainChunk>();

        /// <summary>Seznam chunků viditelných v posledním update cyklu pro efektivní skrývání.</summary>
        public List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();

        /// <summary>Příznak pro vynucení aktualizace viditelných chunků (při pohybu nebo změně nastavení).</summary>
        bool updateChunks = true;

        /// <summary>Dočasný seznam souřadnic chunků pro aktualizaci (předalokovaný pro výkon).</summary>
        private List<Vector2Int> tempChunkList = new List<Vector2Int>(256);

        /// <summary>Callback volaný po vygenerování každého chunku (pro loading screen).</summary>
        private System.Action currentChunkLoadedCallback;

        /// <summary>Callback volaný po vygenerování všech chunků (pro loading screen).</summary>
        private System.Action currentAllChunksGeneratedCallback;

        /// <summary>
        /// Aktualizuje vzdálenost zobrazení a vynutí okamžitou aktualizaci chunků.
        /// Volá se ze slideru v debug panelu.
        /// </summary>
        public void UpdateRenderDistance(int value)
        {
            renderDistance = value;
            updateChunks = true;
        }

        /// <summary>
        /// Inicializuje viewer, najde MapGenerator a spustí coroutinu pro pravidelnou aktualizaci chunků.
        /// </summary>
        private void Start()
        {
            if (viewer == null)
            {
                viewer = Camera.main?.transform;
                if (viewer == null)
                {
                    Debug.LogError("Nenalezen Camera.main!");
                    return;
                }
            }

            mapGenerator = FindAnyObjectByType<MapGenerator>();
            if (mapGenerator == null)
            {
                Debug.LogError("MapGenerator nebyl nalezen!");
            }

            if (terrainMaterial == null)
            {
                Debug.LogError("TerrainMaterial není přiřazený!");
                terrainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            viewerPosition = new Vector2(viewer.position.x, viewer.position.z);
            viewerPositionOld = viewerPosition;

            StartCoroutine(UpdateVisibleChunksRoutine());
        }

        /// <summary>
        /// Sleduje pohyb vieweru. Pokud se posunul o více než čtvrtinu plochy chunku,
        /// nastaví příznak pro aktualizaci viditelných chunků.
        /// Blokováno během načítání scény.
        /// </summary>
        private void Update()
        {
            if (SceneLoader.IsLoading)
                return;

            if (viewer == null) return;

            viewerPosition = new Vector2(viewer.position.x, viewer.position.z);

            Vector2 positionDelta = viewerPositionOld - viewerPosition;
            if (positionDelta.sqrMagnitude > chunkSize * chunkSize * 0.25f)
            {
                viewerPositionOld = viewerPosition;
                updateChunks = true;
            }
        }

        /// <summary>
        /// Coroutina kontrolující každých 0.25 s příznak updateChunks.
        /// Při potřebě aktualizace spustí buď optimalizovanou nebo postupnou verzi.
        /// Blokováno během načítání scény.
        /// </summary>
        IEnumerator UpdateVisibleChunksRoutine()
        {
            var wait = new WaitForSeconds(0.25f);

            while (true)
            {
                if (SceneLoader.IsLoading)
                {
                    yield return null;
                    continue;
                }

                if (updateChunks)
                {
                    if (useGradualLoading)
                        yield return StartCoroutine(UpdateVisibleChunksGradual());
                    else
                        UpdateVisibleChunksOptimized();

                    updateChunks = false;
                }
                yield return wait;
            }
        }

        /// <summary>
        /// Rychlá aktualizace viditelných chunků v jednom snímku.
        /// Sestaví seřazený seznam souřadnic v kruhu renderDistance+1 od hráče,
        /// vrátí vzdálené chunky do poolu, skryje předchozí viditelné chunky
        /// a vytvoří nebo zobrazí nové chunky se správným LOD.
        /// Chunky jsou seřazeny od středu ven pro prioritizaci nejbližšího terénu.
        /// </summary>
        public void UpdateVisibleChunksOptimized()
        {
            if (terrainMaterial == null || viewer == null) return;

            int currentChunkCoordX = Mathf.FloorToInt(viewerPosition.x / chunkSize);
            int currentChunkCoordY = Mathf.FloorToInt(viewerPosition.y / chunkSize);

            ReturnDistantChunksToPool(currentChunkCoordX, currentChunkCoordY);

            for (int i = 0; i < terrainChunksVisibleLastUpdate.Count; i++)
            {
                terrainChunksVisibleLastUpdate[i]?.SetVisible(false);
            }
            terrainChunksVisibleLastUpdate.Clear();

            tempChunkList.Clear();
            int effectiveRenderDistance = renderDistance + 1;
            int sqrEffectiveDistance = effectiveRenderDistance * effectiveRenderDistance;

            for (int yOffset = -effectiveRenderDistance; yOffset <= effectiveRenderDistance; yOffset++)
            {
                for (int xOffset = -effectiveRenderDistance; xOffset <= effectiveRenderDistance; xOffset++)
                {
                    int sqrDistance = xOffset * xOffset + yOffset * yOffset;
                    if (sqrDistance > sqrEffectiveDistance)
                        continue;

                    tempChunkList.Add(new Vector2Int(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset));
                }
            }

            tempChunkList.Sort((a, b) =>
            {
                int distA = (a.x - currentChunkCoordX) * (a.x - currentChunkCoordX) + (a.y - currentChunkCoordY) * (a.y - currentChunkCoordY);
                int distB = (b.x - currentChunkCoordX) * (b.x - currentChunkCoordX) + (b.y - currentChunkCoordY) * (b.y - currentChunkCoordY);
                return distA.CompareTo(distB);
            });

            foreach (Vector2Int coord in tempChunkList)
            {
                int chunkDistance = Mathf.Max(Mathf.Abs(coord.x - currentChunkCoordX), Mathf.Abs(coord.y - currentChunkCoordY));
                int lod = Mathf.Max(0, chunkDistance / lodStep);

                if (terrainChunks.ContainsKey(coord))
                {
                    var chunk = terrainChunks[coord];
                    if (chunk != null)
                    {
                        chunk.SetVisible(true, lod);
                        ApplyDetailFlags(chunk, chunkDistance);
                        terrainChunksVisibleLastUpdate.Add(chunk);
                    }
                }
                else
                {
                    Vector3 position = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);
                    var newChunk = new TerrainChunk(coord, position, transform, terrainMaterial, lod);

                    if (newChunk.chunkObject != null)
                    {
                        ApplyDetailFlags(newChunk, chunkDistance);
                        terrainChunks.Add(coord, newChunk);
                        terrainChunksVisibleLastUpdate.Add(newChunk);
                    }
                }
            }
        }

        /// <summary>
        /// Zapne/vypne collider a trávu chunku podle vzdálenosti od hráče.
        /// Hystereze +1 chunk brání přeblikávání (spawn/despawn) na hranici vzdálenosti.
        /// </summary>
        private void ApplyDetailFlags(TerrainChunk chunk, int chunkDistance)
        {
            if (chunkDistance <= colliderDistance)
                chunk.SetColliderActive(true);
            else if (chunkDistance > colliderDistance + 1)
                chunk.SetColliderActive(false);

            if (chunkDistance <= grassDistance)
                chunk.SetGrassActive(true);
            else if (chunkDistance > grassDistance + 1)
                chunk.SetGrassActive(false);
        }

        /// <summary>
        /// Postupná verze aktualizace chunků – generuje od středu ven, max 5 chunků za snímek.
        /// Vhodná pro slabší hardware kde jednorázové generování způsobuje výpadky snímků.
        /// </summary>
        private IEnumerator UpdateVisibleChunksGradual()
        {
            if (terrainMaterial == null || viewer == null) yield break;

            int currentChunkCoordX = Mathf.FloorToInt(viewerPosition.x / chunkSize);
            int currentChunkCoordY = Mathf.FloorToInt(viewerPosition.y / chunkSize);

            ReturnDistantChunksToPool(currentChunkCoordX, currentChunkCoordY);

            for (int i = 0; i < terrainChunksVisibleLastUpdate.Count; i++)
            {
                terrainChunksVisibleLastUpdate[i]?.SetVisible(false);
            }
            terrainChunksVisibleLastUpdate.Clear();

            int effectiveRenderDistance = renderDistance + 1;
            int chunksThisFrame = 0;
            const int maxChunksPerFrame = 5;

            for (int distance = 0; distance <= effectiveRenderDistance; distance++)
            {
                for (int yOffset = -distance; yOffset <= distance; yOffset++)
                {
                    for (int xOffset = -distance; xOffset <= distance; xOffset++)
                    {
                        if (Mathf.Abs(xOffset) != distance && Mathf.Abs(yOffset) != distance)
                            continue;

                        Vector2Int coord = new Vector2Int(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                        int sqrDistance = xOffset * xOffset + yOffset * yOffset;
                        if (sqrDistance > effectiveRenderDistance * effectiveRenderDistance)
                            continue;

                        int lod = Mathf.Max(0, distance / lodStep);

                        if (terrainChunks.ContainsKey(coord))
                        {
                            var chunk = terrainChunks[coord];
                            if (chunk != null)
                            {
                                chunk.SetVisible(true, lod);
                                ApplyDetailFlags(chunk, distance);
                                terrainChunksVisibleLastUpdate.Add(chunk);
                            }
                        }
                        else
                        {
                            Vector3 position = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);
                            var newChunk = new TerrainChunk(coord, position, transform, terrainMaterial, lod);

                            if (newChunk.chunkObject != null)
                            {
                                ApplyDetailFlags(newChunk, distance);
                                terrainChunks.Add(coord, newChunk);
                                terrainChunksVisibleLastUpdate.Add(newChunk);
                            }
                        }

                        chunksThisFrame++;
                        if (chunksThisFrame >= maxChunksPerFrame)
                        {
                            chunksThisFrame = 0;
                            yield return null;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Vrátí do poolu chunky vzdálenější než renderDistance + 2 od aktuální pozice hráče.
        /// Tato rezerva zabraňuje zbytečnému recyklování chunků na hranici viditelnosti.
        /// </summary>
        private void ReturnDistantChunksToPool(int currentX, int currentY)
        {
            List<Vector2Int> chunksToRemove = new List<Vector2Int>();

            foreach (var chunkPair in terrainChunks)
            {
                Vector2Int coord = chunkPair.Key;
                TerrainChunk chunk = chunkPair.Value;

                int distance = Mathf.Max(Mathf.Abs(coord.x - currentX), Mathf.Abs(coord.y - currentY));

                if (distance > renderDistance + 2)
                {
                    chunksToRemove.Add(coord);
                    chunk?.Dispose();
                }
            }

            foreach (Vector2Int coord in chunksToRemove)
            {
                terrainChunks.Remove(coord);
            }
        }

        /// <summary>
        /// Vypočítá celkový počet chunků, které mají být vygenerovány v kruhu renderDistance+1.
        /// Používá ho ChunkLoaderAPI pro sledování progresu načítání.
        /// </summary>
        public int GetTargetChunkCount()
        {
            int count = 0;
            int effectiveRender = renderDistance + 1;

            for (int y = -effectiveRender; y <= effectiveRender; y++)
            {
                for (int x = -effectiveRender; x <= effectiveRender; x++)
                {
                    int sqr = x * x + y * y;
                    if (sqr <= effectiveRender * effectiveRender)
                        count++;
                }
            }

            Debug.Log($"[EndlessTerrain] GetTargetChunkCount = {count} (renderDistance={renderDistance})");
            return count;
        }

        /// <summary>
        /// Vynutí okamžitou aktualizaci chunků na aktuální pozici hráče.
        /// Volá se z GameManageru po dokončení spawnu hráče.
        /// </summary>
        public void ForceChunkUpdateAtPlayer()
        {
            if (viewer == null) return;

            viewerPosition = new Vector2(viewer.position.x, viewer.position.z);
            viewerPositionOld = viewerPosition;
            updateChunks = true;
            UpdateVisibleChunksOptimized();

            Debug.Log($"[EndlessTerrain] ForceChunkUpdateAtPlayer called. Player at: {viewer.position}");
        }

        /// <summary>
        /// Spustí coroutinu generování chunků světa pro loading screen.
        /// Uloží callbacky pro progress (onChunkLoaded) a dokončení (onComplete).
        /// </summary>
        public void GenerateWorldChunks(System.Action onChunkLoaded, System.Action onComplete)
        {
            currentChunkLoadedCallback = onChunkLoaded;
            currentAllChunksGeneratedCallback = onComplete;

            StartCoroutine(GenerateChunksRoutine());
        }

        /// <summary>
        /// Coroutina generující všechny chunky světa pro loading screen.
        /// Vyčistí existující chunky, sestaví seznam souřadnic v kruhu renderDistance+1,
        /// seřadí je od středu ven a postupně vytváří TerrainChunk instance.
        /// Každé 3 chunky uvolní snímek (yield return null) pro plynulost.
        /// Po dokončení zavolá oba callbacky.
        /// </summary>
        private IEnumerator GenerateChunksRoutine()
        {
            Debug.Log($"[EndlessTerrain] GenerateChunksRoutine START");

            foreach (var chunkPair in terrainChunks)
            {
                chunkPair.Value?.Dispose();
            }
            terrainChunks.Clear();
            terrainChunksVisibleLastUpdate.Clear();

            int currentX = Mathf.RoundToInt(viewer.position.x / chunkSize);
            int currentY = Mathf.RoundToInt(viewer.position.z / chunkSize);

            Debug.Log($"[EndlessTerrain] Player at world: {viewer.position}, chunk coords: ({currentX}, {currentY})");

            List<Vector2Int> coordsToGenerate = new List<Vector2Int>();

            int radius = renderDistance + 1;
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);
                    if (distance > radius)
                        continue;

                    Vector2Int coord = new Vector2Int(currentX + x, currentY + y);

                    if (!coordsToGenerate.Contains(coord))
                    {
                        coordsToGenerate.Add(coord);
                    }
                }
            }

            coordsToGenerate.Sort((a, b) =>
            {
                int distA = (a.x - currentX) * (a.x - currentX) + (a.y - currentY) * (a.y - currentY);
                int distB = (b.x - currentX) * (b.x - currentX) + (b.y - currentY) * (b.y - currentY);
                return distA.CompareTo(distB);
            });

            Debug.Log($"[EndlessTerrain] Will generate {coordsToGenerate.Count} chunks");

            for (int i = 0; i < coordsToGenerate.Count; i++)
            {
                Vector2Int coord = coordsToGenerate[i];

                int xOffset = Mathf.Abs(coord.x - currentX);
                int yOffset = Mathf.Abs(coord.y - currentY);
                int distance = Mathf.Max(xOffset, yOffset);
                int lod = Mathf.Max(0, distance / lodStep);

                Vector3 pos = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);
                var chunk = new TerrainChunk(coord, pos, transform, terrainMaterial, lod);

                terrainChunks[coord] = chunk;
                chunk.SetVisible(true, lod);
                ApplyDetailFlags(chunk, distance);

                currentChunkLoadedCallback?.Invoke();

                if (i % 3 == 0)
                    yield return null;
            }

            Debug.Log($"[EndlessTerrain] GenerateChunksRoutine COMPLETE: Generated {coordsToGenerate.Count} chunks");

            if (currentAllChunksGeneratedCallback != null)
            {
                currentAllChunksGeneratedCallback.Invoke();
            }

            currentChunkLoadedCallback = null;
            currentAllChunksGeneratedCallback = null;
        }

        /// <summary>
        /// Při zničení zastaví všechny coroutiny, uvolní všechny chunky do poolu
        /// a vymaže statickou referenci na instanci.
        /// </summary>
        private void OnDestroy()
        {
            StopAllCoroutines();

            foreach (var chunkPair in terrainChunks)
            {
                chunkPair.Value?.Dispose();
            }
            terrainChunks.Clear();
            terrainChunksVisibleLastUpdate.Clear();

            if (instance == this)
                instance = null;
        }
    }
}