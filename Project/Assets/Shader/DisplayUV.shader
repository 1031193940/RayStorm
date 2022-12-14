Shader "Custom/Display/UV"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }

        CGINCLUDE

        #include "UnityCG.cginc"
        #include "Lighting.cginc"

        ////////////////////////////////////////////////////////////////////////////////
        // Definitions | 
        ////////////////////////////////////////////////////////////////////////////////

        #define DECLARE_TEXTURE2D(textureName) \
            UNITY_DECLARE_TEX2D(textureName);

        #define DECLARE_TEXTURE2D_ST(textureName) \
            DECLARE_TEXTURE2D(textureName); \
            float4 textureName##_ST;

        #define DECLARE_TEXTURE2D_TEXELSIZE(textureName) \
            DECLARE_TEXTURE2D_ST(textureName); \
            float4 textureName##_TexelSize;

        #define DECLARE_TEXTURECUBE(textureName) \
            UNITY_DECLARE_TEXCUBE(textureName);

        #define DECLARE_TEXTURECUBE_HDR(textureName) \
            DECLARE_TEXTURECUBE(textureName); \
            float4 textureName##_HDR;

        #define SAMPLE_TEXTURE2D(textureName, uv) \
            UNITY_SAMPLE_TEX2D(textureName, uv);

        #define SAMPLE_TEXTURECUBE(cubemapName, uvw) \
            UNITY_SAMPLE_TEXCUBE(cubemapName, uvw);

        #define SAMPLE_TEXTURECUBE_LOD(cubemapName, uvw, lod) \
            UNITY_SAMPLE_TEXCUBE_LOD(cubemapName, uvw, lod);

        ////////////////////////////////////////////////////////////////////////////////
        // Structures |
        ////////////////////////////////////////////////////////////////////////////////

        struct VertexInput
        {
            float4 vertex : POSITION;
            float2 uv     : TEXCOORD0;
            half3  normal : NORMAL;
        };

        struct VertexToFragment
        {
            float4 vertex   : SV_POSITION;
            float2 uv       : TEXCOORD0;
            half3  normalWS : TEXCOORD1;
        };

        ////////////////////////////////////////////////////////////////////////////////
        // Variables | Shader uniform variables
        ////////////////////////////////////////////////////////////////////////////////

        DECLARE_TEXTURE2D_ST(_MainTex);

        ////////////////////////////////////////////////////////////////////////////////
        // Helper Functions | Shader helper functions
        ////////////////////////////////////////////////////////////////////////////////

        // TODO : Write shader helper functions here...

        ////////////////////////////////////////////////////////////////////////////////
        // Vertex & Fragment Functions | Shader vertex and fragment functions
        ////////////////////////////////////////////////////////////////////////////////

        VertexToFragment vert(VertexInput i)
        {
            VertexToFragment o;
            o.uv = TRANSFORM_TEX(i.uv, _MainTex);
            o.vertex = UnityObjectToClipPos(i.vertex);
            o.normalWS = UnityObjectToWorldNormal(i.normal);
            return o;
        }

        half4 frag(VertexToFragment i) : SV_Target
        {
            return i.normalWS.xyzz;
            return i.uv.y;
        }

        ENDCG

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardBase"
            }

            CGPROGRAM
            #pragma target   3.0
            #pragma vertex   vert
            #pragma fragment frag
            ENDCG
        }
    }
}