// A stand-in for the game's water (Custom/Water/Default) in the foam preview, on the simulation's own
// data (WaterGame.cginc: the same surface heights, terrain, flow and depth the game's shader reads, so
// the water lands where the foam layer lands). Its look as PreviewWater.shader's: the game's ripple
// normal from _WaterNormal, carried along the flow exactly as the game's pixel program does
// (WaterGame.cginc's RGGameWaterNormal, which the sky layer shares), plus the surface's own slope; the
// sky as the mod's cube had it (x _SkyGain; 0 under the sky layer); the sun's highlight (exponent 512,
// x _SpecGain: the game's x its material's _SpecColor, which the mod turns off under the sun's glitter).
// The shore: the game's water fades with its thickness along the view, 1 - 10 / (3 t + 10) (its
// clean-water constant). The game draws Blend One Zero and composites thin water itself (its pass
// reads a _BackgroundTexture): here it is an alpha over the terrain, so thin water shows the beach
// through it. Its depth state is the game's (Custom/Water/Default's ForwardBase, read from its
// m_ParsedForm 2026-10-23): ZWrite On, ZTest LEqual, Cull Back, Offset -3, 0 -- what the layers on the
// water are tested against. No pollution, rain rings or foam of the game's own.
Shader "Hidden/RealGlassPreviewGameWater"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _WaterNormal ("", 2D) = "bump" {}
        _Sky ("", Cube) = "" {}
        _BodyColour ("", Color) = (0.03, 0.06, 0.08, 1)
        _SkyGain ("", Float) = 1
        _SpecGain ("", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite On
            ZTest LEqual
            Cull Back
            Offset -3, 0
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "../RealGlass/WaterGame.cginc"
            #include "../RealGlass/WaterCommon.cginc"

            samplerCUBE _Sky;
            float _SkyGain;
            float _SpecGain;
            float4 _BodyColour;
            float4 _PreviewSun;      // xyz: toward the sun; w: its intensity
            float4 _PreviewSunColour;
            float4 _PreviewAmbient;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float water : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            v2f vert(appdata_base v)
            {
                RGGameWaterVertex g = RGGameWaterAt(v.vertex);
                v2f o;
                o.world = g.world;
                o.pos = mul(UNITY_MATRIX_VP, float4(g.world, 1.0));
                o.water = g.waterHeight;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = RGGameSurfaceUV(i.world);
                float4 sa, sb;
                RGGameSurface(uv, sa, sb);
                float depth = i.water - RGGameTerrainPixel(uv);
                float wet = min(depth, sb.y - 0.001);

                float2 uvA;
                float3 n = RGGameWaterNormal(i.world.xz, sa, sb, uvA);

                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                float3 r = reflect(-v, n);
                // The mod's cube until 2026-10-08 (RealGlass/WaterCube): the sky x the rough sea's
                // reflectance by the reflected ray's height (a ray below the level: the grazing value),
                // x the Sky slider; 0 when the sky layer (RealGlass/WaterSky) draws the sky instead.
                float3 sky = texCUBE(_Sky, r).rgb * RGSeaRho(max(r.y, 0.0)) * _SkyGain;
                float3 l = normalize(_PreviewSun.xyz);
                float3 h = normalize(v + l);
                float sun = _PreviewSun.w;
                float3 body = _BodyColour.rgb * (_PreviewAmbient.rgb + _PreviewSunColour.rgb * sun * saturate(dot(n, l)));
                float3 glint = _PreviewSunColour.rgb * sun * pow(saturate(dot(n, h)), 512.0) * _SpecGain;

                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                float thick = max(scene - i.screen.w, 0.0);
                float opacity = 1.0 - 10.0 / (3.0 * thick + 10.0);

                clip(wet);
                return float4(body + sky + glint, opacity);
            }
            ENDCG
        }
    }
}
