// Real Glass: the full-screen helpers of the glass pass, run by the mod's command buffer on the
// main camera right after the deferred final pass (before any glass is drawn).
//   Pass 0 "City": the average colour of the lower half of the picture into a 1x1 target --
//   what a reflection below the horizon shows when neither the screen nor its mirror has it
//   (the "City colour" fill), blended over a few frames so it never flickers.
//   Pass 1 "Copy": the picture itself, into the mipmapped copy the glass reflects.
//   Pass 2 "HiZ0", pass 3 "HiZMin", pass 4 "HiZCopy": the hierarchical depth the glass traces its
//   reflections over (GlassScreen.cs builds it: level 0 the linear eye depth, each further level
//   the nearest and the farthest of the level below, drawn into its mip).
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
    #include "Finite.cginc"   // Finite: NaN and negatives to 0, infinities to 1000, on every API

    sampler2D _MainTex;
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

        Pass
        {
            // The hierarchical depth's first level: the scene's linear eye depth, pixel for pixel, as
            // both the nearest (r) and the farthest (g). Held under 60000 m: the pyramid may be half
            // floats.
            Name "HiZ0"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float4 frag(v2f_img i) : SV_Target
            {
                float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(i.uv, 0.0, 0.0)));
                d = FiniteIn(d, 0.0, 60000.0);
                return float4(d, d, 0.0, 0.0);
            }
            ENDCG
        }

        Pass
        {
            // Each further level: the NEAREST (r) and the FARTHEST (g) depth of the 2 x 2 texels it
            // covers one level down (3 along an odd edge, so nothing is lost at the border) --
            // Uludag's Hi-Z pyramid of minima, with Tevs et al.'s maxima beside it.
            Name "HiZMin"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag

            float _RGHiZLevel;       // the level read
            float4 _RGHiZSizes;      // xy that level's size, zw this one's

            float4 frag(v2f_img i) : SV_Target
            {
                float2 dst = floor(i.uv * _RGHiZSizes.zw);
                float2 src = dst * 2.0;
                float3 extra = float3(dst.x == _RGHiZSizes.z - 1.0 && fmod(_RGHiZSizes.x, 2.0) > 0.5,
                                      dst.y == _RGHiZSizes.w - 1.0 && fmod(_RGHiZSizes.y, 2.0) > 0.5, 0.0);
                float nearest = 1e6, farthest = 0.0;
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        bool use = (x < 2 || extra.x > 0.5) && (y < 2 || extra.y > 0.5);
                        float2 uv = (min(src + float2(x, y), _RGHiZSizes.xy - 1.0) + 0.5) / _RGHiZSizes.xy;
                        float2 s = tex2Dlod(_MainTex, float4(uv, 0.0, _RGHiZLevel)).rg;
                        nearest = use ? min(nearest, s.x) : nearest;
                        farthest = use ? max(farthest, s.y) : farthest;
                    }
                }
                return float4(nearest, farthest, 0.0, 0.0);
            }
            ENDCG
        }

        Pass
        {
            // ... and into its mip: the command buffer has made that mip the target; a quad given in
            // clip space copies _RGHiZSource (the level just made, in a target of its own: a mip
            // cannot be read while another of the same texture is drawn to). CopyTexture into the
            // mip would do the same, but the NEXT level then read that mip as 0 (probed, Unity 5.6.7,
            // Direct3D 11, 2026-09-28).
            Name "HiZCopy"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _RGHiZSource;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = float4(vertex.xy, 0.5, 1.0);
                // The target is always a render texture, which Unity keeps with its first row where
                // the sampler starts: the top on Direct3D and Metal.
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
            #endif
                o.uv = uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(tex2Dlod(_RGHiZSource, float4(i.uv, 0.0, 0.0)).rg, 0.0, 0.0);
            }
            ENDCG
        }
    }
}
