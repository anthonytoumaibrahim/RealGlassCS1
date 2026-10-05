// The water's ripples (RealGlass/Water/WaterRipples.cs and WaterOptics.Ripples have the model and
// its sources): a texture laid in place of the game's _WaterNormal on its two water materials,
// redrawn every frame from a sum of directional sine waves with whole wave numbers across the
// texture (it tiles), on the game's own hour clock. Written as the game decodes its own texture
// (Custom/Water/Default, disassembled 2026-10-05): x from .w, y from .y, each x 2 - 1, then halved
// into the normal's slope; the normal's x is world +x and its y world -z, so a surface of slopes
// (dh/dx, dh/dz) is written as x = -2 dh/dx, y = +2 dh/dz.
Shader "RealGlass/WaterRipples"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Finite.cginc"

    #define RG_RIPPLES 32

    // Row i: the whole wave numbers m, n across the texture, whole cycles per hour, the slope (unit
    // mean square: x the root-mean-square in _RGRipple.x). Row RG_RIPPLES + i: the phase (cycles),
    // the direction's x and z (unit).
    float4 _RGRippleWaves[2 * RG_RIPPLES];

    // x: the root-mean-square slope wanted. y: the texels across the mip level being drawn (0: the
    // base level only, every wave in full, the GPU averaging its mips). z, w: at that level a wave
    // shorter than z texels is left out and one of w texels or more is in full (smoothstep between):
    // each level holds only the waves it can show without aliasing.
    float4 _RGRipple;

    // The game's own clock (SimulationManager.Update): y = the share of its hour gone. It runs with the
    // game's speed and stops while the game is paused.
    float4 _SimulationTime2;

    float4 frag(v2f_img i) : SV_Target
    {
        float2 uv = i.uv;
        float hour = _SimulationTime2.y;
        float2 slope = float2(0.0, 0.0);
        [unroll]
        for (int w = 0; w < RG_RIPPLES; w++)
        {
            float4 wave = _RGRippleWaves[w];
            float4 more = _RGRippleWaves[RG_RIPPLES + w];
            // Cycles, frac before the cosine: m u + n v reaches a few hundred.
            float cycles = frac(dot(wave.xy, uv) - wave.z * hour + more.x);
            float texels = _RGRipple.y / max(length(wave.xy), 1e-3);
            float keep = _RGRipple.y > 0.0 ? smoothstep(_RGRipple.z, _RGRipple.w, texels) : 1.0;
            slope += more.yz * (wave.w * keep * cos(6.2831853 * cycles));
        }
        slope = FiniteSigned(float3(slope * _RGRipple.x, 0.0)).xy;

        float2 m = clamp(float2(-2.0 * slope.x, 2.0 * slope.y), -1.0, 1.0);
        float x = 0.5 + 0.5 * m.x, y = 0.5 + 0.5 * m.y;
        return float4(x, y, 1.0, x);
    }
    ENDCG

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            ENDCG
        }
    }
}
