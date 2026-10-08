// Kolo 11: kmeny stromů. Je to URP/Lit (stejné vlastnosti, stejný CBUFFER, stíny, hloubka
// i DepthNormals pro SSAO se přebírají přímo z URP/Lit přes UsePass) – jen osvětlený pass
// přidá do stínu část světla, které kmen ztratil, a o kus víc oblohy. Stejný princip jako
// listí v kole 10 (UNP/Vegetation), ale bez ohybu normály, takže objem kmene zůstává.
// Parametry jsou globální (_Trunk*, nastavuje ObjectSpawner.UpdateTrunkLook); 0 = čisté URP/Lit.
// Leží v Resources, aby se dostal do buildu – materiál kmene vzniká jen za běhu.
Shader "Everlost/Trunk"
{
    Properties
    {
        _WorkflowMode("WorkflowMode", Float) = 1.0
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TrunkPassVertex
            #pragma fragment TrunkPassFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // Globální parametry (ObjectSpawner.UpdateTrunkLook). 0 = čisté URP/Lit.
            float _TrunkShadowFloor;   // kolik ztraceného přímého světla se vrátí do stínu (s obalem N·L)
            float _TrunkAmbient;       // přidaný podíl oblohy (SH) – tlumí ho SSAO, kontakt se zemí zůstane
            float _TrunkDiag;          // 1 = maska (plná purpurová, bez světla) pro měření pixelů
            float _TrunkAlbedoFloor;   // nejnižší lineární jas albeda kůry (tmavý pás břízy 0,046 → ~0,11)


            // Kolo 26: strom, ze kterého v mlze zbývá jen bledá koruna, plynule zmizí (dither ve viditelnosti F/3..F). Rozhoduje
            // vzdálenost POČÁTKU objektu, ne pixelu – kmen i koruna téhož stromu tedy mizí najednou.
            // V husté mlze by jinak zůstala jen bledá silueta koruny proti obloze, zatímco kmen
            // splyne se zamlženým terénem („levitující listí“). _TreeFogFade = 0 → beze změny.
            float _TreeFogFade;
            // Kolo 30: konec dosahu vzdálených stromů (x = začátek, y = konec pásma, m od kamery). Výšková mlha
            // z vrcholu odkryje i stromy za koncem TreeProxies; tady mizí dřív, než by byl vidět jejich okraj.
            float4 _TreeFadeDist;
            // 1 = strom normálně, 0 = zmizel; mezi tím pásmo ditheru. Počítá se z počátku objektu.
            float TreeFogKeep()
            {
                if (_TreeFogFade <= 0.0) return 1.0;
                bool fogOn = false;
                #if defined(FOG_LINEAR_KEYWORD_DECLARED)
                if (FOG_LINEAR) fogOn = true;
                #endif
                #if defined(FOG_EXP_KEYWORD_DECLARED)
                if (FOG_EXP) fogOn = true;
                #endif
                #if defined(FOG_EXP2_KEYWORD_DECLARED)
                if (FOG_EXP2) fogOn = true;
                #endif
                if (!fogOn) return 1.0;
                float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float viewZ = -TransformWorldToView(pivotWS).z;
                float vis = ComputeFogIntensity(ComputeFogFactorZ0ToFar(max(viewZ - _ProjectionParams.y, 0.0)));
                float keep = saturate((vis - _TreeFogFade * 0.3333) / (_TreeFogFade * 0.6667));   // pásmo vis <F/3, F>
                if (_TreeFadeDist.y > 0.0)   // Kolo 30: totéž pásmo i podle vzdálenosti
                    keep = min(keep, saturate((_TreeFadeDist.y - distance(pivotWS, _WorldSpaceCameraPos)) / max(_TreeFadeDist.y - _TreeFadeDist.x, 1.0)));
                return keep;
            }

            // Kolo 26: kmen nedostává discard (zůstává mu early-Z / HSR). Zmizí celý až tam, kde je listí
            // téhož stromu úplně pryč (konec pásma ditheru) – nikdy tedy koruna bez kmene.
            Varyings TrunkPassVertex(Attributes input)
            {
                Varyings o = LitPassVertex(input);
                UNITY_SETUP_INSTANCE_ID(input);
                if (TreeFogKeep() <= 0.0) o.positionCS = float4(0.0, 0.0, 0.0, 1.0);   // zdegenerované trojúhelníky
                return o;
            }

            half4 TrunkPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

                // Nejtmavší barva palety (šedá 61/255, lineárně 0,046) je na úrovni uhlí; ve stínu
                // z ní i s doplňkem zbude čistá čerň. Zvedne se jen to, co je pod prahem – odstín
                // zůstává, hnědá kůra (0,18) ani bílá bříza se nemění.
                half albLum = dot(surfaceData.albedo, half3(0.2126, 0.7152, 0.0722));
                if (albLum < _TrunkAlbedoFloor) surfaceData.albedo *= _TrunkAlbedoFloor / max(albLum, 0.004);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(input.positionCS);
                #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Stín na kmeni: vrátí se část světla, které zakryla koruna (s měkkým obalem N·L,
                // takže strana ke slunci je i ve stínu světlejší – objem zůstává), a trochu oblohy.
                // Obojí násobí SSAO, takže kořenový náběh a kontakt se zemí zůstanou tmavší.
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surfaceData);
                half ndlW = saturate(dot(inputData.normalWS, mainLight.direction) * 0.5 + 0.5);
                half lost = (1.0 - mainLight.shadowAttenuation) * mainLight.distanceAttenuation;
                half3 alb = surfaceData.albedo;
                color.rgb += alb * mainLight.color.rgb * (ndlW * ndlW) * lost * _TrunkShadowFloor * ao.indirectAmbientOcclusion;
                color.rgb += alb * inputData.bakedGI * _TrunkAmbient * ao.indirectAmbientOcclusion;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                if (_TrunkDiag > 0.5) color.rgb = half3(4, 0, 4);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack "Universal Render Pipeline/Lit"
}
