// A separate depth record of visible, simulated water. Scene depth may contain a sloping seabed;
// it is used only for occlusion, never as the reflection's surface position.
Shader "RealGlass/WaterSurfaceMask"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "WaterGame.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float water : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            v2f vert(float4 vertex : POSITION)
            {
                RGGameWaterVertex g = RGGameWaterAt(vertex);
                v2f o;
                o.world = g.world;
                o.pos = mul(UNITY_MATRIX_VP, float4(g.world, 1.0));
                o.screen = ComputeScreenPos(o.pos);
                o.water = g.waterHeight * _MainTex_ST.x + _MainTex_ST.z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = RGGameSurfaceUV(i.world);
                float4 a, b;
                RGGameSurface(uv, a, b);
                clip(min(i.water - RGGameTerrainPixel(uv), b.y - 0.001));
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                // A small depth tolerance, bounded so a distant bank never becomes water.
                clip(scene + min(0.5, 0.0001 * i.screen.w) - i.screen.w);
                return float4(i.screen.w, 0.0, 0.0, 1.0);
            }
            ENDCG
        }
    }
}
