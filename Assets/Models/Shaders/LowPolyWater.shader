Shader "Everlost/LowPolyWater"
{
    Properties
    {
        [Header(Colors)]
        [Space(5)]
        _ShallowColor ("Shallow (color + alpha)", Color) = (0.2, 0.75, 0.85, 0.6)
        _DeepColor ("Deep (color + alpha)", Color) = (0.02, 0.22, 0.45, 0.92)
        _DepthMax ("Depth for full color (m)", Range(0.5, 30.0)) = 6.0

        [Header(Waves)]
        [Space(5)]
        _WaveSpeed ("Speed", Range(0.0, 5.0)) = 1.0
        _WaveHeight ("Height", Range(0.0, 1.0)) = 0.12
        _WaveFrequency ("Frequency", Range(0.05, 3.0)) = 0.6

        [Header(Shore Foam)]
        [Space(5)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Width (m)", Range(0.0, 8.0)) = 1.2
        _FoamTiling ("Band Density", Range(0.0, 40.0)) = 12.0
        _FoamSpeed ("Band Speed", Range(0.0, 10.0)) = 3.0

        [Header(Reflections)]
        [Space(5)]
        _FresnelColor ("Edge Color (fresnel)", Color) = (0.7, 0.9, 1.0, 0.4)
        _FresnelPower ("Fresnel Power", Range(0.5, 8.0)) = 4.0
        _SpecStrength ("Spec Strength", Range(0.0, 2.0)) = 0.6
        _SpecPower ("Spec Sharpness", Range(8.0, 400.0)) = 200.0

        [Header(Flow (kolo 10))]
        [Space(5)]
        _FlowWaveHeight ("River Ripple Height (m)", Range(0.0, 0.5)) = 0.07
        _FlowScale ("River Ripple Density", Range(0.05, 2.0)) = 0.35
        _FlowCycle ("Flow Cycle (s)", Range(0.5, 10.0)) = 3.0
        _RapidWave ("Rapid Extra Height (m)", Range(0.0, 1.0)) = 0.22
        _RapidFoam ("Rapid Foam", Range(0.0, 1.0)) = 0.55
        _LakeWave ("Lake Wave Fraction", Range(0.0, 1.0)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            // Oboustranne: pri ponoreni hrace je videt hladina ZESPODU. S Cull Back
            // vodni plocha zevnitr proste zmizela a hrac koukal na svet jako na suchu,
            // jen s terenem kolem sebe - nebylo podle ceho poznat, ze je pod vodou.
            // Normala se ve fragmentu stejne prevraci nahoru (N.y < 0), takze spodek
            // zustava osvetleny jako strop, ne jako cerna plocha.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // SRP Batcher kompatibilni buffer.
            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DepthMax;
                float _WaveSpeed;
                float _WaveHeight;
                float _WaveFrequency;
                float4 _FoamColor;
                float _FoamDistance;
                float _FoamTiling;
                float _FoamSpeed;
                float4 _FresnelColor;
                float _FresnelPower;
                float _SpecStrength;
                float _SpecPower;
                float _FlowWaveHeight;
                float _FlowScale;
                float _FlowCycle;
                float _RapidWave;
                float _RapidFoam;
                float _LakeWave;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // Kolo 10: xy = tok po proudu (smer x rychlost m/s), z = vaha jezera, w = vaha reky.
                // Nula (mesh bez atributu) = more = puvodni chovani.
                float4 flow : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float4 flowData : TEXCOORD3; // x = peřej 0-1, y = vzor proudu 0-1, z = vaha reky
            };

            // Soucet nekolika sinu -> jemne, organicke vlny. Faze z objektovych souradnic x,z.
            float WaveHeight(float2 p)
            {
                float t = _Time.y * _WaveSpeed;
                float w = sin(p.x * _WaveFrequency + t) * 0.5;
                w += sin((p.x * 0.7 + p.y * 1.3) * _WaveFrequency * 0.8 + t * 1.3) * 0.3;
                w += cos(p.y * _WaveFrequency * 1.1 - t * 0.9) * 0.2;
                return w * _WaveHeight;
            }

            // Hodnotovy sum 2D - bez textury, hladky (smoothstep interpolace).
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Proud: dvoufazovy "flow map" nad procedurnim sumem. Vzor se posouva po proudu
            // rychlosti toku; kazda faze se po jednom cyklu vrati a mezitim ji prekryje druha,
            // takze pohyb je spojity a vzor se neroztahuje ani v ohybech (smer je lokalni).
            float FlowPattern(float2 p, float2 flow)
            {
                float t = _Time.y / _FlowCycle;
                float ph0 = frac(t), ph1 = frac(t + 0.5);
                float bw = abs(ph0 * 2.0 - 1.0);
                float2 q = p * _FlowScale;
                float n0 = VNoise(q - flow * (ph0 * _FlowCycle * _FlowScale));
                float n1 = VNoise(q - flow * (ph1 * _FlowCycle * _FlowScale) + 17.3);
                return lerp(n0, n1, bw);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;

                // Vlny pocitame ve SVETOVYCH souradnicich -> plynule navazuji pres sousedni dlazdice (zadne svary).
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                // Kolo 10: rezimy vody. More = puvodni sinusove vlny, jezero = zlomek z nich
                // (skoro klidna hladina), reka = zadne morske vlny, jen vzor tekouci po proudu;
                // na perejich (rychly tok) je vzor vyssi a pribude pena.
                float riverW = saturate(v.flow.w);
                float lakeW = saturate(v.flow.z);
                float seaW = saturate(1.0 - riverW - lakeW);
                float spd = length(v.flow.xy);
                float rapid = saturate((spd - 0.9) / 1.6);
                float fp = riverW > 0.001 ? FlowPattern(posWS.xz, v.flow.xy) : 0.5;
                float sea = WaveHeight(posWS.xz);
                posWS.y += sea * (seaW + lakeW * _LakeWave)
                         + (fp * 2.0 - 1.0) * (_FlowWaveHeight + rapid * _RapidWave) * riverW;
                o.flowData = float4(rapid * riverW, fp, riverW, 0);

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);

                // NDC pro vzorkovani hloubky (stejny vypocet jako GetVertexPositionInputs).
                float4 ndc = o.positionCS * 0.5;
                o.screenPos.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                o.screenPos.zw = o.positionCS.zw;

                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Fasetova (flat) normala z derivaci svetove pozice - to dela ten low-poly vzhled.
                float3 dpdx = ddx(i.positionWS);
                float3 dpdy = ddy(i.positionWS);
                float3 N = normalize(cross(dpdy, dpdx));
                if (N.y < 0.0) N = -N;

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);

                // Hloubka vody = rozdil sceny za vodou a hladiny (potrebuje zapnutou Depth Texture v URP).
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = i.screenPos.w;
                float waterDepth = max(0.0, sceneEye - surfaceEye);

                // Dvoutonova barva podle hloubky.
                float depth01 = saturate(waterDepth / _DepthMax);
                half4 col = lerp(_ShallowColor, _DeepColor, depth01);

                // Osvetleni - reaguje na slunce (barva/intenzita) i ambient (den/noc). Half-lambert = mekke.
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(N, mainLight.direction)) * 0.5 + 0.5;
                float3 ambient = SampleSH(N);
                col.rgb *= ambient + mainLight.color * ndl;

                // Lesk (specular) na fasetach.
                float3 halfVec = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(N, halfVec)), _SpecPower) * _SpecStrength;
                col.rgb += mainLight.color * spec;

                // Fresnel - projasneni u okraje/horizontu.
                float fresnel = pow(1.0 - saturate(dot(N, viewDir)), _FresnelPower);
                col.rgb += _FresnelColor.rgb * fresnel * _FresnelColor.a;

                // Pena u brehu - plna tesne u brehu + animovane pruhy o kus dal.
                float foamEdge = 1.0 - saturate(waterDepth / _FoamDistance);
                float foamWave = 0.6 + 0.4 * sin(waterDepth * _FoamTiling - _Time.y * _FoamSpeed);
                float foamBand = smoothstep(0.55, 0.9, foamEdge * foamWave);
                float foamShore = smoothstep(0.85, 1.0, foamEdge);
                float foam = saturate(foamBand + foamShore);
                // Kolo 10: pena jen na perejich a vodopadech - podle rychlosti toku, ve vzoru proudu.
                foam = saturate(foam + i.flowData.x * _RapidFoam * smoothstep(0.55, 0.8, i.flowData.y));
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam);

                // Alfa: hlubsi = neprusvitnejsi, pena neprusvitna.
                col.a = lerp(_ShallowColor.a, _DeepColor.a, depth01);
                col.a = saturate(col.a + foam);

                col.rgb = MixFog(col.rgb, i.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
