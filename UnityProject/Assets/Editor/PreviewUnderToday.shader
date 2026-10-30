// PREVIEW ONLY (FoamPreview's "today" under column, the surface from below redone 2026-10-30): a frozen
// copy of RealGlass/WaterUnder as it stood at affebf9, so the sheet shows today next to the new look.
// Never in a bundle (not in BundleBuilder's list).
//
// The view from under the water (RealGlass/Water/WaterUnder.cs draws it, Water/WaterUnderOptics.cs has
// the numbers and their sources; 2026-10-24, its look redone 2026-10-28 after a player's picture): one
// full-screen pass while the camera is at or below the water's level, on a copy of the frame before the
// image effects. Per pixel, the ray from the near plane on -- under water where that point is below the
// level, so a camera half in the water gets the waterline across the frame -- through the water to what
// it ends on:
//   THE MURK  the image formation of Jaffe 1990 / McGlamery 1975 as revised by Akkaynak & Treibitz
//             (2018, "A Revised Underwater Image Formation Model"): what the ray ends on x e^(-c d) +
//             the veiling light. The water's beam attenuation c per channel (WaterUnderOptics: pure
//             water, dissolved matter, particles), so what lies under the water keeps its shape and
//             takes the water's teal as it recedes. The veiling light is in-scattered daylight, which
//             fades with depth as e^(-K_d z) (K_d after Kirk 1984): integrated along the ray as its
//             depth changes, B0 e^(-K z0) c (1 - e^(-(c - K dy) d)) / (c - K dy) -- the water darker
//             looking down, lighter looking up; brighter toward the sun, as water's scattering is peaked
//             forward (Henyey & Greenstein 1941). B0: the water's own reflectance from those numbers x
//             the daylight x the n-squared law (WaterUnderOptics.Vectors).
//   THE SHAFTS  the sun's share of that veiling light comes only from water the sunbeams reach: the
//             ray sampled in steps (each start jittered per pixel, Jimenez 2014's interleaved gradient
//             noise), each sample's sunbeam traced back up, refracted, to where it entered the surface,
//             and that point tested against the game's own shadow map of the sun (grabbed at the
//             light's AfterShadowMap with its cascades: WaterUnder.cs) -- the light between the shadows
//             of bridges and buildings hangs in the water as shafts (single scattering with volumetric
//             shadows, Toth & Umenhoffer 2009). The share kept is the samples' lit share, weighted as
//             each adds to the veil.
//   THE BED   the game lights the seabed as dry land: x e^(-K_d depth), the daylight that reaches it;
//             the sun's share of it focused by the ripples into caustics -- the area a beam of sunlight
//             through the surface lands on (Watt 1990's beam tracing): 1 / det(I + D (1 - 1/n) H), H the
//             ripple surface's curvature, D the depth (Hessian by central differences of the ripple
//             texture the water uses).
//   THE SURFACE FROM BELOW  a level plane at the water's height, its normal the ripples': inside
//             Snell's window (48.6 deg of straight up) the world above as the frame shows it (the
//             game culls the water's underside), x the water-to-air Fresnel transmission, a little
//             displaced by the ripples; outside it total internal reflection -- the water below, the
//             veiling light along the reflected ray.
Shader "Hidden/RealGlassPreviewUnderToday"
{
    // Gray: no slope, until the mod binds the ripple texture the water is shaded with.
    Properties
    {
        _RGRipples ("", 2D) = "gray" {}
    }

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../RealGlass/Finite.cginc"

            // The scene before the water (the material's own: WaterSurfaceMask.SceneDepth).
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            // The frame as drawn (a copy).
            sampler2D _RGUnderFrame;
            // The ripple texture the water is shaded with (the game's layout: x in .w, y in .y).
            sampler2D _RGRipples;
            float4 _SimulationTime;
            // The sun's shadow map as the game rendered it this frame, and its cascades' constants
            // (24 texels: unity_WorldToShadow[4] by rows, unity_ShadowSplitSpheres[4],
            // unity_ShadowSplitSqRadii, _LightShadowData, unity_ShadowFadeCenterAndType, _LightSplitsNear).
            UNITY_DECLARE_SHADOWMAP(_RGSunShadow);
            sampler2D _RGSunShadowData;

            // x: the water's level (m), y: the caustics' strength (0 none, 1 real), z: the ripples' slope
            // per decoded unit (the game's shading at rest: 0.25), w: how far the ripples displace the
            // world seen through the surface (uv per unit of slope; the preview passes affebf9's 0.015).
            float4 _RGUnder;
            // rgb: the beam attenuation c (1/m).
            float4 _RGUnderC;
            // rgb: the daylight's diffuse attenuation K_d (1/m).
            float4 _RGUnderK;
            // rgb: the veiling light at the surface, looking level (linear).
            float4 _RGUnderVeil;
            // xyz: toward the sun under the water (refracted; unit, up). w: the sun's share of the
            // daylight just below the surface (0..1; 0 at night or under cloud).
            float4 _RGUnderSun;
            // x: 1 = the sun's shadow map is there this frame (0: no shafts, the sun's light everywhere);
            // y: the steps along a ray; z: how far they reach (m); w: how high over the surface the
            // light's entry is tested (m: the water itself never shades it).
            float4 _RGUnderShafts;
            // The preview's only (the mod never sets it): 1 = the shafts' lit share as grey.
            float _RGUnderDebug;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            // The forward peak of water's scattering of the sun's light (Henyey & Greenstein 1941, g 0.8),
            // averaging 1 over all directions.
            float SunPhase(float3 dir)
            {
                const float g = 0.8;
                float q = 1.0 + g * g - 2.0 * g * dot(_RGUnderSun.xyz, dir);
                return (1.0 - g * g) / (q * sqrt(q));
            }

            // The veiling light's brightness along dir (averaging 1 over all directions): the sun's share
            // scattered forward (lit: the share of it the shafts let through), the rest (the sky's light)
            // evenly -- the water glows toward the sun, darker away from it.
            float VeilToward(float3 dir, float lit)
            {
                float s = _RGUnderSun.w;
                return (1.0 - s) + s * lit * SunPhase(dir);
            }

            // A cube round the camera, every face at the far plane (as CityReflection's).
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float3 world = _WorldSpaceCameraPos + vertex.xyz * 500.0;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                #if defined(UNITY_REVERSED_Z)
                o.pos.z = 1.0e-6 * o.pos.w;
                #else
                o.pos.z = 0.999999 * o.pos.w;
                #endif
                o.dir = vertex.xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            // The water surface's slope (dh/dx, dh/dz) at xz from the ripple texture, as the game shades
            // still water (WaterGame.cginc's reads with no flow: A at xz x 0.0061, B at (z, x) x 0.0057
            // with its channels swapped, crossfaded on a 2.5 s cycle).
            float2 RippleSlope(float2 xz)
            {
                float phase = _SimulationTime.x * 0.4 + 0.2 * cos(0.06 * xz.x) * cos(0.06 * xz.y);
                float blend = 2.0 * abs(0.5 - frac(phase));
                float4 a = tex2Dlod(_RGRipples, float4(xz * 0.0061, 0.0, 0.0));
                float4 b = tex2Dlod(_RGRipples, float4(xz.yx * 0.0057, 0.0, 0.0));
                float2 n2 = (lerp(float2(a.w, a.y), float2(b.y, b.w), blend) * 2.0 - 1.0) * _RGUnder.z;
                // The game's normal is (n2.x, up, -n2.y).
                return float2(-n2.x, n2.y);
            }

            // Sunlight through the surface onto a point D metres below it, xz: the share it is focused
            // to (1 = even; Watt 1990). Blurred by the sun's disc (0.53 deg) and faded to 1 where the
            // pixel spans the pattern (footprint, m).
            float Caustic(float2 xz, float D, float footprint)
            {
                float2 l = _RGUnderSun.xz / max(_RGUnderSun.y, 0.2);
                float2 s = xz + l * D;
                float h = max(0.2, 0.0046 * D);
                float2 gx0 = RippleSlope(s - float2(h, 0.0)), gx1 = RippleSlope(s + float2(h, 0.0));
                float2 gz0 = RippleSlope(s - float2(0.0, h)), gz1 = RippleSlope(s + float2(0.0, h));
                float k = D * 0.25 / (2.0 * h);
                float jxx = 1.0 + k * (gx1.x - gx0.x), jzz = 1.0 + k * (gz1.y - gz0.y);
                float jxz = k * (gz1.x - gz0.x), jzx = k * (gx1.y - gx0.y);
                float det = abs(jxx * jzz - jxz * jzx);
                float c = min(1.0 / max(det, 0.05), 6.0);
                return lerp(c, 1.0, smoothstep(0.1, 0.4, footprint / h));
            }

            float4 ShadowData(int k)
            {
                return tex2Dlod(_RGSunShadowData, float4((k + 0.5) / 24.0, 0.5, 0.0, 0.0));
            }

            // The sun's cascades, read once a pixel: unity_WorldToShadow[4] by rows, the split spheres
            // and their squared radii.
            struct SunCascades
            {
                float4 rows[16];
                float4 spheres[4];
                float4 sq;
            };

            SunCascades ReadCascades()
            {
                SunCascades s;
                [unroll]
                for (int k = 0; k < 16; k++)
                    s.rows[k] = ShadowData(k);
                [unroll]
                for (int j = 0; j < 4; j++)
                    s.spheres[j] = ShadowData(16 + j);
                s.sq = ShadowData(20);
                return s;
            }

            // Whether the sun reaches the world point p (1 lit, 0 shaded): the game's cascades as its
            // own lighting picks them (the split spheres), lit past the last one.
            float SunLit(float3 p, SunCascades s)
            {
                float4 w;
                float3 f0 = p - s.spheres[0].xyz, f1 = p - s.spheres[1].xyz;
                float3 f2 = p - s.spheres[2].xyz, f3 = p - s.spheres[3].xyz;
                w.x = dot(f0, f0) < s.sq.x ? 1.0 : 0.0;
                w.y = dot(f1, f1) < s.sq.y ? 1.0 : 0.0;
                w.z = dot(f2, f2) < s.sq.z ? 1.0 : 0.0;
                w.w = dot(f3, f3) < s.sq.w ? 1.0 : 0.0;
                w.yzw = saturate(w.yzw - w.xyz);
                float inside = dot(w, float4(1.0, 1.0, 1.0, 1.0));
                float4 q = float4(p, 1.0);
                float4 coord = float4(0.0, 0.0, 0.0, 0.0);
                [unroll]
                for (int c = 0; c < 4; c++)
                    coord += w[c] * float4(dot(s.rows[c * 4], q), dot(s.rows[c * 4 + 1], q), dot(s.rows[c * 4 + 2], q), dot(s.rows[c * 4 + 3], q));
                float lit = UNITY_SAMPLE_SHADOW(_RGSunShadow, coord.xyz);
                return lerp(1.0, saturate(lit), inside);
            }

            // The share of the sun's in-scattered light along the ray from p0 (path m, in the water)
            // that the sunbeams reach: each sample's beam traced up to the surface, tested there. The
            // steps closest together near the eye, where the veil is mostly made (t = reach u^2).
            float ShaftShare(float3 p0, float3 d, float path, float level, float2 pixel)
            {
                // Interleaved gradient noise (Jimenez 2014): each pixel's steps start apart.
                float jitter = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                float reach = min(path, _RGUnderShafts.z);
                int steps = (int)_RGUnderShafts.y;
                float n = max(_RGUnderShafts.y, 1.0);
                float3 up = _RGUnderSun.xyz / max(_RGUnderSun.y, 0.05);
                SunCascades cascades = ReadCascades();
                float lit = 0.0, all = 0.0;
                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= steps)
                        break;
                    float u = (i + jitter) / n;
                    float t = reach * u * u;
                    float3 p = p0 + d * t;
                    float z = max(level - p.y, 0.0);
                    // As this sample adds to the veil: through the water back to the eye, by the light
                    // that reaches its depth (green: the eye's), over its share of the way (dt / du).
                    float w = exp(-_RGUnderC.g * t - _RGUnderK.g * z) * u;
                    float3 entry = p + up * z;
                    entry.y = level + _RGUnderShafts.w;
                    lit += w * SunLit(entry, cascades);
                    all += w;
                }
                return all > 0.0 ? lit / all : 1.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 frame = Finite(tex2D(_RGUnderFrame, uv).rgb);
                float3 d = SafeNormalize(i.dir, float3(0.0, 0.0, 1.0));
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float along = max(dot(d, forward), 1e-4);
                float level = _RGUnder.x;

                // Where the ray starts: the near plane. Above the level, the air's: as drawn.
                float3 cam = _WorldSpaceCameraPos;
                float start = _ProjectionParams.y / along;
                float3 p0 = cam + d * start;
                if (p0.y >= level)
                    return float4(frame, 1.0);

                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float eye = LinearEyeDepth(raw);
                bool sky = eye >= 0.999 * _ProjectionParams.z;
                float tScene = sky ? 1.0e6 : eye / along;
                float tSurface = d.y > 1e-5 ? (level - cam.y) / d.y : 1.0e6;

                float3 c = max(_RGUnderC.rgb, 1e-4);
                float3 K = max(_RGUnderK.rgb, 0.0);
                float t = min(min(tSurface, tScene), 1.0e6);
                float path = max(t - start, 0.0);

                // The shafts: the sun's share of the veil the sunbeams reach along this ray.
                float lit = 1.0;
                if (_RGUnderShafts.x > 0.5 && _RGUnderSun.w > 0.0)
                    lit = ShaftShare(p0, d, path, level, i.pos.xy);
                if (_RGUnderDebug > 0.5)
                    return float4(lit, lit, lit, 1.0);

                float3 end;
                if (tSurface < tScene)
                {
                    // The surface from below.
                    float3 s = cam + d * tSurface;
                    float2 g = RippleSlope(s.xz);
                    float3 n = normalize(float3(-g.x, 1.0, -g.y));
                    float cosI = saturate(dot(d, n));
                    float sinT2 = 1.7769 * (1.0 - cosI * cosI);
                    float3 r = reflect(d, -n);
                    // The water below along the mirror ray (looking down from the level).
                    float3 below = _RGUnderVeil.rgb * VeilToward(r, lit) * c / (c + K * saturate(-r.y));
                    if (sinT2 >= 1.0)
                        end = below;
                    else
                    {
                        float cosT = sqrt(1.0 - sinT2);
                        float rs = (1.333 * cosI - cosT) / (1.333 * cosI + cosT);
                        float rp = (cosI - 1.333 * cosT) / (cosI + 1.333 * cosT);
                        float F = 0.5 * (rs * rs + rp * rp);
                        float3 tilt = mul((float3x3)UNITY_MATRIX_V, float3(n.x, 0.0, n.z));
                        float3 above = Finite(tex2D(_RGUnderFrame, uv + tilt.xy * _RGUnder.w).rgb);
                        end = above * (1.0 - F) + below * F;
                    }
                }
                else
                {
                    // Something under the water: lit by the daylight that reaches its depth.
                    float3 p = cam + d * t;
                    float D = max(level - p.y, 0.0);
                    float light = 1.0;
                    // (No "!sky": Unity 5.6's HLSLcc turns a not of a combined bool into never-false.)
                    if (_RGUnder.y > 0.0 && _RGUnderSun.w > 0.0 && tScene < 1.0e5)
                    {
                        // A pixel's width at this distance (m): the frustum's height over the screen's.
                        float footprint = t * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                        float focus = Caustic(p.xz, D, footprint);
                        light = 1.0 + _RGUnderSun.w * _RGUnder.y * (focus - 1.0);
                    }
                    end = sky ? float3(0.0, 0.0, 0.0) : frame * exp(-K * D) * light;
                }

                // Along the way: the veiling light, the daylight fading with depth as the ray climbs or
                // sinks (the start's depth z0, the ray's rise dy).
                float z0 = max(level - p0.y, 0.0);
                float3 q = max(c - K * d.y, 1e-4);
                float3 veil = _RGUnderVeil.rgb * VeilToward(d, lit) * exp(-K * z0) * c * (1.0 - exp(-q * path)) / q;
                float3 colour = end * exp(-c * path) + veil;
                return float4(Finite(colour), 1.0);
            }
            ENDCG
        }

        // 1 SHADOW DATA (a Blit in the sun's AfterShadowMap: WaterUnder.cs): the light's cascade constants
        // as they stand right after its shadow map, one texel each, 24 wide -- unity_WorldToShadow[4] by
        // rows, unity_ShadowSplitSpheres[4], unity_ShadowSplitSqRadii, _LightShadowData,
        // unity_ShadowFadeCenterAndType, _LightSplitsNear (probed in the 5.6 editor: valid there, the
        // frame unchanged).
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 frag(v2f_img i) : SV_Target
            {
                int k = (int)floor(i.uv.x * 24.0);
                float4 v = float4(0.0, 0.0, 0.0, 0.0);
                if (k < 16)
                    v = unity_WorldToShadow[k / 4][k % 4];
                else if (k < 20)
                    v = unity_ShadowSplitSpheres[k - 16];
                else if (k == 20)
                    v = unity_ShadowSplitSqRadii;
                else if (k == 21)
                    v = _LightShadowData;
                else if (k == 22)
                    v = unity_ShadowFadeCenterAndType;
                else
                    v = _LightSplitsNear;
                return v;
            }
            ENDCG
        }
    }

    Fallback Off
}
