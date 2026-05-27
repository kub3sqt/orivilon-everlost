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
        // Tags and settings for rendering
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest+0" }  // Transparent cutout rendering with alpha testing
        Cull Off  // Disable culling to render both sides of the geometry
        CGINCLUDE
        #include "UnityShaderVariables.cginc"  // Include Unity shader variables for standard functionality
        #include "UnityPBSLighting.cginc"  // Include physically-based lighting model
        #include "Lighting.cginc"  // Include standard lighting functions
        #pragma target 3.0  // Shader target version (3.0 or higher)
        #pragma multi_compile_instancing  // Enable multi-instance support for rendering multiple objects

        // Structure to pass vertex data between shaders
        struct Input
        {
            float3 localPos;  // Local position of the vertex
            float3 worldPos;  // World position of the vertex
            float2 uv_texcoord;  // UV coordinates for texture mapping
        };

        // Uniform variables for passing properties from the material editor to the shader
        uniform float4 _MainColor;  // Main color
        uniform float4 _SecondColor;  // Second color for blending
        uniform sampler2D _Texture;  // Main texture
        uniform float _AlphaCutoff;  // Alpha cutoff value for transparency
        uniform float _Force;  // Wind force multiplier
        uniform float _Speed;  // Wind speed multiplier
        uniform float _WavesScale;  // Wind wave scale multiplier
        uniform float _SecureFoliageBase;  // Whether to secure the foliage base
        uniform float _HeightLevel;  // Height level for color blending
        uniform float _FadeRange;  // Range for color fading
        uniform float _Smoothness;  // Smoothness of the material surface
        uniform float _UseSecondColor;  // Whether to use the second color for blending
        uniform float _EnableWind;  // Whether wind effect is enabled

        uniform float _EnableSnow;  // Whether snow effect is enabled
        uniform float4 _SnowColor;  // Color of the snow
        uniform float _SnowCoverage;  // Coverage of snow
        uniform float _SnowHeightLevel;  // Height level where snow appears
        uniform float _SnowFadeRange;  // Snow fade range
        uniform sampler2D _SnowMask;  // Snow mask texture for controlling where snow appears
        uniform float _SnowMaskTiling;  // Tiling factor for snow mask texture

        void vertexDataFunc(inout appdata_full v, out Input o)
{
    // Initialize the output structure for the vertex shader
    UNITY_INITIALIZE_OUTPUT(Input, o);
    // Get the local position of the vertex
    float3 localPosition = v.vertex.xyz;

    // Check if wind effect is enabled
    if (_EnableWind > 0.5)
    {
        // Calculate wind force based on time, position, and wind settings
        float wind = sin(_Time.y * _Speed * 7.0 + dot(localPosition, float3(1.0, 1.0, 0.0) * _WavesScale * 10.0)) * _Force * 0.5;
        float3 windOffset = float3(wind, wind, wind);
        // Scale the wind offset for more subtle movement
        windOffset *= 0.05;

        // Apply wind effect to the vertex position, based on whether foliage is secured
        if (_SecureFoliageBase > 0.5)
        {
            // Adjust the wind offset based on the vertex height
            float heightFactor = saturate(localPosition.y);
            v.vertex.xyz += mul(unity_WorldToObject, float4(windOffset * heightFactor, 0.0)).xyz;
        }
        else
        {
            // Apply wind effect uniformly
            v.vertex.xyz += mul(unity_WorldToObject, float4(windOffset, 0.0)).xyz;
        }
    }

    // Store the local and world positions for use in the fragment shader
    o.localPos = localPosition;
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
}

