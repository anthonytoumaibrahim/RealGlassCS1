// Analytic, undisplaced water planes used by the reflection optics/harbour previews only.
Shader "Hidden/RealGlassPreviewWaterSurface"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                clip(scene + min(0.5, 0.0001 * i.screen.w) - i.screen.w);
                return float4(i.screen.w, 0.0, 0.0, 1.0);
            }
            ENDCG
        }
    }
}
