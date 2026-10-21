// Real Glass: the full-screen helpers of the glass pass, run by the mod's command buffer on the
// main camera right after the deferred final pass (before any glass is drawn).
//   Pass 0 "City": the average colour of the lower half of the picture into a 1x1 target --
//   what a reflection below the horizon shows when neither the screen nor its mirror has it
//   (the "City colour" fill), blended over a few frames so it never flickers.
//   Pass 1 "Copy": the picture itself, into the mipmapped copy the glass reflects.
//   Pass 2 "HiZ0", pass 3 "HiZMin", pass 4 "HiZCopy": the hierarchical depth the glass traces its
//   reflections over (GlassScreen.cs builds it: level 0 the linear eye depth, each further level
//   the nearest and the farthest of the level below, drawn into its mip).
//   Pass 5 "SkyBackground": not on the main camera -- in the live sky's cube (GlassSky.cs), between
//   the skybox and the clouds: the sky's light at the sun's height, and alpha cleared for the clouds.
//   Pass 6 "LodSample": not on a camera either -- while a city's LODs are compared with their
//   models (LodGlass.cs): a model's shine map and its LOD's own, each as one tile to read back.
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
    #include "FullScreen.cginc"   // vert_full / v2f_full: vert_img with a float uv (Apple GPUs honour half)

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
            #pragma vertex vert_full
            #pragma fragment frag

            sampler2D _RGCityPrev;   // 1x1: last frame's city colour (copied just before this pass)
            float _RGCityBlend;      // this frame's share; 1 on a new target's first frame

            float4 frag(v2f_full i) : SV_Target
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
            #pragma vertex vert_full
            #pragma fragment frag

            float4 frag(v2f_full i) : SV_Target
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
            #pragma vertex vert_full
            #pragma fragment frag
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float4 frag(v2f_full i) : SV_Target
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
            #pragma vertex vert_full
            #pragma fragment frag

            float _RGHiZLevel;       // the level read
            float4 _RGHiZSizes;      // xy that level's size, zw this one's

            float4 frag(v2f_full i) : SV_Target
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

        Pass
        {
            // The live sky's own sky, in each face of its cube, after the sky camera's skybox and
            // before the clouds are drawn over it (GlassSky.cs): its light x _RGSkyDusk.x -- the
            // clear sky's light at the sun's height, which the game's sky does not follow
            // (GlassUniforms.Dusk) -- and its alpha set to 0. A multiply (Blend DstColor Zero), so
            // alpha = 0 x what the skybox left; the clouds then blend premultiplied over it, which
            // leaves their opacity there, 1 - prod(1 - a): how much of the sun's disc they hide,
            // which the glass reads in the sun's direction. A quad given in clip space covers the
            // face; nothing to read, so no flip.
            Name "SkyBackground"
            Blend DstColor Zero

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            float4 _RGSkyDusk;   // x the multiplier on the sky's light

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return float4(vertex.xy, 0.5, 1.0);
            }

            float4 frag() : SV_Target
            {
                float k = FiniteIn(_RGSkyDusk.x, 0.0, 1.0);
                return float4(k, k, k, 0.0);
            }
            ENDCG
        }

        Pass
        {
            // One LOD's tile of the read-back (LodGlass.ReadBack), each texel at its centre: r its
            // model's shine over the whole shine map, g the LOD's own over its texture (its rectangle
            // of the game's LOD atlas) -- each read at the mip a tile wide (_RGLodInfo.y, .z), so a
            // texel is the average of what it covers. _RGLodInfo.x = 1: the marker tile instead (its
            // lower half and upper half two known colours: which way the read-back rows run).
            Name "LodSample"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            float4 _RGLodTarget;     // this tile in its target, texture space: x, y, width, height
            float4 _RGLodRect;       // the LOD's rectangle in the atlas, uv: x, y, width, height
            float4 _RGLodInfo;       // x 1 the marker, y the model map's mip, z the atlas's mip
            sampler2D _RGLodModel, _RGLodAtlasXys;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                float2 t = _RGLodTarget.xy + uv * _RGLodTarget.zw;
                o.pos = float4(t * 2.0 - 1.0, 0.5, 1.0);
                // Texture space in the target on every API: the first row the sampler reads is the
                // top on Direct3D and Metal, where clip space's y runs the other way.
            #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
            #endif
                o.uv = uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_RGLodInfo.x > 0.5)
                    return i.uv.y < 0.5 ? float4(0.0, 1.0, 0.0, 1.0) : float4(1.0, 0.0, 1.0, 0.0);

                float model = 1.0 - tex2Dlod(_RGLodModel, float4(i.uv, 0.0, FiniteIn(_RGLodInfo.y, 0.0, 14.0))).z;
                float lod = 1.0 - tex2Dlod(_RGLodAtlasXys, float4(_RGLodRect.xy + i.uv * _RGLodRect.zw, 0.0, FiniteIn(_RGLodInfo.z, 0.0, 14.0))).z;
                return float4(FiniteIn(model, 0.0, 1.0), FiniteIn(lod, 0.0, 1.0), 0.0, 1.0);
            }
            ENDCG
        }
    }
}
