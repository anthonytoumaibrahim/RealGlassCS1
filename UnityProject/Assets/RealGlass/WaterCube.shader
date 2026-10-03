// The sky the game's water reflects, with real water's reflectance (RealGlass/Water/WaterSurface.cs;
// ported from RealLight's SkyCube, 2026-10-03). Drawn as a hidden camera's skybox into a small cube
// (Camera.RenderToCubemap) that takes the place of the theme's _EnvironmentCubemap on the game's two
// water materials only. The game's water shader adds that cube x 0.5 at every angle, lit by the sun
// and the ambient (no Fresnel: a white cubemap gives white water). So the cube carries the law
// itself: along a reflected direction d the THEME'S OWN cube x rho(max(d.y, 0)) (2% looking down,
// about half at grazing; a ray below the level meets the next wave: the grazing value) x the gain,
// which divides out the shader's 0.5 and carries the Sky slider. The game's lighting is kept.
Shader "RealGlass/WaterCube"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }

        Pass
        {
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "WaterCommon.cginc"

            // The theme's cube, as the game set it (the global _EnvironmentCubemap).
            samplerCUBE _RGWaterTheme;

            // x: the Sky slider / 0.5; y: 1 when there is a theme's cube; z: the theme cube's mip
            // whose texels are this cube's.
            float4 _RGWaterGain;

            // rgb: the light the game's water lights its cube with (linear: the sun on the level +
            // the sky's ambient, floored). A real sky is light already: it is divided by this, so
            // that the game's lighting gives it back.
            float4 _RGWaterLight;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = vertex.xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 theme = _RGWaterGain.y > 0.5 ? texCUBElod(_RGWaterTheme, float4(d, _RGWaterGain.z)).rgb : float3(0.0, 0.0, 0.0);
                // In light (the theme's cube as the game lights it), then back to what the game lights.
                float3 sky = RGSkyLight(theme * _RGWaterLight.rgb, d) / _RGWaterLight.rgb;
                float rho = RGSeaRho(max(d.y, 0.0));
                return float4(sky * (rho * _RGWaterGain.x), 1.0);
            }
            ENDCG
        }
    }
}
