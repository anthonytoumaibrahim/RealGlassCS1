// GPU PREVIEW ONLY (not in the bundle): a stand-in for the game's Custom/Buildings/Building/Default,
// so the preview's "vanilla" looks like the game's buildings under the glass. From the game's
// disassembled final pass: the albedo lit by the light buffer, plus its only reflection --
// cube x 0.2 x spec x (albedo + 0.25), spec = 1 - _XYSMap.z, no Fresnel -- plus, at night, the
// windows the illumination channel (_ACIMap.b) marks, lit when their value passes the evening's
// threshold. Lambert through the legacy deferred prepass, as the game's buildings are.
Shader "RealGlass/Preview/Building"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _XYSMap ("Normal XY, specular", 2D) = "gray" {}
        _ACIMap ("Alpha, colour, illumination", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma target 3.5

        sampler2D _MainTex;
        sampler2D _XYSMap;
        sampler2D _ACIMap;
        samplerCUBE _RGSky;
        float _PreviewNight;        // 0 day .. 1 night
        float _PreviewLitShare;     // windows lit at night (their _ACIMap.b above 1 - share)

        struct Input
        {
            float2 uv_MainTex;
            float3 worldRefl;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            float4 c = tex2D(_MainTex, IN.uv_MainTex);
            float4 xys = tex2D(_XYSMap, IN.uv_MainTex);
            float4 aci = tex2D(_ACIMap, IN.uv_MainTex);
            float spec = 1.0 - xys.z;
            o.Albedo = c.rgb;
            float3 refl = texCUBElod(_RGSky, float4(IN.worldRefl, 8.0 * (1.0 - spec))).rgb * 0.2 * spec * (c.rgb + 0.25);
            float lit = step(1.0 - _PreviewLitShare, aci.b) * step(0.48, aci.b);
            float3 lamp = float3(1.0, 0.80, 0.56) * 1.8 * (0.6 + 0.4 * aci.b);
            o.Emission = refl * (1.0 - 0.7 * _PreviewNight) + lamp * lit * _PreviewNight;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/VertexLit"
}
