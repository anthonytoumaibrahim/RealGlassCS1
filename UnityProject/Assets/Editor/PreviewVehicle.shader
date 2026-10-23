// GPU PREVIEW ONLY (not in the bundle): a stand-in for the game's Custom/Vehicles/Vehicle/Default, so
// the preview's "vanilla" cars look like the game's under the glass. From the game's disassembled final
// pass (2026-10-23): the alpha cut (_ACIMap.r > 0.5), the paint where the colour mask says
// (albedo x _Color where _ACIMap.g is 0), the reflection cube x 0.2 x spec x (albedo + 0.25), and its
// WINDOWS -- spec over 0.8..0.9 in every deck but its top 0.2 m (_FloorParams) -- with the albedo dimmed
// there and a floor seen through them: the view ray met with the deck's floor, a floor texture there,
// added unlit as the game adds it. (The game's floor texture is VehicleProperties.m_floorDiffuse; a
// plain dark floor with rows of seats stands in for it.) And its LAMPS: _ACIMap.b over 0.545 lights by
// _LightState.x at the front (local z >= 0) and .y at the rear, under 0.4545 by .z on the left (local
// x <= 0) and .w on the right, added unlit as albedo x their sum. Lambert through the legacy deferred prepass.
Shader "RealGlass/Preview/Vehicle"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _XYSMap ("Normal XY, specular", 2D) = "gray" {}
        _ACIMap ("Alpha, colour, illumination", 2D) = "black" {}
        _Color ("Paint", Color) = (1, 1, 1, 1)
        _FloorParams ("Floor", Vector) = (0.5, 2.0, 2.5, 5.0)
        _LightState ("Lights: front, rear, left, right", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" }

        CGPROGRAM
        #pragma surface surf Lambert addshadow vertex:vert
        #pragma target 3.5

        sampler2D _MainTex;
        sampler2D _XYSMap;
        sampler2D _ACIMap;
        samplerCUBE _RGSky;
        float4 _Color;
        float4 _FloorParams;
        float4 _LightState;
        float _PreviewNight;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldRefl;
            float3 localPos;
            float3 localCam;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPos = v.vertex.xyz;
            o.localCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float4 c = tex2D(_MainTex, IN.uv_MainTex);
            float4 xys = tex2D(_XYSMap, IN.uv_MainTex);
            float4 aci = tex2D(_ACIMap, IN.uv_MainTex);
            clip(0.5 - aci.r);
            float spec = 1.0 - xys.z;

            // The decks: the first from the floor to the deck height, then every deck height above it.
            float4 F = _FloorParams;
            float y = IN.localPos.y;
            float below = F.y >= y ? 1.0 : 0.0;
            float base = below > 0.5 ? F.x : F.y;
            float period = max(below > 0.5 ? F.y - F.x : F.y, 1e-3);
            float q = (y - base) / period;
            float h = period * (q >= 0.0 ? frac(abs(q)) : -frac(abs(q)));
            float s = saturate((spec - 0.8) * 10.0);
            float glass = s * s * (3.0 - 2.0 * s) * (period - 0.2 >= h ? 1.0 : 0.0);

            float3 albedo = lerp(c.rgb * _Color.rgb, c.rgb, aci.g);
            float k = max(0.0, (1.35 - glass) / 1.35);
            float dim = k * k * (3.0 - 2.0 * k);
            o.Albedo = albedo * dim;

            // The floor through the window: the ray from the camera met with this deck's floor.
            float3 d = IN.localPos - IN.localCam;
            float floorY = y - h;
            float t = d.y < -1e-4 ? (floorY - IN.localCam.y) / d.y : 1e6;
            float3 hitF = IN.localCam + d * t;
            float2 fuv = hitF.xz / F.zw + 0.5;
            float inside = (all(abs(hitF.xz / F.zw) <= 0.5) ? 1.0 : 0.0) * (d.y < 0.0 ? 1.0 : 0.0);
            float seats = step(0.55, frac(fuv.y * 3.0)) * step(0.2, frac(fuv.x * 2.0)) * step(frac(fuv.x * 2.0), 0.8);
            float3 floorC = lerp(float3(0.05, 0.05, 0.055), float3(0.13, 0.12, 0.11), seats) * inside;

            float3 refl = texCUBElod(_RGSky, float4(IN.worldRefl, 8.0 * (1.0 - spec))).rgb * 0.2 * spec * (albedo + 0.25);

            float hi = saturate(2.2 * aci.b - 1.2), lo = saturate(1.0 - 2.2 * aci.b);
            float front = IN.localPos.z >= 0.0 ? 1.0 : 0.0, left = IN.localPos.x <= 0.0 ? 1.0 : 0.0;
            float lamps = hi * (front * _LightState.x + (1.0 - front) * _LightState.y) +
                          lo * (left * _LightState.z + (1.0 - left) * _LightState.w);
            o.Emission = refl * (1.0 - 0.7 * _PreviewNight) + floorC * glass + albedo * lamps;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/VertexLit"
}
