using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.World.Terrain
{
    /// <summary>
    /// MonoBehaviour wrapper pro terénní chunk v Unity scéně.
    /// Slouží jako fyzický hostitel MeshFilter, MeshRenderer a MeshCollider komponent.
    /// Vyžaduje přítomnost všech tří komponent přes [RequireComponent] atributy.
    /// V Awake automaticky získá reference na tyto komponenty.
    /// Třída TerrainChunk (non-MonoBehaviour) přes tento objekt spouští coroutiny
    /// a spravuje spawnuté dekorace jako child objekty.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class TerrainChunkObject : MonoBehaviour
    {
        /// <summary>Reference na MeshFilter pro přiřazení vizuálního meshe.</summary>
        public MeshFilter meshFilter;

        /// <summary>Reference na MeshRenderer pro renderování a nastavení materiálu.</summary>
        public MeshRenderer meshRenderer;

        /// <summary>Reference na MeshCollider pro fyzikální kolize (vždy LOD0 mesh).</summary>
        public MeshCollider meshCollider;

        /// <summary>
        /// Při inicializaci automaticky získá reference na všechny tři požadované komponenty.
        /// </summary>
        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }
    }
}