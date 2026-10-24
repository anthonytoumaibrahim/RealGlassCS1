// Foam on the map's water (RealGlass/Water/WaterFoam.cs has the model and its sources). Drawn on the
// game's own water meshes, right after each of the game's water draws, with the game's property
// block (WaterGame.cginc): the vertices land where the game's water is, and every input is the
// game's simulation as the game's own shader reads it. The game's water is drawn untouched first;
// this only adds white, nothing anywhere else.
//
// HOW MUCH FOAM a point has (0..1, the share of it that is white), from three sources, combined as
// independent chances (1 - (1 - a)(1 - b)(1 - c)):
//   SHORE   where the water is shallow at the sea's level: within the shore depth of the waterline
//           (Crest's shoreline foam; War Thunder's shore foam), the band breathing in and out as
//           waves run up the beach (a 10 s cycle, its phase drifting along the shore).
//   FAST    where the flow is fast (rapids, weirs, dam outflows), more where the surface is steep.
//   FRONTS  where the water is rising fast: the front of a tsunami, a flood or a dam break
//           (Chentanez & Mueller 2010's breaking test on a height field: rising fast and steep).
// WHERE it is white: the foam pattern (FoamPattern.cs: 0.6 patches + 0.4 lace, each uniform on
// 0..1), thresholded at the level that leaves that share above it; carried along the flow in two
// phases half a cycle apart and crossfaded (Vlachos 2010, the game's own way with its ripples).
// FILTERED so it does not sparkle: where a pixel spans more than the pattern's features, the
// threshold turns into a ramp as wide as the variation averaged away, and far away the pixel shows
// exactly the share of white it should (the amount itself).
// LIT as a rough white surface: Koepke 1984's reflectance of dense foam, the sun or moon (with the
// game's shadows) and the sky's ambient.
Shader "RealGlass/WaterFoam"
{
    // _MainTex is the game's (the block sets it per patch); declared so that its _MainTex_ST is the
    // identity the game's own material has, not 0.
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _RGFoamPattern ("", 2D) = "gray" {}
    }

    SubShader
    {
        // Right after the game's water (Geometry+1) and the sky on it (RealGlass/WaterSky, Geometry+2):
        // foam is on top of the water, reflecting nothing; before the glass and the game's fog.
        Tags { "Queue" = "Geometry+3" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            // The game's own test (Custom/Water/Default: ZTest LEqual, Cull Back, Offset -3, 0, and it
            // writes depth): foam on the nearest water only, drawn as the sky layer is -- 2 cm nearer
            // along each eye ray (RGGameLayerClip) and one more unit of slope than the game's, against
            // the driver's rounding (WaterSky.shader has the why); the 2 units stay for OpenGL's
            // fixed-point depth. (The first in-game run, at plain LEqual, showed
            // only the strip the game's water leaves out: its Offset -3 puts it in front. Until
            // 2026-10-23: ZTest Always + a hand test against the depth texture, which has no water -- a
            // tsunami's far side showed through.)
            ZTest LEqual
            Offset -4, -2
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "Finite.cginc"
            #include "WaterGame.cginc"

            sampler2D _RGFoamPattern;

            // x: the lace's repeats a metre, y: the patches' repeats a metre, z: the pattern's texels a
            // side, w: -
            float4 _RGFoamScale;
            // x: the lace's features and y: the patches' (texels; log2), z, w: -
            float4 _RGFoamFeature;
            // Shore foam. x: its amount at the waterline (0 = none), y: the depth it reaches (m),
            // z: the sea's level (m), w: how far from it the water still counts as the sea (m)
            float4 _RGFoamShore;
            // Fast water. x: its amount, y: the flow speed it starts at, z: where it is full (the game's
            // units), w: how much a steep surface adds (x its slope)
            float4 _RGFoamFast;
            // Fronts. x: their amount, y: the rise a water frame it starts at, z: where it is full (m)
            float4 _RGFoamFront;
            // x: the foam's reflectance, y: the flow's speed on the water at 1 (m/s), z: the advection's
            // cycle (s), w: the strength of it all (0..)
            float4 _RGFoamLook;
            // A diagnostic (the hidden WaterFoamView row; 0 normally): x = 1 shows each source's amount
            // as a colour (red shore, green fast water, blue fronts) on every wet pixel; 2 magenta on
            // every pixel drawn (past the depth test), before the game's no-water test; 3 the same
            // after it.
            float4 _RGFoamDebug;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                // x: the water's height (m; the game's pixel program compares the terrain with it),
                // y: its rise from the older frame to the newer (m)
                float2 water : TEXCOORD1;
                SHADOW_COORDS(2)
            };

            v2f vert(appdata_base v)
            {
                RGGameWaterVertex g = RGGameWaterAt(v.vertex);
                v2f o;
                o.world = g.world;
                o.pos = RGGameLayerClip(g.world);
                o.water = float2(g.waterHeight * _MainTex_ST.x + _MainTex_ST.z, g.heights.y - g.heights.x);
                TRANSFER_SHADOW(o);
                return o;
            }

            // The patches' and the lace's mix, and the threshold that leaves a share a of it above:
            // the inverse of the distribution of A U1 + B U2 (U1, U2 uniform on 0..1), a trapezoid.
            #define RG_A 0.6
            #define RG_B 0.4

            float FoamThreshold(float a)
            {
                float p = 1.0 - saturate(a);
                float lo = RG_B / (2.0 * RG_A);
                if (p < lo)
                    return sqrt(2.0 * RG_A * RG_B * p);
                if (p > 1.0 - lo)
                    return 1.0 - sqrt(2.0 * RG_A * RG_B * (1.0 - p));
                return RG_A * p + 0.5 * RG_B;
            }

            // How far a channel is averaged away at this pixel: 0 while a texel is no bigger than the
            // pixel, 1 once the pixel spans a whole feature (footprint and feature in texels, log2).
            float Averaged(float2 uv, float feature)
            {
                float2 dx = ddx(uv) * _RGFoamScale.z, dy = ddy(uv) * _RGFoamScale.z;
                float texels = max(sqrt(max(dot(dx, dx), dot(dy, dy))), 1.0);
                return saturate(log2(texels) / max(feature, 1.0));
            }

            // The white share at one phase: the pattern at xz against the threshold for amount a.
            float Coverage(float2 xz, float a, float t)
            {
                float2 uvLace = xz * _RGFoamScale.x;
                float2 uvPatch = xz * _RGFoamScale.y;
                float lace = tex2D(_RGFoamPattern, uvLace).r;
                float patch = tex2D(_RGFoamPattern, uvPatch).g;
                float rLace = Averaged(uvLace, _RGFoamFeature.x);
                float rPatch = Averaged(uvPatch, _RGFoamFeature.y);

                float field = RG_A * patch + RG_B * lace;
                // Where the lace is averaged away its share of the field is spread evenly over 0..0.4
                // round the patches' value: the share above t is then a ramp half that wide -- exact
                // once the lace is fully averaged. Plus the pixel's own change across it (the glass's
                // anti-aliased threshold, 0.75 fwidth). Where the patches too are averaged away, the
                // amount itself.
                float e = 0.5 * RG_B * rLace + 0.75 * fwidth(field) + 1e-3;
                float c = saturate((field - t + e) / (2.0 * e));
                return lerp(c, a, rPatch);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 world = i.world;
                float2 uv = RGGameSurfaceUV(world);
                float4 sa, sb;
                RGGameSurface(uv, sa, sb);

                // The game's own test for water here (the terrain under the surface, some depth); the
                // pixel is dropped at the very end, after every read that needs its neighbours.
                float depth = i.water.x - RGGameTerrainPixel(uv);
                float wet = min(depth, sb.y - 0.001);

                float2 flow = sb.xz * 2.0 - 1.0;
                float speed = length(flow);
                float2 tilt = sa.xz * 2.0 - 1.0;
                float steep = length(tilt);
                float clock = _SimulationTime.x;

                // SHORE: at the sea's level, within the (breathing) shore depth of the waterline.
                float sea = 1.0 - smoothstep(_RGFoamShore.w, 2.0 * _RGFoamShore.w, abs(i.water.x - _RGFoamShore.z));
                float breathe = 0.7 + 0.3 * sin(6.2831853 * clock / 10.0 + 0.031 * world.x + 0.023 * world.z);
                float reach = max(_RGFoamShore.y * breathe, 0.01);
                float shore = _RGFoamShore.x * sea * (1.0 - smoothstep(0.0, reach, depth));
                // Broken waves: lines of white along the depth's contours (parallel to the beach on a
                // shelving shore), running in toward the waterline once a cycle -- the shore waves'
                // phase taken from the depth, as War Thunder takes it from the distance to the shore.
                // The swash at the very edge stays white.
                float lines = 0.5 + 0.5 * cos(6.2831853 * (2.0 * depth / max(_RGFoamShore.y, 0.01) + clock / 10.0));
                float broken = lerp(0.35, 1.0, smoothstep(0.3, 0.9, lines));
                shore *= max(broken, 1.0 - smoothstep(0.0, 0.3 * reach, depth));

                // FAST: the flow's speed, the steeper the surface the sooner.
                float fast = _RGFoamFast.x * smoothstep(_RGFoamFast.y, _RGFoamFast.z, speed * (1.0 + _RGFoamFast.w * steep));

                // FRONTS: rising fast.
                float front = _RGFoamFront.x * smoothstep(_RGFoamFront.y, _RGFoamFront.z, i.water.y);

                float a = 1.0 - (1.0 - saturate(shore)) * (1.0 - saturate(fast)) * (1.0 - saturate(front));
                a = saturate(a * _RGFoamLook.w);

                // Carried along the flow: two phases half a cycle apart, each reset when the other is
                // whole (Vlachos 2010), with a slow offset across the map so the resets do not pulse
                // in step (the game's own term).
                float cycle = max(_RGFoamLook.z, 0.5);
                float phase = clock / cycle + 0.2 * cos(0.06 * world.x) * cos(0.06 * world.z);
                float f0 = frac(phase), f1 = frac(phase + 0.5);
                float2 reachFlow = flow * _RGFoamLook.y * cycle;
                float t = FoamThreshold(a);
                float c0 = Coverage(world.xz - reachFlow * (f0 - 0.5), a, t);
                float c1 = Coverage(world.xz - reachFlow * (f1 - 0.5) + float2(17.3, 29.1), a, t);
                float white = lerp(c0, c1, 2.0 * abs(0.5 - f0));
                // Sparse foam as grey, not lone white specks (they would only sparkle): no pixel more
                // than four times the share asked for; and a trace of foam is none.
                white = min(white, 4.0 * a) * smoothstep(0.01, 0.05, a);

                // A rough white surface on the water's large-scale slope.
                float3 n = normalize(float3(tilt.x, sqrt(max(1.0 - dot(tilt, tilt), 0.04)), tilt.y));
                float lit = saturate(dot(n, _WorldSpaceLightPos0.xyz)) * SHADOW_ATTENUATION(i);
                float3 light = _LightColor0.rgb * lit + ShadeSH9(float4(n, 1.0));
                float3 colour = Finite(_RGFoamLook.x * light);
                if (_RGFoamDebug.x > 1.5 && _RGFoamDebug.x < 2.5)
                    return float4(1.0, 0.0, 1.0, 0.85);
                clip(wet);
                if (_RGFoamDebug.x > 2.5)
                    return float4(1.0, 0.0, 1.0, 0.85);
                if (_RGFoamDebug.x > 0.5)
                    return float4(saturate(float3(shore, fast, front)), 0.85);
                return float4(colour, a > 0.0 ? FiniteAlpha(white) : 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
