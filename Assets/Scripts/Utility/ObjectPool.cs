using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generická třída object poolu pro libovolný typ dědící z MonoBehaviour.
/// Zabraňuje opakovanému vytváření a ničení objektů (Instantiate/Destroy),
/// které jsou výkonnostně nákladné. Místo toho objekty deaktivuje a znovu aktivuje.
/// Pokud jsou všechny objekty v poolu aktivní, pool se automaticky rozšíří o 200 nových.
/// Všechny poolované objekty jsou potomky sdíleného parent GameObjectu pro přehlednost hierarchie.
/// </summary>
public class ObjectPool<PoolObject> where PoolObject : MonoBehaviour
{
    /// <summary>Prefab, ze kterého se vytvářejí nové instance v poolu.</summary>
    private PoolObject prefab;

    /// <summary>Interní seznam všech instancí (aktivních i neaktivních).</summary>
    private List<PoolObject> pool = new List<PoolObject>();

    /// <summary>Rodičovský GameObject sdružující všechny poolované objekty v hierarchii.</summary>
    private GameObject parent;

    /// <summary>
    /// Vytvoří pool načtením prefabu z Resources složky.
    /// </summary>
    /// <param name="path">Cesta k prefabu v Resources (bez přípony).</param>
    /// <param name="initialPoolSize">Počáteční počet předvytvořených instancí.</param>
    public ObjectPool(string path, int initialPoolSize = 5)
    {
        this.prefab = Resources.Load<PoolObject>(path);
        parent = new GameObject("Object Pool " + prefab.name);
        AddObjectsToPool(initialPoolSize);
    }

    /// <summary>
    /// Vytvoří pool z předané instance prefabu.
    /// </summary>
    /// <param name="prefab">Prefab MonoBehaviour, ze kterého se pool plní.</param>
    /// <param name="initialPoolSize">Počáteční počet předvytvořených instancí.</param>
    public ObjectPool(PoolObject prefab, int initialPoolSize = 5)
    {
        this.prefab = prefab.GetComponent<PoolObject>();
        parent = new GameObject("Object Pool " + prefab.name);
        AddObjectsToPool(initialPoolSize);
    }

    /// <summary>
    /// Vytvoří jednu novou instanci, resetuje její Transform a deaktivuje ji.
    /// </summary>
    private void AddObjectToPool()
    {
        var newObject = GameObject.Instantiate(prefab);
        newObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        newObject.transform.localScale = Vector3.one;
        newObject.transform.SetParent(parent.transform);
        newObject.gameObject.SetActive(false);
        pool.Add(newObject);
    }

    /// <summary>
    /// Vytvoří zadaný počet nových instancí v poolu.
    /// </summary>
    /// <param name="count">Počet instancí k vytvoření.</param>
    private void AddObjectsToPool(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AddObjectToPool();
        }
    }

    /// <summary>
    /// Vrátí první dostupnou (neaktivní) instanci z poolu a aktivuje ji.
    /// Pokud jsou všechny instance aktivní, pool se automaticky rozšíří o 200 nových.
    /// Vrátí null pokud se rozšíření nezdaří nebo pool je prázdný.
    /// </summary>
    /// <returns>Aktivovaná instance z poolu, nebo null.</returns>
    public PoolObject Get()
    {
        if (pool.TrueForAll(obj => obj != null && obj.gameObject.activeSelf)) AddObjectsToPool(200);

        PoolObject objectFromPool = null;

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            if (!pool[i].gameObject.activeSelf)
            {
                objectFromPool = pool[i];
                break;
            }
        }

        if (objectFromPool != null)
        {
            objectFromPool.gameObject.SetActive(true);
        }

        return objectFromPool;
    }

    /// <summary>
    /// Vrátí instanci zpět do poolu deaktivací a přesunutím pod parent objekt.
    /// </summary>
    /// <param name="objectToReturn">Instance k vrácení do poolu.</param>
    public void Return(PoolObject objectToReturn)
    {
        objectToReturn.gameObject.SetActive(false);
        objectToReturn.transform.SetParent(parent.transform);
    }
}