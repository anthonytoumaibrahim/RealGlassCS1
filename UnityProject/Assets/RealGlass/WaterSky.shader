// The sky in the map's water, as real water reflects it (RealGlass/Water/WaterSkyLayer.cs has the
// model and its sources; the author, 2026-10-08, after a Battlefield 6 screenshot of Lake Mead:
// "that's EXACTLY how we want ours to look"). Drawn on the game's own water meshes right after each of
// the game's water draws, with the game's per-patch property block (WaterGame.cginc), as the foam is;
// meanwhile the game's own water reflects no sky (its cube is black while this layer draws:
// WaterSurface).
//
// WHY A LAYER: the game reads its sky cube along each ripple's reflected ray and adds it with no
// Fresnel of its own, so a cube can only carry a reflectance by that ray's height -- which counts each
// ripple's tilt twice (for a view 35 deg down and a facet tilted a toward the eye, the true incidence is
// 55 - a deg, the cube's 55 - 2a): rippled water came out about twice too bright, with the horizon's
// sky in it. Here every pixel knows the eye and its ripple:
//   NORMAL    the game's own (RGGameWaterNormal: its ripples carried along the flow, + the surface's slope).
//   HOW MUCH  Schlick's Fresnel for water on that facet; where the ripple texture's mips have averaged
//             ripples away (a share u of their slopes, from the mip the GPU reads), the rough sea's
//             reflectance (Cox & Munk's slopes: _RGSeaTable) on the filtered normal instead -- what
//             filtering takes from the normals is roughness, not slope (Bruneton, Neyret & Holzschuch
//             2010, "Real-time Realistic Ocean Lighting using Seamless Transitions from Geometry to BRDF").
//   OF WHAT   the sky the player sees: a live cube of the game's skybox, Volumetric Weather's clouds in
//             it (the glass's GlassSky; _RGWaterSkyCube), read along the facet's mirror ray (a ray turned
//             below the level meets the next wave: mirrored back up), as blurred as the averaged-away
//             slopes spread it; towards a published real sky by its weight (RGSkyLight).
//   ONTO      Blend One OneMinusSrcAlpha, premultiplied: the water as the game drew it x (1 - F) + the
//             sky x F -- light that is reflected is not also let through. Queue Geometry+2, before the
//             game's fog: far water hazes as the game's own water does.
//   THE SUN'S GLITTER (2026-10-17, with the FFT waves: WaterSpectrum.cs): the sun's reflection off a
//             sea of facets whose slopes are Gaussian about the ripple texture's own (LEAN mapping,
//             Olano & Baker 2010: the mean from the filtered normal, the covariance from what the
//             mips averaged away -- WaterSpectrum.Table, by the mip the GPU reads each of the game's
//             two reads at -- plus the waves too short for the texture). The microfacet sum for a
//             distant light, E F p(slope of h) / (4 cos v cos^4 h), Smith's masking for Gaussian
//             slopes (Bruneton, Neyret & Holzschuch 2010, after Walter et al. 2007): far away the
//             averaged-away slopes widen it into the glitter path toward a low sun; close up the
//             texture's own facets catch it. Added over the water; the game's own highlight (exponent
//             512 on its normal, x the material's _SpecColor) is turned off meanwhile.
Shader "RealGlass/WaterSky"
{
    // _MainTex is the game's (the block sets it per patch); declared so that its _MainTex_ST is the
    // identity the game's own material has, not 0.
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _WaterNormal ("", 2D) = "bump" {}
    }

    SubShader
    {
        // Right after the game's water (Geometry+1), before the foam (Geometry+3), the glass and the fog.
        Tags { "Queue" = "Geometry+2" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite Off
            // As the foam: the game's water lies just in front of the depth buffer's value here, so
            // what hides the water is tested by hand against the depth texture (terrain, buildings).
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Finite.cginc"
            #include "WaterGame.cginc"
            #include "WaterCommon.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            // The sky as the player sees it: a live cube of the skybox (WaterSkyLayer, a global).
            samplerCUBE _RGWaterSkyCube;

            // x: the Sky slider (1 = real water); y: the cube's size (texels a face); z: its last mip.
            float4 _RGWaterSky;

            // The ripples' averaging (WaterOptics.SkyRippleMips). x: the mip of the ripple texture where
            // the shortest wave is averaged away, y: the mips from there to the longest, z: the ripple
            // texture's texels a side, w: the ripples' mean square slope at full detail (both directions).
            float4 _RGWaterSkyRipples;

            // The FFT waves' averaging (WaterSpectrum.Table at the ripples' strength, in the texture's
            // slope units): entry e = mip e - 1 of the ripple texture, the slopes averaged away (xx, zz,
            // xz) and their share of all the texture holds.
            float4 _RGWaterSkyWaves[12];

            // xyz: the slopes of the waves too short for the texture (xx, zz, xz, same units); w: 1 =
            // the table above is in use, 0 = the 32 waves' averaging (_RGWaterSkyRipples) as before.
            float4 _RGWaterSkyTail;

            // The sun's glitter. xyz: toward the sun (unit); w: its strength (1 = real water, 0 = none).
            float4 _RGWaterSun;

            // The sun's light, linear (the game's light: colour x intensity).
            float4 _RGWaterSunColour;

            // The mip (fractional, -1 = magnified) the GPU reads a ripple texture of _RGWaterSkyRipples.z
            // texels at: anisotropic filtering (4 x) takes the longer footprint over up to 4 taps, so the
            // mip is the shorter axis's, or a quarter of the longer; the box it averages over is the
            // footprint itself -- taken here as one square of the same area.
            float RippleMip(float2 uv)
            {
                float2 dx = ddx(uv) * _RGWaterSkyRipples.z, dy = ddy(uv) * _RGWaterSkyRipples.z;
                float major = max(length(dx), length(dy)), minor = min(length(dx), length(dy));
                return 0.5 * log2(max(major * max(major * 0.25, minor), 1e-12));
            }

            float4 WavesAt(float mip)
            {
                float e = clamp(mip + 1.0, 0.0, 11.0);
                float i = floor(e);
                float4 lo = _RGWaterSkyWaves[(int)i];
                float4 hi = _RGWaterSkyWaves[(int)min(i + 1.0, 11.0)];
                return lerp(lo, hi, e - i);
            }

            // Smith's Lambda for Gaussian slopes of covariance s (xx, zz, xz), seen from w (unit, up y):
            // (e^(-a^2) / (a sqrt(pi)) - erfc(a)) / 2, a = cos / (sqrt(2 s_w) sin), s_w the slopes'
            // variance along w's azimuth; erfc by Abramowitz & Stegun 7.1.26.
            float SmithLambda(float3 w, float3 s)
            {
                float2 az = w.xz;
                float across = max(dot(az, az), 1e-8);
                float sw = (az.x * az.x * s.x + 2.0 * az.x * az.y * s.z + az.y * az.y * s.y) / across;
                float a = max(w.y, 1e-4) / sqrt(2.0 * max(sw, 1e-8) * across);
                float t = 1.0 / (1.0 + 0.3275911 * a);
                float erfc = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * exp(-a * a);
                return max(0.0, 0.5 * (exp(-a * a) / (a * 1.7724539) - erfc));
            }

            // The sun's light off the facets about the normal n (the texture's, filtered) whose slopes
            // spread by s, toward the eye v.
            float3 SunGlitter(float3 v, float3 n, float3 s)
            {
                float3 l = _RGWaterSun.xyz;
                float3 h = SafeNormalize(v + l, float3(0.0, 1.0, 0.0));
                float zv = max(v.y, 1e-3), zh = max(h.y, 1e-3), zn = max(n.y, 1e-3);
                float2 d = float2(-h.x, -h.z) / zh - float2(-n.x, -n.z) / zn;
                float sxx = s.x + 1e-5, szz = s.y + 1e-5, sxz = s.z;
                float det = max(sxx * szz - sxz * sxz, 1e-12);
                float q = (szz * d.x * d.x - 2.0 * sxz * d.x * d.y + sxx * d.y * d.y) / det;
                float p = exp(-0.5 * q) / (6.2831853 * sqrt(det));
                float c = 1.0 - saturate(dot(v, h));
                float c2 = c * c;
                float fresnel = 0.0204 + 0.9796 * c2 * c2 * c;
                float3 sd = float3(sxx, szz, sxz);
                float masking = 1.0 + SmithLambda(v, sd) + SmithLambda(l, sd);
                float lit = l.y > 0.0 ? 1.0 : 0.0;
                // x pi: the game's light is Unity's (a white Lambert surface shows it at its colour).
                return _RGWaterSunColour.rgb * (lit * 3.14159265 * fresnel * p / (4.0 * zv * zh * zh * zh * zh * masking));
            }

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                // The water's height (m): the game's pixel program compares the terrain with it.
                float water : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                RGGameWaterVertex g = RGGameWaterAt(v.vertex);
                v2f o;
                o.world = g.world;
                o.pos = mul(UNITY_MATRIX_VP, float4(g.world, 1.0));
                o.water = g.waterHeight * _MainTex_ST.x + _MainTex_ST.z;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 world = i.world;
                float2 uv = RGGameSurfaceUV(world);
                float4 sa, sb;
                RGGameSurface(uv, sa, sb);

                // The game's own test for water here; dropped at the very end, after every read that
                // needs its neighbours.
                float depth = i.water - RGGameTerrainPixel(uv);
                float wet = min(depth, sb.y - 0.001);
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                float own = i.screen.w;
                float shown = scene + 0.5 + 0.002 * own - own;

                RGGameRippleReads reads = RGGameRipplePlaces(world.xz, sb);
                float3 n = RGGameWaterNormalAt(reads, sa);
                // The slopes this pixel averages away (shaded: x (speed + 0.5) as the game shades the
                // texture's), and their share of all the ripples' slope.
                float u;
                float3 averaged;
                float gain = reads.speed + 0.5;
                if (_RGWaterSkyTail.w > 0.5)
                {
                    // The FFT waves: each of the game's two reads at its own mip, B transposed (its slopes
                    // are the texture's swapped and negated), weighted as the game crossfades them.
                    float4 wa = WavesAt(RippleMip(reads.uvA));
                    float4 wb = WavesAt(RippleMip(reads.uvB));
                    float ka = 1.0 - reads.blend, kb = reads.blend;
                    averaged = gain * gain * (ka * ka * wa.xyz + kb * kb * wb.yxz + _RGWaterSkyTail.xyz);
                    u = lerp(wa.w, wb.w, reads.blend);
                }
                else
                {
                    // The 32 waves: how much of their slope the mip the GPU reads the first copy at has
                    // averaged away, by the pixel's longer footprint (anisotropic filtering keeps the other).
                    float texels = max(length(ddx(reads.uvA)), length(ddy(reads.uvA))) * _RGWaterSkyRipples.z;
                    u = saturate((log2(max(texels, 1e-6)) - _RGWaterSkyRipples.x) / max(_RGWaterSkyRipples.y, 0.5));
                    float each = 0.5 * u * max(_RGWaterSkyRipples.w, 0.0);
                    averaged = float3(each, each, 0.0);
                }

                float3 v = SafeNormalize(_WorldSpaceCameraPos - world, float3(0.0, 1.0, 0.0));
                float cosI = saturate(dot(v, n));
                float c = 1.0 - cosI;
                float c2 = c * c;
                float schlick = 0.0204 + 0.9796 * c2 * c2 * c;
                float reflectance = lerp(schlick, RGSeaRho(cosI), u);

                float3 r = reflect(-v, n);
                r.y = abs(r.y);
                // The averaged-away slopes spread the reflected rays by 2 sigma in each plane: as wide in
                // the cube's texels (a face spans pi / 2).
                float spread = 2.0 * sqrt(0.5 * max(averaged.x + averaged.y, 0.0));
                float lod = clamp(log2(max(1.0, spread * _RGWaterSky.y * 0.6366198)), 0.0, max(_RGWaterSky.z, 0.0));
                float3 sky = Finite(RGSkyLight(texCUBElod(_RGWaterSkyCube, float4(r, lod)).rgb, r));

                float a = FiniteAlpha(reflectance * max(_RGWaterSky.x, 0.0));
                float3 glitter = _RGWaterSun.w > 0.0 ? Finite(SunGlitter(v, n, averaged) * _RGWaterSun.w) : float3(0.0, 0.0, 0.0);
                clip(shown);
                clip(wet);
                return float4(sky * a + glitter, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
