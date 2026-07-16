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
            Cull Back

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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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

            Varyings vert(Attributes v)
            {
                Varyings o;

                // Vlny pocitame ve SVETOVYCH souradnicich -> plynule navazuji pres sousedni dlazdice (zadne svary).
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                posWS.y += WaveHeight(posWS.xz);

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
