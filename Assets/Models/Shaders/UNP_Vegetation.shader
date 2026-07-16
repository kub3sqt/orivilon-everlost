Shader "UNP/Vegetation"
{
    Properties
    {
        // Texture properties: Main texture and snow mask
        [Header(Maps)]
        [Space(5)]
        [NoScaleOffset] _Texture("Texture", 2D) = "white" {}  // Main texture for vegetation
        [NoScaleOffset] _SnowMask("Snow Mask", 2D) = "white" {}  // Texture for snow effect mask

        // Material properties: Color, smoothness, and transparency settings
        [Header(Material)]
        [Space(5)]
        _MainColor("Color", Color) = (1,1,1,1)  // Base color of the vegetation
        _Smoothness("Smoothness", Range(0, 1)) = 0.5  // Surface smoothness
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.5  // Transparency cutoff for material
        _Translucency("Translucency", Range(0, 2)) = 0.6  // Prosvítání listů/stébel proti světlu (backlight)
        _ColorVariation("Color Variation", Range(0, 0.5)) = 0.14  // Nízkofrekvenční variace barvy trávy podle pozice ve světě
        _Desaturate("Desaturate", Range(0, 1)) = 0.12  // Lehké odsycení trávy (méně přepálená zeleň)

        // Second color blending settings: Allows blending of a secondary color based on height
        [Header(Second Color)]
        [Space(5)]
        [Toggle] _UseSecondColor("Enable", Float) = 0.0  // Toggle to enable second color blending
        _SecondColor("Color", Color) = (0,1,0,1)  // The secondary color used when blending
        _HeightLevel("Height", Float) = 1.0  // Height at which the second color starts blending
        _FadeRange("Fade Range", Float) = 0.2  // Range for gradual blending
        [HideInInspector] _texcoord("", 2D) = "white" {}  // Hidden texture coordinate (unused)
        [HideInInspector] __dirty("", Int) = 1  // Internal state tracking (unused)

        // Wind settings: Controls wind effects on the vegetation
        [Header(Wind)]
        [Space(5)]
        [Toggle] _EnableWind("Enable", Float) = 1.0  // Toggle to enable wind effect
        [Toggle] _SecureFoliageBase("Secure Base", Float) = 1.0  // Toggle to secure base of foliage
        _Force("Force", Float) = 1.0  // Wind force multiplier
        _Speed("Speed", Float) = 1.0  // Wind speed multiplier
        _WavesScale("Wave Scale", Float) = 1.0  // Scale for wave-like motion in wind

        // Snow settings: Controls snow effects on the vegetation
        [Header(Snow)]
        [Space(5)]
        [Toggle] _EnableSnow("Enable", Float) = 0.0  // Toggle to enable snow effect
        _SnowColor("Color", Color) = (1,1,1,1)  // Color of the snow effect
        _SnowCoverage("Coverage", Range(0, 1)) = 0.5  // Percentage of vegetation covered by snow
        _SnowHeightLevel("Height", Float) = 1.0  // Height at which snow starts to appear
        _SnowFadeRange("Fade Range", Float) = 1.0  // Range over which snow fades in/out
        _SnowMaskTiling("Tiling", Float) = 1.0  // Tiling factor for snow mask texture
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off  // Disable culling to render both sides of the geometry

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher kompatibilni material buffer (sdileny vsemi passy)
        CBUFFER_START(UnityPerMaterial)
            float4 _MainColor;
            float4 _SecondColor;
            float4 _SnowColor;
            float _Smoothness;
            float _AlphaCutoff;
            float _Translucency;
            float _ColorVariation;
            float _Desaturate;
            float _UseSecondColor;
            float _HeightLevel;
            float _FadeRange;
            float _EnableWind;
            float _SecureFoliageBase;
            float _Force;
            float _Speed;
            float _WavesScale;
            float _EnableSnow;
            float _SnowCoverage;
            float _SnowHeightLevel;
            float _SnowFadeRange;
            float _SnowMaskTiling;
        CBUFFER_END

        TEXTURE2D(_Texture);   SAMPLER(sampler_Texture);
        TEXTURE2D(_SnowMask);  SAMPLER(sampler_SnowMask);

        // Vitr - dve vetve podle typu vegetace.
        // Trava (SecureFoliageBase=1): puvodni chovani - baze drzi, spicky se vlni.
        // Listy stromu (SecureFoliageBase=0): pomale ohybani + rychle trepetani s vetsi amplitudou,
        // aby byl pohyb listi na velkych stromech viditelny (na rozdil od puvodniho drobneho posunu).
        float3 ApplyWind(float3 positionOS)
        {
            if (_EnableWind > 0.5)
            {
                float t = _Time.y * _Speed;

                if (_SecureFoliageBase > 0.5)
                {
                    // TRAVA - beze zmeny oproti puvodni verzi
                    float wind = sin(t * 7.0 + dot(positionOS, float3(1.0, 1.0, 0.0) * _WavesScale * 10.0)) * _Force * 0.5;
                    float3 windOffset = float3(wind, wind, wind) * 0.05;
                    positionOS += mul((float3x3)GetWorldToObjectMatrix(), windOffset * saturate(positionOS.y));
                }
                else
                {
                    // LISTY STROMU - kombinace pomaleho ohybani a rychleho trepetani jednotlivych listu
                    float sway = sin(t * 2.2 + dot(positionOS.xz, float2(0.7, 0.5) * _WavesScale * 3.0));
                    float flutter = sin(t * 8.5 + dot(positionOS, float3(2.7, 1.3, 2.1)))
                                  + 0.6 * sin(t * 12.0 + positionOS.y * 4.0);
                    float amp = _Force * 0.5;
                    float3 windOffset = float3(sway * 1.2 + flutter * 0.5,
                                               flutter * 0.35,
                                               sway * 0.9 + flutter * 0.5) * amp * 0.12;
                    positionOS += mul((float3x3)GetWorldToObjectMatrix(), windOffset);
                }
            }
            return positionOS;
        }

        // Povrch - barva podle vysky, snih, alpha z textury (stejna logika jako puvodni surf())
        void GetSurfaceColor(float2 uv, float3 localPos, out half3 albedo, out half alpha)
        {
            float4 texColor = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, uv);

            float blendFactor = saturate((localPos.y - _HeightLevel) / _FadeRange);
            float4 blendedColor = (_UseSecondColor > 0.5) ? lerp(_MainColor, _SecondColor, blendFactor) : _MainColor;

            float2 snowUV = uv * _SnowMaskTiling;
            float4 snowColor = float4(0, 0, 0, 0);
            if (_EnableSnow > 0.5)
            {
                float snowMaskValue = SAMPLE_TEXTURE2D(_SnowMask, sampler_SnowMask, snowUV).r;
                float snowFactor = saturate((localPos.y - _SnowHeightLevel) / _SnowFadeRange);
                snowColor = lerp(float4(0, 0, 0, 0), _SnowColor, snowFactor * (_SnowCoverage * 2.0) * snowMaskValue);
            }

            albedo = saturate(blendedColor * texColor).rgb + snowColor.rgb;
            alpha = texColor.a;
        }
        ENDHLSL

        // Hlavni osvetleny pass
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // _SCREEN_SPACE_OCCLUSION zamerne vynechano - SSAO na travе/listech delalo tmave obrysy
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 localPos : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Puvodni lokalni pozice (pred vetrem) se pouziva pro vyskove blendovani barev
                o.localPos = v.positionOS.xyz;

                float3 positionOS = ApplyWind(v.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half3 albedo;
                half alpha;
                GetSurfaceColor(i.uv, i.localPos, albedo, alpha);

                // Vylepseni vzhledu travy (jen SecureBase = trava, ne listy stromu),
                // aby uprostred dne nevypadala placata a prepalena.
                if (_SecureFoliageBase > 0.5)
                {
                    // jemny vyskovy gradient - tmavsi u zeme, svetlejsi spicky (mekky AO)
                    albedo *= lerp(0.78, 1.06, saturate(i.localPos.y));
                    // nizkofrekvencni variace podle pozice ve svete - rozbije uniformni "koberec"
                    float variation = sin(i.positionWS.x * 0.35) * sin(i.positionWS.z * 0.29);
                    albedo *= 1.0 + variation * _ColorVariation;
                    // lehke odsyceni prepalene zelene
                    half luma = dot(albedo, half3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, half3(luma, luma, luma), _Desaturate);
                }

                // Alpha cutoff (stejne chovani jako puvodni clip())
                clip(alpha - _AlphaCutoff);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(i.positionCS);
                #endif

                // Oboustranne listy - normala se otoci pro zadni strany
                float3 normalWS = NormalizeNormalPerPixel(i.normalWS);
                normalWS = isFrontFace ? normalWS : -normalWS;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0.0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = alpha;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Prosvitani (backlight) - kdyz je slunce za vegetaci, listy a stebla teple prosvitaji.
                // Nejvic se projevi za usvitu a soumraku, kdy je slunce nizko za travou.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                float backLight = saturate(dot(-inputData.viewDirectionWS, mainLight.direction));
                float trans = pow(backLight, 3.0) * _Translucency;
                color.rgb += albedo * mainLight.color.rgb * trans * mainLight.shadowAttenuation;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0; // cutout - plne krytí po clipu
                return color;
            }
            ENDHLSL
        }

        // Stinovy pass (s animaci vetru, aby stiny odpovidaly geometrii)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 localPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.localPos = v.positionOS.xyz;
                o.uv = v.uv;

                float3 positionWS = TransformObjectToWorld(ApplyWind(v.positionOS.xyz));
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
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo;
                half alpha;
                GetSurfaceColor(i.uv, i.localPos, albedo, alpha);
                clip(alpha - _AlphaCutoff);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(i.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // Depth pass (depth priming / depth textura)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 localPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.localPos = v.positionOS.xyz;
                o.uv = v.uv;
                o.positionCS = TransformObjectToHClip(ApplyWind(v.positionOS.xyz));
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo;
                half alpha;
                GetSurfaceColor(i.uv, i.localPos, albedo, alpha);
                clip(alpha - _AlphaCutoff);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(i.positionCS);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // Pozn.: DepthNormals pass zamerne odstranen - vegetace se tak neucastni SSAO
        // (SSAO kolem travy a listi delalo tmava halo). Stiny a depth pass zustavaji.
    }

    Fallback Off
}
