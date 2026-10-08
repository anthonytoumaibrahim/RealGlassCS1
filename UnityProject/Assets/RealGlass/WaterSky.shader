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

                float2 uvA;
                float3 n = RGGameWaterNormal(world.xz, sa, sb, uvA);
                // How much of the ripples' slope this pixel averages away: the mip the GPU reads the first
                // copy at, by the pixel's longer footprint (anisotropic filtering keeps the other).
                float texels = max(length(ddx(uvA)), length(ddy(uvA))) * _RGWaterSkyRipples.z;
                float u = saturate((log2(max(texels, 1e-6)) - _RGWaterSkyRipples.x) / max(_RGWaterSkyRipples.y, 0.5));

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
                float spread = 2.0 * sqrt(0.5 * u * max(_RGWaterSkyRipples.w, 0.0));
                float lod = clamp(log2(max(1.0, spread * _RGWaterSky.y * 0.6366198)), 0.0, max(_RGWaterSky.z, 0.0));
                float3 sky = Finite(RGSkyLight(texCUBElod(_RGWaterSkyCube, float4(r, lod)).rgb, r));

                float a = FiniteAlpha(reflectance * max(_RGWaterSky.x, 0.0));
                clip(shown);
                clip(wet);
                return float4(sky * a, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
