Shader "UNP/FlatEmissive"
{
    // Properties exposed in the Unity Inspector
    Properties
    {
        _Color("Color", Color) = (0.4764151, 0.9408201, 1, 1) // The primary color of the material, including alpha for transparency
    }

    SubShader
    {
        // Define rendering order and type
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        
        // Render settings
        Cull Off        // Disable backface culling (renders both sides of the object)
        ZWrite Off      // Disable depth writing (ensures proper blending for transparency)
        Blend SrcAlpha OneMinusSrcAlpha // Enable alpha blending for transparency

        Pass
        {
            CGPROGRAM
            // Shader stages
            #pragma vertex vert          // Specify the vertex shader
            #pragma fragment frag        // Specify the fragment shader

            // Include Unity's helper functions
            #include "UnityCG.cginc"

            // Shader property for the main color
            uniform float4 _Color;

            // Vertex input structure
            struct appdata_t
            {
                float4 vertex : POSITION; // Vertex position in object space
            };

            // Vertex-to-fragment structure
            struct v2f
            {
                float4 pos : SV_POSITION; // Vertex position in clip space
                float4 color : COLOR;     // Vertex color passed to the fragment shader
            };

            // Vertex shader
            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex); // Transform object space position to clip space
                o.color = _Color;                      // Pass the color to the fragment shader
                return o;
            }

            // Fragment shader
            fixed4 frag(v2f i) : SV_Target
            {
                return i.color; // Output the interpolated color from the vertex shader
            }
            ENDCG
        }
    }

    // Fallback shader for platforms that don't support this shader
    Fallback "Unlit/Transparent"
}