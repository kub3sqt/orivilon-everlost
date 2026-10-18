namespace Orivilon.World.Generation
{
    /// <summary>
    /// Strop souběžnosti terénních jobů (výkonové kolo 14.10.2026).
    ///
    /// <para><b>Proč.</b> Fyzika (PhysX), culling i render v Unity běží na TÝCHŽ pracovních
    /// vláknech jako terénní joby a job systém nezná priority. Capture ukázal všechna vlákna
    /// plná <c>DensityJob</c>/<c>CaveProbeJob</c>/<c>MarchingCubesJob</c> a hlavní vlákno
    /// stovky ms v <c>PxScene.simulate</c> – jen čekalo, až se fyzikální úloha dostane na řadu.
    /// Čím méně jader (nebo pomalejší vlákna), tím horší záseky.</para>
    ///
    /// <para><b>Jak.</b> Paralelní job se dělí na dávky a jednu dávku zpracuje vždy jedno vlákno,
    /// takže job s B dávkami nikdy nezabere víc než B vláken. <see cref="VoxelTerrain"/> nastaví
    /// <see cref="MaxParallel"/> tak, aby součet přes všechny rozpracované sloupce a chunky
    /// nechal část vláken volnou pro engine. 0 = bez omezení (načítání, teleport).</para>
    ///
    /// <para>Výsledek jobů na velikosti dávky nezávisí – mění se jen rozdělení práce, takže
    /// generování světa zůstává deterministické.</para>
    /// </summary>
    public static class JobBudget
    {
        /// <summary>Kolik vláken smí jeden terénní paralelní job zabrat. 0 = neomezeno. Jen hlavní vlákno.</summary>
        public static int MaxParallel;

        /// <summary>
        /// Závislost na předchozí práci téže „dráhy“ (lane) rozpočtu: první job nově plánované
        /// práce na ní čeká, takže joby jedné dráhy běží vždy jen po jednom. Výchozí = žádná.
        /// Nastavuje a hned po naplánování maže VoxelTerrain (jen hlavní vlákno).
        /// </summary>
        public static Unity.Jobs.JobHandle Dependency;

        /// <summary>Velikost dávky: původní, nebo větší tak, aby dávek bylo nejvýš <see cref="MaxParallel"/>.</summary>
        public static int Batch(int count, int defaultBatch)
        {
            if (MaxParallel <= 0 || count <= 0) return defaultBatch;
            int needed = (count + MaxParallel - 1) / MaxParallel;
            return needed > defaultBatch ? needed : defaultBatch;
        }
    }
}
