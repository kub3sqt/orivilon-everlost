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
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        LOD 200

        // Shadow caster pass for rendering shadows
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ColorMask 0
            Cull Back
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            // Basic vertex shader for shadow pass
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            // Basic fragment shader for shadow pass
            float4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        // Main rendering pass for water
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha // Enable transparency
            Cull Back
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;    // Vertex position
                float2 uv : TEXCOORD0;      // UV coordinates
                float3 normal : NORMAL;     // Vertex normal
            };

            struct v2f
            {
                float4 pos : SV_POSITION;   // Screen position
                float2 uv : TEXCOORD0;     // UV coordinates
                float3 worldPos : TEXCOORD1; // World position
                float3 worldNormal : TEXCOORD2; // World normal
            };

            // Shader properties
            float _WavesSpeed;
            float _WavesHeight;
            float _WavesFrequency;
            float4 _Color;
            float _Smoothness;
            sampler2D _Normal1;
            sampler2D _Normal2;
            float _NormalIntensity;
            float _NormalWavesSpeed;
            float _Transparency;
            float _TilingSize;
            samplerCUBE _ReflectionCube;
            float _ReflectionIntensity;
            sampler2D _RefractionTex;

            // Vertex shader: Adds wave motion and passes data to fragment shader
            v2f vert(appdata v)
            {
                v2f o;
                float3 localPos = v.vertex.xyz;

                // Calculate wave height using sine and cosine
                float wave = sin(_Time.y * _WavesSpeed + localPos.x * _WavesFrequency) +
                             cos(_Time.y * _WavesSpeed + localPos.z * _WavesFrequency);
                localPos.y += wave * _WavesHeight;

                // Transform to screen space and calculate normals
                o.pos = UnityObjectToClipPos(float4(localPos, 1.0));
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = mul((float3x3)unity_WorldToObject, v.normal);

                o.uv = v.uv;
                return o;
            }

            // Fragment shader: Combines refraction, reflection, and normal effects
            half4 frag(v2f i) : SV_Target
            {
                // Scroll normal maps for dynamic water appearance
                float2 scrollUV1 = i.uv + float2(_Time.y * _NormalWavesSpeed, 0.0);
                float2 scrollUV2 = i.uv - float2(_Time.y * _NormalWavesSpeed, 0.0);

                // Apply tiling to UV coordinates
                float2 tiledUV1 = scrollUV1 * pow(_TilingSize, 2.0);
                float2 tiledUV2 = scrollUV2 * pow(_TilingSize, 2.0);

                // Sample and unpack normal maps
                float3 normalTex1 = UnpackNormal(tex2D(_Normal1, tiledUV1));
                float3 normalTex2 = UnpackNormal(tex2D(_Normal2, tiledUV2));

                // Invert and scale normals
                normalTex1.xy = -normalTex1.xy;
                normalTex2.xy = -normalTex2.xy;
                normalTex1.xy *= _NormalIntensity;
                normalTex2.xy *= _NormalIntensity;

                // Combine normals with world normal
                float3 finalNormal = normalize(i.worldNormal + normalTex1 + normalTex2);

                // Calculate refraction
                float3 I = normalize(i.worldPos - _WorldSpaceCameraPos);
                float eta = 1.33;
                float3 refracted = refract(I, finalNormal, eta);

                // Sample refraction texture
                float3 refractedColor = tex2Dproj(_RefractionTex, float4(i.uv + refracted.xy * 0.05, 0.0, 1.0)).rgb;

                // Calculate specular highlight
                float specular = pow(max(0.0, dot(reflect(finalNormal, finalNormal), normalize(_WorldSpaceCameraPos - i.worldPos))), 16.0 * (max(0.1, _Smoothness)));
                specular = max(specular, 0.0);

                // Sample reflection texture
                float3 reflection = texCUBE(_ReflectionCube, finalNormal).rgb;
                reflection *= _ReflectionIntensity;

                // Combine effects with base color
                half4 color = _Color;
                color.rgb += specular;
                color.rgb += reflection * 0.5;
                color.rgb += refractedColor * 0.5;
                color.a = _Transparency;

                return color;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}