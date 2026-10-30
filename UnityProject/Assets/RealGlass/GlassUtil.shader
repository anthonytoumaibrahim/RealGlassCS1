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
//   Pass 7 "SkyFog": in the water's live sky cube (GlassSky.cs), between the skybox and the clouds:
//   the game's own fog over the sky, as the player sees it on screen.
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

        Pass
        {
            // The game's fog (Hidden/DayNight/Fog, DayNightFogEffect, [ImageEffectOpaque]) on a pixel
            // of sky, ported line for line from its OpenGL Core program (its names kept there): every
            // input is a global the game sets each frame (DayNightProperties' sky, FogProperties' fog,
            // NaturalResourceManager's pollution), so this is the fog the player sees over the sky.
            // In the water's live sky cube (GlassSky.cs), right after the skybox and before the clouds
            // (on screen they are drawn after the fog): the water then mirrors the sky as it is on
            // screen. Without it, the cube held the bare skybox, whose light path, 8 km / (dir.y + 0.06),
            // saturates the lowest few degrees and every direction below the horizon to the light that
            // crosses 133 km of air: at sunset and sunrise a deep red all round, which the fog hides on
            // screen but rough water, reading the cube's blurred mips, spread over the sea.
            //   A sky pixel's point P is on the far plane, depth01 = 1: here P = eye + dir x the main
            //   camera's far plane (_RGSkyFog.x) -- a sphere where the game has a plane, the same at the
            //   middle of the screen. Its fog: the horizon band saturate(1 - P.y / m_HorizonHeight) (all
            //   of the sky below the horizon), or more where the distance fog's 3D noise, faded over
            //   m_FogHeight, reaches the far plane; its colour the sky's own scattering over a path of
            //   8 km / m_ColorDecay. Drawn at the far plane under the skybox's depth (ZTest LEqual: only
            //   sky; in a face of the cube, everything), blended as the game's lerp.
            Name "SkyFog"
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            float4 _RGSkyFog;    // x the main camera's far plane (m)

            // The game's globals, as its fog declares them.
            float2 _ColorCorrection;
            float3 _BetaR, _BetaM, _MieConst, _MiePhase_g, _SunDir, _SkyMultiplier;
            float4 _NightHorizonColor, _NightZenithColor;
            float4 _FogParams0, _FogParams1, _FogPollutionColor, _FogPollutionParams, _FogSpeed;
            float4 _FogNoiseGainConstrastStepScale, _NaturalResourceMapping;
            sampler3D _Fog3DNoiseTex;
            sampler2D _NaturalDestruction;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(_WorldSpaceCameraPos + vertex.xyz * 500.0, 1.0));
                #if defined(UNITY_REVERSED_Z)
                o.pos.z = 1.0e-6 * o.pos.w;
                #else
                o.pos.z = 0.999999 * o.pos.w;
                #endif
                o.dir = vertex.xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float far = max(_RGSkyFog.x, 1.0);
                float3 eye = _WorldSpaceCameraPos;
                float3 p = eye + d * far;

                // How much fog: the horizon band (a sky pixel's), the distance fog to the far plane.
                float band = saturate(1.0 - p.y * _FogPollutionParams.z);
                float distant = 1.0 - exp(-saturate(far * _FogParams0.z - 1.0) * _FogParams0.w * _FogParams0.y);

                // Its colour: the sky's scattering (as Hidden/DayNight/Skybox) over the fog's own path.
                float mu = dot(d, _SunDir);
                float path = max(min(band, _FogParams1.y) * (d.y + _FogParams1.y), _FogParams1.y);
                float r1 = 8.0 / path;
                float r2 = 1.2 / path;
                float3 a = r1 * _NightZenithColor.xyz * (2.0 - _NightZenithColor.xyz * r1);
                float3 t = exp(-(_BetaR * r1 + _BetaM * r2));
                float3 s = _SkyMultiplier.x * (1.0 - t - t * a) + a * t;
                float3 mie = r2 * s / max(s.x, 1.0e-6) * _MieConst;
                float phase = _MiePhase_g.x * pow(max(_MiePhase_g.y - _MiePhase_g.z * mu, 1.0e-4), -1.5);
                float forward = saturate(band - (1.0 - _FogParams1.z));
                forward = _FogParams1.z * (1.0 - forward) + forward;
                float3 light = (s * 0.75 + phase * mie * forward) * ((mu * mu + 1.0) * _SkyMultiplier.y) + a * _NightHorizonColor.xyz;
                float3 colour = pow(max((1.0 - exp(-light)) * _ColorCorrection.x, 0.0), _ColorCorrection.y);

                // The distance fog's noise, 30 steps from the far plane back toward the eye.
                float3 at = p;
                float left = far;
                float noise = 0.0;
                for (int k = 0; k < 30; k++)
                {
                    float3 q = at * _FogNoiseGainConstrastStepScale.w + _FogSpeed.xyz;
                    noise += tex3Dlod(_Fog3DNoiseTex, float4(q, 0.0)).x * saturate(left / _FogNoiseGainConstrastStepScale.z);
                    at -= d * _FogNoiseGainConstrastStepScale.z;
                    left -= _FogNoiseGainConstrastStepScale.z;
                }
                noise = saturate(1.0 - exp2(noise * -0.0480898358) + _FogPollutionParams.w);

                // Pollution: its own colour where the ground is destroyed, beyond the band.
                float polluted = tex2Dlod(_NaturalDestruction, float4(p.xz * _NaturalResourceMapping.zz + _NaturalResourceMapping.xy, 0.0, 0.0)).x;
                polluted *= 1.0 - band;
                colour = lerp(colour, _FogPollutionColor.rgb, polluted);
                float amount = saturate(max(polluted * _FogPollutionParams.x, noise * distant));
                amount *= 1.0 - saturate(p.y * _FogParams1.x);
                return float4(Finite(colour), FiniteIn(max(band, amount), 0.0, 1.0));
            }
            ENDCG
        }
    }
}
