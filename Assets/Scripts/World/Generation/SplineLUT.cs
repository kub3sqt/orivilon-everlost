using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Orivilon.World.Generation
{
    /// <summary>
    /// Převzorkovaná křivka v NativeArray.
    ///
    /// Důvod existence: AnimationCurve.Evaluate() NENÍ thread-safe a není Burst-kompatibilní.
    /// Křivky se autorují v Inspectoru jako AnimationCurve, ale při startu světa se převedou
    /// sem a joby čtou lineární interpolací. Rozdíl proti přesné křivce je při 256 vzorcích
    /// pod 2 cm na 512 m svislého rozsahu.
    /// </summary>
    public struct SplineLUT : IDisposable
    {
        [ReadOnly] public NativeArray<float> lut;
        public float domainMin;
        public float domainMax;

        public bool IsCreated => lut.IsCreated;

        /// <summary>Vyhodnotí spline. Hodnoty mimo doménu se ořezávají na krajní vzorky.</summary>
        public float Eval(float x)
        {
            int last = lut.Length - 1;
            float t = math.saturate((x - domainMin) / (domainMax - domainMin)) * last;
            int i0 = (int)t;
            int i1 = math.min(i0 + 1, last);
            return math.lerp(lut[i0], lut[i1], t - i0);
        }

        /// <summary>
        /// Postaví LUT z AnimationCurve. Volat POUZE z hlavního vlákna – čte managed křivku.
        /// </summary>
        public static SplineLUT FromCurve(AnimationCurve curve, float domainMin, float domainMax,
                                          int resolution, Allocator allocator)
        {
            resolution = math.max(resolution, 2);
            var arr = new NativeArray<float>(resolution, allocator, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < resolution; i++)
            {
                float t = domainMin + (domainMax - domainMin) * (i / (float)(resolution - 1));
                arr[i] = curve != null ? curve.Evaluate(t) : 0f;
            }

            return new SplineLUT { lut = arr, domainMin = domainMin, domainMax = domainMax };
        }

        public void Dispose()
        {
            if (lut.IsCreated) lut.Dispose();
        }
    }

    /// <summary>Sada splinů, kterou potřebuje makro vrstva. Předává se do jobů hodnotou.</summary>
    public struct SplineSet : IDisposable
    {
        /// <summary>Continentalness → základní výška v metrech.</summary>
        public SplineLUT baseC;

        /// <summary>Erosion → násobič amplitudy (erodovaná krajina je plochá).</summary>
        public SplineLUT ampE;

        /// <summary>Continentalness → násobič amplitudy (vrcholy jen ve vnitrozemí).</summary>
        public SplineLUT ampC;

        /// <summary>Peaks &amp; valleys → normalizovaný tvar hřebene ⟨-1, 1⟩.</summary>
        public SplineLUT pv;

        public bool IsCreated => baseC.IsCreated;

        public void Dispose()
        {
            baseC.Dispose(); ampE.Dispose(); ampC.Dispose(); pv.Dispose();
        }
    }
}