void surf(Input i, inout SurfaceOutputStandard o)
{
    // Sample the texture at the given UV coordinates
    float4 tex2DNode97 = tex2D(_Texture, i.uv_texcoord);
    
    // Calculate blend factor for the second color based on vertex height
    float blendFactor = saturate((i.localPos.y - _HeightLevel) / _FadeRange);
    float4 blendedColor;

    // If the second color is enabled, blend it with the main color based on the height
    if (_UseSecondColor > 0.5)
    {
        blendedColor = lerp(_MainColor, _SecondColor, blendFactor);
    }
    else
    {
        // Otherwise, just use the main color
        blendedColor = _MainColor;
    }

    // Adjust snow UV coordinates based on snow mask tiling
    float2 snowUV = i.uv_texcoord * _SnowMaskTiling;

    // Initialize snow color to transparent (no snow)
    float4 snowColor = float4(0, 0, 0, 0);

    // If snow effect is enabled, calculate snow coverage and apply the effect
    if (_EnableSnow > 0.5)
    {
        // Sample the snow mask to get snow coverage
        float snowMaskValue = tex2D(_SnowMask, snowUV).r;
        // Calculate snow effect based on height and snow parameters
        float snowFactor = saturate((i.localPos.y - _SnowHeightLevel) / _SnowFadeRange);
        // Apply the snow color effect, blended with snow coverage
        snowColor = lerp(float4(0, 0, 0, 0), _SnowColor, snowFactor * (_SnowCoverage * 2.0) * snowMaskValue);
    }

    // Combine the blended color, texture color, and snow effect
    o.Albedo = saturate((blendedColor * tex2DNode97)).rgb + snowColor.rgb;
    
    // Use texture's alpha for transparency
    o.Alpha = tex2DNode97.a;
    // Set smoothness for the material's appearance
    o.Smoothness = _Smoothness;
    // Set metallic property to 0 for a non-metallic surface
    o.Metallic = 0.0;
    
    // Discard fragments with alpha below the cutoff value (for transparency handling)
    clip(o.Alpha - _AlphaCutoff);
}
        ENDCG  // End of current CG shader program block

        CGPROGRAM  // Start of new CG shader program block

        #pragma exclude_renderers vulkan xbox360 psp2 n3ds wiiu  // Exclude specific renderers for compatibility

         #pragma surface surf Standard keepalpha fullforwardshadows nolightmap nodirlightmap dithercrossfade vertex:vertexDataFunc  
         // Surface shader settings:
         // - surf: Function to handle surface properties (color, lighting, etc.)
         // - Standard: Use standard shader model
         // - keepalpha: Preserve transparency (alpha channel)
         // - fullforwardshadows: Enable full forward rendering for shadows
         // - nolightmap: Disable lightmap calculations
         // - nodirlightmap: Disable directional lightmap
         // - dithercrossfade: Enable smooth cross-fading
         // - vertexDataFunc: Custom vertex function for data processing


        ENDCG
        Pass
     {
          Name "ShadowCaster"  // Name of the pass, used for rendering shadows
          Tags { "LightMode" = "ShadowCaster" }  // Set the light mode to 'ShadowCaster' for this pass
          ZWrite On  // Enables writing to the depth buffer, which is necessary for shadow casting

          CGPROGRAM
          #pragma vertex vert  // Specifies the vertex shader function
          #pragma fragment frag  // Specifies the fragment shader function
          #pragma target 3.0  // Sets the shader target to model 3.0
          #pragma multi_compile_shadowcaster  // Compiles variants needed for shadow casting
          #pragma multi_compile UNITY_PASS_SHADOWCASTER  // Enables shadow caster pass variants
          #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2  // Skip certain fog variants for this pass
          #include "HLSLSupport.cginc"  // Includes helper functions for HLSL
          #if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
               #define CAN_SKIP_VPOS  // Defines CAN_SKIP_VPOS for certain platforms, which may optimize processing
          #endif
          #include "UnityCG.cginc"  // Includes common Unity functions
          #include "Lighting.cginc"  // Includes lighting functions to calculate lighting for the fragment
          #include "UnityPBSLighting.cginc"  // Includes PBR (Physically Based Rendering) lighting functions

          // Vertex structure that holds data passed from the vertex shader to the fragment shader
          struct v2f
          {
               V2F_SHADOW_CASTER;  // Shadow caster specific vertex data
               float2 customPack1 : TEXCOORD1;  // Custom texture coordinates (used for mapping textures in the shader)
               float3 worldPos : TEXCOORD2;  // World position of the vertex
               float3 worldNormal : TEXCOORD3;  // World normal of the vertex
               half4 color : COLOR0;  // Color information (may be used for vertex coloring or material color)
               UNITY_VERTEX_INPUT_INSTANCE_ID  // Instance ID for GPU instancing (used for rendering multiple objects efficiently)
               UNITY_VERTEX_OUTPUT_STEREO  // Stereo rendering output (for VR or 3D rendering)
          };

          // Vertex shader function
          v2f vert( appdata_full v )
          {
               v2f o;  // Output vertex structure
               UNITY_SETUP_INSTANCE_ID( v );  // Set up instance ID for efficient GPU instancing
               UNITY_INITIALIZE_OUTPUT( v2f, o );  // Initialize the output structure of the vertex shader
               UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );  // Initialize stereo output for VR or 3D rendering
               UNITY_TRANSFER_INSTANCE_ID( v, o );  // Transfer the instance ID to the output structure

               Input customInputData;  // Custom input data for the shader
               vertexDataFunc( v, customInputData );  // Populate the custom input data

               float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;  // Transform the vertex position to world space
               half3 worldNormal = UnityObjectToWorldNormal( v.normal );  // Transform the vertex normal to world space

               o.worldNormal = worldNormal;  // Store world normal in the output structure
               o.customPack1.xy = customInputData.uv_texcoord;  // Store custom UV texture coordinates in the output structure
               o.customPack1.xy = v.texcoord;  // Store the original vertex texture coordinates (overwrites custom ones)
               o.worldPos = worldPos;  // Store the world position in the output structure

               TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )  // Apply any necessary normal offset for the shadow caster pass
               o.color = v.color;  // Store the vertex color in the output structure

               return o;  // Return the transformed output vertex data
          }

          // Fragment shader function
          half4 frag(v2f IN
               #if !defined(CAN_SKIP_VPOS)
               , UNITY_VPOS_TYPE vpos : VPOS  // Position variable for certain platforms (to handle depth and stencil operations)
               #endif
               ) : SV_Target
          {
               UNITY_SETUP_INSTANCE_ID(IN);  // Set up instance ID for efficient GPU instancing

               Input surfIN;  // Declare surface input structure
               UNITY_INITIALIZE_OUTPUT(Input, surfIN);  // Initialize surface input structure

               surfIN.uv_texcoord = IN.customPack1.xy;  // Pass the UV coordinates to the surface shader
               surfIN.worldPos = IN.worldPos;  // Pass the world position to the surface shader

               SurfaceOutputStandard o;  // Declare a standard surface output structure
               UNITY_INITIALIZE_OUTPUT(SurfaceOutputStandard, o);  // Initialize the surface output structure

               surf(surfIN, o);  // Call the surface shader function to compute lighting, color, etc.

               half4 result = half4(o.Albedo, o.Alpha);  // Combine the albedo (color) and alpha (transparency) for the final color result
               return result;  // Return the final color result from the fragment shader
          }

          ENDCG  // End the CGPROGRAM section
     }
}
}