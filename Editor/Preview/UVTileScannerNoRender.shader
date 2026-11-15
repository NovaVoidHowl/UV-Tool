Shader "Hidden/NVH/UVTileScanner/NoRender"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-100"
            "RenderType" = "Opaque"
            "ForceNoShadowCasting" = "True"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NoRender"
            Tags { "LightMode" = "Always" }

            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Zero One
            Fog { Mode Off }

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
                float4 position : SV_POSITION;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                clip(-1);
                return 0;
            }
            ENDCG
        }
    }

    Fallback Off
}
