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

        // Kolo 10: globalni parametry svetla vegetace (Shader.SetGlobalFloat z VegetationLook).
        // Vychozi 0 = puvodni chovani, takze bez skriptu se nic nemeni.
        float _FolNormalUpLeaves;   // 0-1: normala listu k "korune" (nahoru), mene cernych rubu listu
        float _FolNormalUpGrass;    // 0-1: normala travy k vertikale (trava se svetli jako teren)
        float _FolShadowFloor;      // 0-1: kolik primeho svetla projde do stinu na listech
        float _FolAmbient;          // pridany podil ambientu (SH) na listech
        float _FolShadowFloorGrass; // totez pro travu (mene - trava ma drzet tonalitu terenu)
        float _FolAmbientGrass;
        float _FolDiag;             // diagnostika: 1 = bez stinu na vegetaci, 2 = jen ambient

        TEXTURE2D(_Texture);   SAMPLER(sampler_Texture);
        TEXTURE2D(_SnowMask);  SAMPLER(sampler_SnowMask);

        // Poryvy prochazeji svetem; stejne prefaby se proto nehybou v zakrytu.
        // Barevný, hloubkový a stínový průchod sdílejí stejnou deformaci.
        float2 _FoliageWindClock; // Editorová izolace pohybu: x=1 zmrazí vítr na čase y.
        float3 ApplyWind(float3 positionOS)
        {
            if (_EnableWind > 0.5)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float t = (_FoliageWindClock.x > 0.5 ? _FoliageWindClock.y : _Time.y) * _Speed;
                float phase = dot(positionWS.xz, float2(0.035, 0.022) * _WavesScale);
                float gust = 0.65 + 0.35 * sin(t * 0.55 - phase * 0.7);
                float sway = sin(t * 1.7 - phase);
                float3 offset;
                if (_SecureFoliageBase > 0.5)
                {
                    float ripple = sin(t * 1.9 + dot(positionWS.xz, float2(0.7, 0.43)));
                    float bend = (sway + ripple * 0.2) * gust * _Force * 0.035 * saturate(positionOS.y);
                    offset = float3(bend, -abs(bend) * 0.15, bend * 0.6);
                }
                else
                {
                    // Jen jemné pomalé chvění. Subpixelové rychlé pohyby na jemných
                    // kartách blikají ve stínové mapě. Pohyb nesmí záviset na renderující kameře.
                    float flutter = sin(t * 2.3 + dot(positionWS, float3(2.7, 1.3, 2.1)));
                    offset = float3(sway, flutter * 0.05, sway * 0.6 + flutter * 0.07)
                           * gust * _Force * 0.07;
                }
                positionOS += mul((float3x3)GetWorldToObjectMatrix(), offset);
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

            half3 foliage = saturate(blendedColor * texColor).rgb;
            if (_SecureFoliageBase < 0.5)
            {
                // Tmavé palety jehličí mají po převodu do lineárního prostoru téměř
                // černé albedo. Zvedáme odrazivost, ne emisi; skutečný stín zůstává.
                half tintLuma = dot(blendedColor.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 leafFloor = blendedColor.rgb * (0.05h / max(tintLuma, 0.001h));
                // I černý texel uvnitř neprůhledného listu dostane odstín materiálu.
                // Alfa se nemění, takže mezery mezi listy zůstanou průhledné.
                foliage = max(foliage, leafFloor);
            }

            // Paleta zustava citelna, ale oslunene listi neni neonove syte.
            half luminance = dot(foliage, half3(0.2126, 0.7152, 0.0722));
            albedo = lerp(luminance.xxx, foliage, 0.82);
            if (_SecureFoliageBase < 0.5 && luminance > 0.14h)
            {
                // 24.10.2026: listí keřů a listnáčů s jasem nad ~0,14 (lineárně) svítilo jako plast.
                // Jas nad prahem se stlačí, odstín zůstává. Tmavé jehličí se nemění.
                half capped = 0.14h + (luminance - 0.14h) * 0.45h;
                albedo *= capped / luminance;
            }
            albedo += snowColor.rgb;
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
                half3 grassTint : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Kolo 26: strom, ze ktereho v mlze zbyva jen bleda koruna, plynule zmizi (dither ve viditelnosti F/3..F). Rozhoduje
            // vzdalenost POCATKU objektu, ne pixelu - kmen i koruna tehoz stromu tedy mizi najednou.
            // V huste mlze by jinak zustala jen bleda silueta koruny proti obloze, zatimco kmen
            // splyne se zamlzenym terenem (levitujici listi). _TreeFogFade = 0 -> beze zmeny.
            float _TreeFogFade;
            // Kolo 30: konec dosahu vzdalenych stromu (x = zacatek, y = konec pasma, m od kamery) - viz EverlostTrunk.
            float4 _TreeFadeDist;
            // 1 = strom normalne, 0 = zmizel; mezi tim pasmo ditheru. Pocita se z pocatku objektu.
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
                float keep = saturate((vis - _TreeFogFade * 0.3333) / (_TreeFogFade * 0.6667));   // pasmo vis <F/3, F>
                if (_TreeFadeDist.y > 0.0)   // Kolo 30: totez pasmo i podle vzdalenosti
                    keep = min(keep, saturate((_TreeFadeDist.y - distance(pivotWS, _WorldSpaceCameraPos)) / max(_TreeFadeDist.y - _TreeFadeDist.x, 1.0)));
                return keep;
            }

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
                o.grassTint = half3(1, 1, 1);
                if (_SecureFoliageBase > 0.5)
                {
                    // Jedna stabilní barva na trs, nezávislá na větru a poloze kamery.
                    // Počítá se ve vertexu, místo sinusů pro každý pixel trávy.
                    float3 root = TransformObjectToWorld(float3(0, 0, 0));
                    float3 h = frac(root.xzx * 0.1031);
                    h += dot(h, h.yzx + 33.33);
                    float tuft = frac((h.x + h.y) * h.z);
                    float patch = sin(root.x * 0.15) * sin(root.z * 0.11);
                    // 24.10.2026: přirozené tmavší i světlejší trsy a větší skvrny svěží/sušší trávy.
                    // Odstín se posouvá jen jemně – barvu nese materiál, tady jde hlavně o jas.
                    float patch2 = sin(root.x * 0.043 + 1.7) * sin(root.z * 0.037 - 0.6);
                    half brightness = 1.0h + (tuft * 2.0h - 1.0h) * max(_ColorVariation, 0.18h) * 1.2h;
                    brightness *= 1.0h + patch * _ColorVariation * 0.6h;
                    half dry = saturate(0.5h + 0.5h * patch2) * 0.45h + tuft * 0.15h;
                    o.grassTint = lerp(half3(0.92, 1.00, 0.94), half3(1.04, 1.00, 0.86), dry) * brightness;
                }

                // Kolo 26: strom za pasmem mlhy - listi uplne pryc uz ve vertexu (zadne fragmenty).
                if (_SecureFoliageBase < 0.5 && TreeFogKeep() <= 0.0) o.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                return o;
            }



            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                if (_SecureFoliageBase < 0.5)   // jen listi, ne trava: dither v pasmu mlhy (Kolo 26)
                {
                    float keep = TreeFogKeep();
                    float n = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                    clip(keep - n - 0.0001);
                }

                half3 albedo;
                half alpha;
                GetSurfaceColor(i.uv, i.localPos, albedo, alpha);

                // Vylepseni vzhledu travy (jen SecureBase = trava, ne listy stromu),
                // aby uprostred dne nevypadala placata a prepalena.
                if (_SecureFoliageBase > 0.5)
                {
                    // 24.10.2026: palety trávy mají neonové barvy (lineární jas 0,7–0,9 a sytá žlutozelená
                    // špička), na slunci pak tráva „svítí“. Jas se mapuje do rozsahu skutečné vegetace
                    // a sytost se zkrátí se zachováním odstínu – suchá tráva zůstane žlutavá, svěží zelená.
                    half luma = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                    half3 chroma = albedo / max(luma, 0.0001h);
                    chroma = lerp(half3(1, 1, 1), chroma, 0.62h * (1.0h - _Desaturate * 0.5h));
                    albedo = chroma * (0.045h + 0.175h * luma);
                    // Neonově žluté špičky palet do zelenější olivové (ubrat červenou), kořen tmavší.
                    half tip = saturate(i.localPos.y * 1.6h);
                    albedo.r *= lerp(0.95h, 0.80h, tip);
                    albedo *= lerp(0.72h, 1.0h, tip);
                    albedo *= i.grassTint;
                }

                // Alpha cutoff (stejne chovani jako puvodni clip())
                clip(alpha - _AlphaCutoff);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(i.positionCS);
                #endif

                // Oboustranne listy - normala se otoci pro zadni strany
                float3 normalWS = NormalizeNormalPerPixel(i.normalWS);
                normalWS = isFrontFace ? normalWS : -normalWS;
                // Tenké karty vegetace přijímají světlo oblohy i na rubu.
                // Normála mířící do země jinak vytvoří černé plochy i mimo vržený stín.
                normalWS.y = abs(normalWS.y);
                // Kolo 10: karty listu a stebel maji normalu kolmou na kartu. Na siluete koruny/trsu
                // se karta vidi z rubu nebo z boku, normala miri od slunce a pixel dostane jen
                // (tmavy) ambient - z toho cerny lem. Normala se proto primicha k vertikale.
                half upW = _SecureFoliageBase > 0.5 ? _FolNormalUpGrass : _FolNormalUpLeaves;
                normalWS = normalize(lerp(normalWS, float3(0, 1, 0), upW));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = max(SampleSH(normalWS), SampleSH(float3(0, 1, 0))
                    * (_SecureFoliageBase > 0.5 ? 0.8h : 1.0h));
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

                // Měkký rozptyl přes tenký list zmírní ostrou hranici N·L = 0.
                // Zůstává násobený skutečným stínem: koruny a okolní objekty dál stíní.
                half ndl = dot(normalWS, mainLight.direction);
                // Tenký list přijímá přímé světlo z obou stran. Opačná normála
                // má ovlivnit prosvítání, ne vytvořit černou polovinu koruny.
                half diffuseNdl = _SecureFoliageBase > 0.5 ? ndl : abs(ndl);
                half wrap = saturate((diffuseNdl + 0.4h) / 1.4h);
                half scatter = max(0.0h, wrap - saturate(ndl));
                color.rgb += albedo * mainLight.color.rgb * scatter * (1.0h / PI)
                           * mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // Kolo 10: stin na vegetaci neni cerny - listy a trava ve stinu dostanou cast
                // primeho svetla zpet (rozptyl korunou) a o kus vic oblohy. Stin zustava citelny.
                half ndlF = saturate(diffuseNdl * 0.5 + 0.5);
                half lost = (1.0 - mainLight.shadowAttenuation) * mainLight.distanceAttenuation;
                bool grass = _SecureFoliageBase > 0.5;
                half floorF = grass ? _FolShadowFloorGrass : _FolShadowFloor;
                half ambF = grass ? _FolAmbientGrass : _FolAmbient;
                color.rgb += albedo * mainLight.color.rgb * ndlF * lost * floorF
                           * (grass ? 1.0h : (1.0h / PI));
                color.rgb += albedo * inputData.bakedGI * ambF;
                if (_FolDiag > 1.5) color.rgb = albedo * inputData.bakedGI;
                else if (_FolDiag > 0.5) color.rgb += albedo * mainLight.color.rgb * ndlF * lost;
                float backLight = saturate(dot(-inputData.viewDirectionWS, mainLight.direction));
                // Krátká tráva nemá svítit stejně silně jako tenké listy korun.
                float trans = pow(backLight, 3.0) * _Translucency * (grass ? 0.35 : 1.0);
                color.rgb += albedo * mainLight.color.rgb * trans * mainLight.shadowAttenuation;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0; // cutout - plne krytí po clipu
                return color;
            }
            ENDHLSL
        }

        // Stínový průchod sleduje stejný vítr jako viditelná vegetace.
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
            float4 _EverlostViewPos;      // poloha hlavní kamery (BiomeAtmosphere), stínový průchod má jinou kameru
            float4 _EverlostGrassShadow;  // x = začátek, y = konec zkracování stínu trávy (m); y = 0 vypíná

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

                float3 positionOS = ApplyWind(v.positionOS.xyz);
                // 24.10.2026: drobná tráva vrhá stín jen blízko hráče. Ve vzdálenějších kaskádách
                // je stébel menší než texel stínové mapy – kreslí jen blikající šum a tmavé fleky.
                // Ke konci pásma se stín plynule zkracuje (žádná ostrá hranice), stromy a keře zůstávají.
                if (_SecureFoliageBase > 0.5 && _EverlostGrassShadow.y > 0.0)
                {
                    float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                    float k = saturate((_EverlostGrassShadow.y - distance(rootWS, _EverlostViewPos.xyz))
                                       / max(_EverlostGrassShadow.y - _EverlostGrassShadow.x, 0.01));
                    positionOS.y *= k;
                }
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                // Cull Off: bias musí směřovat podle osvětlené strany tenké karty.
                // Rubová normála jinak posouvá stín opačně a vytváří vlastní tmavé skvrny.
                normalWS = normalize(normalWS);
                // Projekce je spojitá i pro hranu vůči slunci. Pouhé převrácení
                // znaménka normály posouvalo bias skokem z jedné strany karty na druhou.
                normalWS *= dot(normalWS, lightDirectionWS);
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

        // DepthNormals: SSAO ma zdroj DepthNormals a jeho prepass zaroven plni _CameraDepthTexture.
        // Bez tohoto passu chybely koruny stromu v hloubkove texture - mlha (ValleyMist) se pak na
        // listi pocitala podle vzdaleneho terenu za nim a vetve prosvitaly modrym oparem.
        // Trava se schvalne nekresli: jeji halo v SSAO delalo tmave fleky na terenu (puvodni duvod
        // odstraneni passu). Listi samo SSAO nevzorkuje (_SCREEN_SPACE_OCCLUSION ve ForwardLit chybi).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif
            #if defined(_GBUFFER_NORMALS_OCT)
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
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
                float3 localPos : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
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
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                if (_SecureFoliageBase > 0.5) o.positionCS = float4(0.0, 0.0, 0.0, 1.0);   // tráva ne (viz výše)
                return o;
            }

            void frag(Varyings i, bool isFrontFace : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo;
                half alpha;
                GetSurfaceColor(i.uv, i.localPos, albedo, alpha);
                clip(alpha - _AlphaCutoff);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(i.positionCS);
                #endif
                float3 normalWS = normalize(i.normalWS) * (isFrontFace ? 1.0 : -1.0);
                normalWS.y = abs(normalWS.y);
                normalWS = normalize(lerp(normalWS, float3(0, 1, 0), 0.5));
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
            #else
                outNormalWS = half4(normalWS, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
