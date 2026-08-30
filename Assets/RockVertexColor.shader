// RockVertexColor.shader
// -----------------------------------------------------------------------------
// URP lit shader, který vykresluje vertex colors zapečené generátorem
// low_poly_rock_gen.py. Žádná textura není potřeba.
//
// Použití:
//   1) Hoď soubor kamkoli do Assets/ (např. Assets/Shaders/).
//   2) Create -> Material, Shader = "Everlost/Rock Vertex Color".
//   3) Materiál přiřaď kamenům.
//
// Sliders:
//   Tint                 celkové obarvení (šedý kámen -> pískovec, žula, mech...)
//   Brightness           kompenzuje ztmavení při gamma/linear převodu z glTF
//   Vertex Color Strength 0 = jednolitá barva, 1 = plné skvrny z meshe
//   Contrast             roztáhne rozdíl mezi světlými a tmavými fasetami
//
// Kdyby to v tvé verzi URP neprošlo kompilací, ekvivalent v Shader Graphu jsou
// tři uzly: Vertex Color -> Multiply (s Color property) -> Base Color na Lit
// Master Stack. Tenhle shader dělá přesně to, jen s pár slidery navíc.
// -----------------------------------------------------------------------------

Shader "Everlost/Rock Vertex Color"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Brightness("Brightness", Range(0, 3)) = 1.6
        _VertexColorStrength("Vertex Color Strength", Range(0, 1)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.08
        _Metallic("Metallic", Range(0, 1)) = 0
        [MainTexture] _BaseMap("Base Map (volitelné)", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        // Jeden společný CBUFFER pro všechny passy -> funguje SRP Batcher.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Brightness;
            half   _VertexColorStrength;
            half   _Contrast;
            half   _Smoothness;
            half   _Metallic;
            half   _Cutoff;
        CBUFFER_END
        ENDHLSL

        // ---------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RockVertex
            #pragma fragment RockFragment
            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half4  fogAndVertexLight : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings RockVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS   = nrm.normalWS;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color      = input.color;

                half3 vertexLight = VertexLighting(pos.positionWS, nrm.normalWS);
                half fogFactor    = ComputeFogFactor(pos.positionCS.z);
                output.fogAndVertexLight = half4(fogFactor, vertexLight);

                return output;
            }

            half4 RockFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // --- barva z vertex colors --------------------------------
                half3 vc = input.color.rgb;
                vc = saturate((vc - 0.5h) * _Contrast + 0.5h);
                vc = lerp(half3(1.0h, 1.0h, 1.0h), vc, _VertexColorStrength);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = _BaseColor.rgb * tex.rgb * vc * _Brightness;

                // --- standardní URP osvětlení ------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS      = input.positionWS;
                inputData.normalWS        = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord     = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord        = input.fogAndVertexLight.x;
                inputData.vertexLighting  = input.fogAndVertexLight.yzw;
                inputData.bakedGI         = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask      = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.alpha      = 1.0h;
                surfaceData.metallic   = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1.0h;
                surfaceData.normalTS   = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // Potřeba pro SSAO a depth-based efekty.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
