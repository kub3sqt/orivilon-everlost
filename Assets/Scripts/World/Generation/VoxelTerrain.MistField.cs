using Orivilon.World.Generation.Hydro;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Hrubé výšky povrchu pro mapu údolní mlhy (<c>Orivilon.World.Biomes.ValleyMistField</c>), 24.10.2026.
    ///
    /// <para>Jen čte generátor stejnými Burst joby jako sloupce (<c>WorldGen.Schedule</c>),
    /// bez řek, nic neukládá a nemění seed ani chunky. Bodový <see cref="SurfaceHeight"/> stál
    /// v Mono ~0,15 ms na vzorek – mapa 64×64 by blokovala hlavní vlákno na stovky ms.</para>
    ///
    /// <para>Job vlastní terén, protože čte spline LUT builderu: <see cref="CompleteMistField"/> se volá
    /// v <c>OnDestroy</c> dřív, než se buildery uvolní.</para>
    /// </summary>
    public partial class VoxelTerrain
    {
        private JobHandle mistHandle;
        private ColumnField mistData;
        private bool mistInFlight;

        /// <summary>Spustí výpočet mřížky side×side výšek s krokem step od origin (střed buněk). False = běží jiný / není builder.</summary>
        public bool TryScheduleCoarseSurface(Vector2 origin, float step, int side)
        {
            if (mistInFlight || builder == null || hydro == null) return false;
            // Platný odkaz regionu je povinný (joby hlídají přiřazené kontejnery); řeky mimo region
            // sampler sám vynechá, pro tvar údolí nejsou potřeba.
            float2 center = new float2(origin.x, origin.y) + step * side * 0.5f;
            HydroRegionRef h = HydroRef(center);
            if (!h.filled.IsCreated) return false;
            mistData = WorldGen.Schedule(new float2(origin.x + step * 0.5f, origin.y + step * 0.5f), step, side,
                                         builder.Params, builder.Splines, h, Allocator.Persistent, out mistHandle);
            mistInFlight = true;
            return true;
        }

        /// <summary>Hotové výšky (min. hladina moře) do dst. False, dokud job běží.</summary>
        public bool TryTakeCoarseSurface(float[] dst)
        {
            if (!mistInFlight || !mistHandle.IsCompleted) return false;
            mistHandle.Complete();
            float sea = SeaLevel;
            int n = Mathf.Min(dst.Length, mistData.surfY.Length);
            for (int i = 0; i < n; i++) dst[i] = Mathf.Max(sea, mistData.surfY[i]);
            mistData.Dispose();
            mistInFlight = false;
            return true;
        }

        private void CompleteMistField()
        {
            if (!mistInFlight) return;
            mistHandle.Complete();
            mistData.Dispose();
            mistInFlight = false;
        }
    }
}
