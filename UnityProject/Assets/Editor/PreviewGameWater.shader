// A stand-in for the game's water (Custom/Water/Default) in the foam preview, on the simulation's own
// data (WaterGame.cginc: the same surface heights, terrain, flow and depth the game's shader reads, so
// the water lands where the foam layer lands). Its look as PreviewWater.shader's: the game's ripple
// normal from _WaterNormal, here carried along the flow exactly as the game's pixel program does
// (two phases on a 2.5 s cycle, 0.14 of a tile, its strength x (0.5 + the flow's speed)), plus the
// surface's own slope; the sky x real water's reflectance; the sun's highlight (exponent 512).
// The shore: the game's water fades with its thickness along the view, 1 - 10 / (3 t + 10) (its
// clean-water constant). How the game composites that is not known (its blend state is not in the
// player's data): here it is an alpha over the terrain, so thin water shows the beach through it.
// No pollution, rain rings or foam of the game's own.
Shader "Hidden/RealGlassPreviewGameWater"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _WaterNormal ("", 2D) = "bump" {}
        _Sky ("", Cube) = "" {}
        _BodyColour ("", Color) = (0.03, 0.06, 0.08, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "../RealGlass/WaterGame.cginc"

            sampler2D _WaterNormal;
            samplerCUBE _Sky;
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

            float Rho(float up)
            {
                float c = 1.0 - saturate(up);
                return 0.0204 + 0.9796 * c * c * c * c * c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = RGGameSurfaceUV(i.world);
                float4 sa, sb;
                RGGameSurface(uv, sa, sb);
                float depth = i.water - RGGameTerrainPixel(uv);
                float wet = min(depth, sb.y - 0.001);

                float2 flow = sb.xz * 2.0 - 1.0;
                float speed = max(abs(flow.x), abs(flow.y));
                float2 xz = i.world.xz;
                float phase = _SimulationTime.x * 0.4 + 0.2 * cos(0.06 * xz.x) * cos(0.06 * xz.y);
                float f = frac(phase);
                float g = frac(phase + 0.5) - 0.5;
                float4 a = tex2D(_WaterNormal, xz * 0.0061 - 0.14 * (f - 0.5) * flow);
                float4 b = tex2D(_WaterNormal, xz.yx * 0.0057 - 0.14 * g * flow.yx);
                float blend = 2.0 * abs(0.5 - f);
                float2 ripple = lerp(float2(a.w, a.y), float2(b.y, b.w), blend) * 2.0 - 1.0;
                float2 tilt = sa.xz * 2.0 - 1.0;
                float2 n2 = ripple * 0.5 * (speed + 0.5) + float2(tilt.x, -tilt.y);
                n2 /= max(length(n2), 1.0);
                float3 nt = float3(n2, sqrt(max(1.0 - dot(n2, n2), 0.0)));
                float3 n = normalize(float3(nt.x, nt.z, -nt.y));

                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                float3 r = reflect(-v, n);
                float3 sky = texCUBE(_Sky, r).rgb * Rho(abs(r.y));
                float3 l = normalize(_PreviewSun.xyz);
                float3 h = normalize(v + l);
                float sun = _PreviewSun.w;
                float3 body = _BodyColour.rgb * (_PreviewAmbient.rgb + _PreviewSunColour.rgb * sun * saturate(dot(n, l)));
                float3 glint = _PreviewSunColour.rgb * sun * pow(saturate(dot(n, h)), 512.0) * 0.6;

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
