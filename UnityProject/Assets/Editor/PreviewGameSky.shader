// A stand-in, in the foam preview (-GameSky), for the game's own skybox (Hidden/DayNight/Skybox), read
// from its OpenGL Core program (its names kept there): the sky's scattering over a path of 8 km /
// (dir.y + 0.06), the sun's disc, the night's horizon -- without the moon and the stars. It reads the
// same globals the game sets (DayNightProperties.UpdateSharedAtmosphericProperties); FoamPreview sets
// them as the game would for the preview's sun, with the inputs Volumetric Weather logged from the
// game (2026-10-30: Rayleigh 1, 680/550/440 nm, the colour correction of a linear game without the
// sky's tone mapping, (1, 2)).
Shader "Hidden/RealGlassPreviewGameSky"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float2 _ColorCorrection;
            float3 _BetaR, _BetaM, _MieConst, _MiePhase_g, _SunDir;
            float4 _SkyMultiplier, _NightHorizonColor, _NightZenithColor;
            float _SunSize;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float4 path : TEXCOORD1;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                float3 d = normalize(mul((float3x3)unity_ObjectToWorld, vertex.xyz));
                o.dir = d;
                o.path = float4(max(d.y + 0.06, 0.06).xxx, max(d.y, 0.0));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float mu = dot(d, _SunDir);
                float3 r1 = 8.0 / i.path.xyz;
                float3 r2 = 1.2 / i.path.xyz;
                float3 a = r1 * _NightZenithColor.xyz * (2.0 - _NightZenithColor.xyz * r1);
                float3 t = exp(-(_BetaR * r1 + _BetaM * r2));
                float3 s = a * t + _SkyMultiplier.x * (1.0 - t - t * a);
                float3 mie = r2 * s / max(s.x, 1.0e-6) * _MieConst;
                float phase = _MiePhase_g.x * pow(max(_MiePhase_g.y - _MiePhase_g.z * mu, 1.0e-4), -1.5);
                float3 light = (s * 0.75 + phase * mie) * ((mu * mu + 1.0) * _SkyMultiplier.y);
                float disc = min(pow(max((1.0 - mu) * _SunSize, 1.0e-6), -1.5), 1000.0);
                if (_SunDir.y > -0.1)
                    light += min(mie, i.path.www) * disc * t;
                if (_SunDir.y < 0.25)
                    light += _NightHorizonColor.xyz * a;
                float3 c = pow(max((1.0 - exp(-light)) * _ColorCorrection.x, 0.0), _ColorCorrection.y);
                return float4(c, 1.0);
            }
            ENDCG
        }
    }
}
