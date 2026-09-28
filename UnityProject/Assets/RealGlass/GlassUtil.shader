// Real Glass: the full-screen helpers of the glass pass, run by the mod's command buffer on the
// main camera right after the deferred final pass (before any glass is drawn).
//   Pass 0 "City": the average colour of the lower half of the picture into a 1x1 target --
//   what a reflection below the horizon shows when neither the screen nor its mirror has it
//   (the "City colour" fill), blended over a few frames so it never flickers.
Shader "RealGlass/GlassUtil"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
    }

    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "City"
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _RGCityBlend;   // share of this frame's average (the rest is last frame's)

            float4 frag(v2f_img i) : SV_Target
            {
                // 8 x 4 samples of a coarse mip over the lower half of the screen (v < 0.5 is the
                // bottom in the screen-position convention the copy keeps).
                float3 sum = float3(0.0, 0.0, 0.0);
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        float2 uv = float2((x + 0.5) / 8.0, (y + 0.5) / 8.0);
                        sum += tex2Dlod(_MainTex, float4(uv, 0.0, 5.0)).rgb;
                    }
                }
                return float4(sum / 32.0, _RGCityBlend);
            }
            ENDCG
        }
    }
}
