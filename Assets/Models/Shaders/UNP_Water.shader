Shader "UNP/Water"
{
    Properties
    {
        // Normal maps for water surface detail
        [Header(Maps)]
        [Space(5)]
        [NoScaleOffset] _Normal1 ("Normal Map 1", 2D) = "bump" { }
        [NoScaleOffset] _Normal2 ("Normal Map 2", 2D) = "bump" { }
        [NoScaleOffset] _RefractionTex ("Refraction Mask", 2D) = "white" { }

        // General surface properties
        [Header(Surface Properties)]
        [Space(5)]
        _Color ("Color", Color) = (0.0, 0.5, 1.0, 1.0)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Transparency ("Transparency", Range(0.0, 1.0)) = 0.7

        // Settings for water wave behavior
        [Header(Waves Settings)]
        [Space(5)]
        _WavesSpeed ("Speed", Range(0.1, 5.0)) = 1.0
        _WavesHeight ("Height", Range(0.0, 0.5)) = 0.1
        _WavesFrequency ("Frequency", Range(0.5, 5.0)) = 1.0

        // Settings for normal map animation
        [Header(Normal Map)]
        [Space(5)]
        _NormalWavesSpeed ("Speed", Range(0.0, 1.0)) = 0.05
        _TilingSize ("Tiling", Range(0.0, 10.0)) = 1.0
        _NormalIntensity ("Intensity", Range(0.0, 2.0)) = 1.0

        // Reflection settings
        [Header(Reflection)]
        [Space(5)]
        [NoScaleOffset] _ReflectionCube ("Reflection Cube", Cube) = "_Skybox" { }
        _ReflectionIntensity ("Intensity", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher kompatibilni material buffer (sdileny vsemi passy)
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Smoothness;
            float _Transparency;
            float _WavesSpeed;
            float _WavesHeight;
            float _WavesFrequency;
            float _NormalWavesSpeed;
            float _TilingSize;
            float _NormalIntensity;
            float _ReflectionIntensity;
        CBUFFER_END

        TEXTURE2D(_Normal1);        SAMPLER(sampler_Normal1);
        TEXTURE2D(_Normal2);        SAMPLER(sampler_Normal2);
        TEXTURE2D(_RefractionTex);  SAMPLER(sampler_RefractionTex);
        TEXTURECUBE(_ReflectionCube); SAMPLER(sampler_ReflectionCube);

        // Vypocet vlny - stejna matematika jako puvodni HDRP/built-in verze
        float3 ApplyWaves(float3 localPos)
        {
            float wave = sin(_Time.y * _WavesSpeed + localPos.x * _WavesFrequency) +
                         cos(_Time.y * _WavesSpeed + localPos.z * _WavesFrequency);
            localPos.y += wave * _WavesHeight;
            return localPos;
        }
        ENDHLSL

        // Shadow caster pass for rendering shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Main rendering pass for water
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha // Enable transparency
            Cull Back
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;   // Vertex position
                float2 uv : TEXCOORD0;          // UV coordinates
                float3 normalOS : NORMAL;       // Vertex normal
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;    // Screen position
                float2 uv : TEXCOORD0;              // UV coordinates
                float3 positionWS : TEXCOORD1;      // World position
                float3 normalWS : TEXCOORD2;        // World normal
                half fogFactor : TEXCOORD3;         // Fog
            };

            // Vertex shader: Adds wave motion and passes data to fragment shader
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 localPos = ApplyWaves(v.positionOS.xyz);

                o.positionCS = TransformObjectToHClip(localPos);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Fragment shader: Combines refraction, reflection, and normal effects
            half4 frag(Varyings i) : SV_Target
            {
                // Scroll normal maps for dynamic water appearance
                float2 scrollUV1 = i.uv + float2(_Time.y * _NormalWavesSpeed, 0.0);
                float2 scrollUV2 = i.uv - float2(_Time.y * _NormalWavesSpeed, 0.0);

                // Apply tiling to UV coordinates
                float2 tiledUV1 = scrollUV1 * pow(_TilingSize, 2.0);
                float2 tiledUV2 = scrollUV2 * pow(_TilingSize, 2.0);

                // Sample and unpack normal maps
                float3 normalTex1 = UnpackNormal(SAMPLE_TEXTURE2D(_Normal1, sampler_Normal1, tiledUV1));
                float3 normalTex2 = UnpackNormal(SAMPLE_TEXTURE2D(_Normal2, sampler_Normal2, tiledUV2));

                // Invert and scale normals
                normalTex1.xy = -normalTex1.xy;
                normalTex2.xy = -normalTex2.xy;
                normalTex1.xy *= _NormalIntensity;
                normalTex2.xy *= _NormalIntensity;

                // Combine normals with world normal
                float3 finalNormal = normalize(i.normalWS + normalTex1 + normalTex2);

                // Calculate refraction
                float3 I = normalize(i.positionWS - _WorldSpaceCameraPos);
                float eta = 1.33;
                float3 refracted = refract(I, finalNormal, eta);

                // Sample refraction texture
                float3 refractedColor = SAMPLE_TEXTURE2D(_RefractionTex, sampler_RefractionTex, i.uv + refracted.xy * 0.05).rgb;

                // Calculate specular highlight (zachovana puvodni matematika)
                float specular = pow(max(0.0, dot(reflect(finalNormal, finalNormal), normalize(_WorldSpaceCameraPos - i.positionWS))), 16.0 * (max(0.1, _Smoothness)));
                specular = max(specular, 0.0);

                // Sample reflection cubemap
                float3 reflection = SAMPLE_TEXTURECUBE(_ReflectionCube, sampler_ReflectionCube, finalNormal).rgb;
                reflection *= _ReflectionIntensity;

                // Combine effects with base color
                half4 color = _Color;
                color.rgb += specular;
                color.rgb += reflection * 0.5;
                color.rgb += refractedColor * 0.5;
                color.a = _Transparency;

                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
