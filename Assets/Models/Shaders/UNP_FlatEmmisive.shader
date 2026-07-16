Shader "UNP/FlatEmissive"
{
    // Properties exposed in the Unity Inspector
    Properties
    {
        _Color("Color", Color) = (0.4764151, 0.9408201, 1, 1) // The primary color of the material, including alpha for transparency
    }

    SubShader
    {
        // URP verze: ploche "emisivni" zbarveni bez osvetleni, pruhledne
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Cull Off        // Disable backface culling (renders both sides of the object)
        ZWrite Off      // Disable depth writing (ensures proper blending for transparency)
        Blend SrcAlpha OneMinusSrcAlpha // Enable alpha blending for transparency

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher kompatibilni material buffer
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 color = _Color;
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
