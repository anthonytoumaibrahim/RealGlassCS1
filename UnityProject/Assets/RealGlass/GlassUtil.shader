// Real Glass: the full-screen helpers of the glass pass, run by the mod's command buffer on the
// main camera right after the deferred final pass (before any glass is drawn).
//   Pass 0 "City": the average colour of the lower half of the picture into a 1x1 target --
//   what a reflection below the horizon shows when neither the screen nor its mirror has it
//   (the "City colour" fill), blended over a few frames so it never flickers.
//   Pass 1 "Copy": the picture itself, into the mipmapped copy the glass reflects.
//
// Both let only finite, non-negative light through. A NaN or an infinity in the picture spreads
// to a whole block of every coarser mip, and a blend keeps it for good: NaN x 0.9 is NaN, frame
// after frame -- the dark reflections of 2026-09-28, in both fills that read the city colour. So
// the city colour's blend is done here, from a copy of last frame's value, not by the blend unit:
// a bad value, or a new target's uninitialised memory, is gone the next frame instead of never.
Shader "RealGlass/GlassUtil"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;

    // NaN and negatives to 0, infinities to a ceiling no light in the game comes near. Exact on
    // Direct3D 11, whose min and max return the other operand for a NaN (IEEE 754-2008 minNum /
    // maxNum); what GPUs do elsewhere.
    float3 Finite(float3 c)
    {
        return min(max(c, 0.0), 1000.0);
    }
    ENDCG

    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "City"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag

            sampler2D _RGCityPrev;   // 1x1: last frame's city colour (copied just before this pass)
            float _RGCityBlend;      // this frame's share; 1 on a new target's first frame

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
                        sum += Finite(tex2Dlod(_MainTex, float4(uv, 0.0, 5.0)).rgb);
                    }
                }
                float3 now = sum / 32.0;
                if (_RGCityBlend >= 1.0)
                    return float4(now, 1.0);
                float3 last = Finite(tex2Dlod(_RGCityPrev, float4(0.5, 0.5, 0.0, 0.0)).rgb);
                return float4(lerp(last, now, _RGCityBlend), 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "Copy"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                return float4(Finite(tex2D(_MainTex, i.uv).rgb), 1.0);
            }
            ENDCG
        }
    }
}
