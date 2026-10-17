// The ripples as an ocean's waves (RealGlass/Water/WaterSpectrum.cs has the model and its sources,
// WaterWaves.cs the steps): an inverse FFT of a wind-sea spectrum per cascade, by pixel passes (no
// compute shaders: Unity 5.6 has none on a Mac's OpenGL), composed into the ripple texture the game
// shades its water with -- written exactly as RealGlass/WaterRipples writes it (x from .w, y from .y,
// x 2 - 1, halved into the normal's slope: a surface of slopes (dh/dx, dh/dz) is x = -2 dh/dx,
// y = +2 dh/dz), so the game, the sky layer and the city reflection read it as they read the 32 waves.
//
// Pass 0 SPECTRUM: this hour's spectrum of the fields, two complex spectra a texel (each field's real
//        output packed as the real or imaginary part: a real field's spectrum is Hermitian).
// Pass 1 FFT: one radix-2 step of the inverse transform along u or v (Stockham, self-sorting).
// Pass 2 COMPOSE: both cascades into the ripple texture, the slopes of the choppy surface.
Shader "RealGlass/WaterWaves"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Finite.cginc"
    #include "FullScreen.cginc"   // vert_full / v2f_full: vert_img with a float uv (Apple GPUs honour half)

    #define RG_TAU 6.2831853

    // The FFT step's input (Graphics.Blit's source): float, point, read at texel centres only.
    sampler2D _MainTex;

    // The cascade's spectrum (WaterSpectrum.H0): h0(k) (real, imaginary) at a unit mean-square slope,
    // whole cycles per hour, 0; texel (i, j) = the wave numbers (i, j), from Size / 2 on less Size.
    sampler2D _RGWaveH0;

    // x: texels a side (N); y: the cascade's patch (m); z: which fields (0: A, 1: B).
    float4 _RGWaveSpectrum;

    // x: N; y: this step's span (2, 4 .. N); z: 1 along u, 0 along v.
    float4 _RGWaveFFT;

    // The cascades' fields after the FFT: A = (dh/dx, dh/dz, dDx/dx, dDz/dz), B = (dDx/dz, h, -, -).
    sampler2D _RGWaveA0;
    sampler2D _RGWaveB0;
    sampler2D _RGWaveA1;
    sampler2D _RGWaveB1;

    // x: the root-mean-square slope wanted; y: the choppiness (lambda, on the fields at that strength);
    // z: N; w: how many times cascade 1 repeats across the texture.
    float4 _RGWaveCompose;

    // The game's own clock (SimulationManager.Update): y = the share of its hour gone. It runs with the
    // game's speed and stops while the game is paused.
    float4 _SimulationTime2;

    // A texel's index along each axis, 0..n-1 (the uv of a texel centre is (i + 0.5) / n).
    float2 TexelIndex(float2 uv, float n)
    {
        return min(floor(uv * n), n - 1.0);
    }

    float4 ReadTexel(sampler2D t, float2 index, float n)
    {
        return tex2Dlod(t, float4((index + 0.5) / n, 0.0, 0.0));
    }

    float2 Times(float2 a, float2 b)
    {
        return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
    }

    float4 fragSpectrum(v2f_full i) : SV_Target
    {
        float n = _RGWaveSpectrum.x;
        float2 index = TexelIndex(i.uv, n);
        float2 m = index - n * step(n * 0.5, index);
        float2 k = m * (RG_TAU / _RGWaveSpectrum.y);
        float2 mirror = fmod(n - index, n);
        float4 a = ReadTexel(_RGWaveH0, index, n);
        float4 b = ReadTexel(_RGWaveH0, mirror, n);

        // h(k, t) = h0(k) e^(i phase) + conj(h0(-k)) e^(-i phase); whole cycles in the hour.
        float phase = RG_TAU * frac(a.z * _SimulationTime2.y);
        float2 turn = float2(cos(phase), sin(phase));
        float2 h = Times(a.xy, turn) + Times(float2(b.x, -b.y), float2(turn.x, -turn.y));

        // k = 0 has no direction (and no wave): 0 / 1e-20, never 0 / 0 -- OpenGL compiled a ternary
        // here into arithmetic on both sides, and the NaN filled the whole transform.
        float2 unit = k / max(length(k), 1e-20);
        float4 o;
        if (_RGWaveSpectrum.z < 0.5)
        {
            // A: (i kx h) + i (i kz h); (-kx^2 / |k| h) + i (-kz^2 / |k| h).
            o.xy = float2(-k.x * h.y - k.y * h.x, k.x * h.x - k.y * h.y);
            float fx = -k.x * unit.x, fz = -k.y * unit.y;
            o.zw = float2(fx * h.x - fz * h.y, fx * h.y + fz * h.x);
        }
        else
        {
            // B: (-kx kz / |k| h) + i h.
            float fxz = -k.x * unit.y;
            o = float4(fxz * h.x - h.y, fxz * h.y + h.x, 0.0, 0.0);
        }
        return o;
    }

    float4 fragFFT(v2f_full i) : SV_Target
    {
        float n = _RGWaveFFT.x, span = _RGWaveFFT.y;
        bool alongU = _RGWaveFFT.z > 0.5;
        float2 index = TexelIndex(i.uv, n);
        float j = alongU ? index.x : index.y;
        float half_ = span * 0.5;
        float even = floor(j / span) * half_ + fmod(j, half_);
        float odd = even + n * 0.5;
        float4 e = ReadTexel(_MainTex, alongU ? float2(even, index.y) : float2(index.x, even), n);
        float4 o = ReadTexel(_MainTex, alongU ? float2(odd, index.y) : float2(index.x, odd), n);
        // e^(+2 pi i j / span): the inverse transform. j / span's fraction first: exact in float.
        float angle = RG_TAU * frac(j / span);
        float2 w = float2(cos(angle), sin(angle));
        return e + float4(Times(w, o.xy), Times(w, o.zw));
    }

    // Cascade 0 between its texels, as the GPU's bilinear filter would (the float textures are read
    // point: not every GPU filters 32-bit floats), the patch repeating.
    float4 Bilinear(sampler2D t, float2 uv, float n)
    {
        float2 p = uv * n - 0.5;
        float2 base = floor(p);
        float2 f = p - base;
        float2 i0 = fmod(base + n, n), i1 = fmod(base + 1.0 + n, n);
        float4 a = ReadTexel(t, i0, n);
        float4 b = ReadTexel(t, float2(i1.x, i0.y), n);
        float4 c = ReadTexel(t, float2(i0.x, i1.y), n);
        float4 d = ReadTexel(t, i1, n);
        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    float4 fragCompose(v2f_full i) : SV_Target
    {
        float n = _RGWaveCompose.z;
        float4 a = Bilinear(_RGWaveA0, i.uv, n);
        float2 b = Bilinear(_RGWaveB0, i.uv, n).xy;
        // Cascade 1's texels are this texture's: the one straight under.
        float2 index = TexelIndex(frac(i.uv * _RGWaveCompose.w), n);
        a += ReadTexel(_RGWaveA1, index, n);
        b += ReadTexel(_RGWaveB1, index, n).xy;

        float strength = _RGWaveCompose.x;
        a *= strength;
        b *= strength;
        float chop = _RGWaveCompose.y;
        // J = I + chop grad D; the slopes of the moved surface J^-T grad h (WaterSpectrum.ChoppySlope).
        float jxx = 1.0 + chop * a.z, jzz = 1.0 + chop * a.w, jxz = chop * b.x;
        float det = max(jxx * jzz - jxz * jxz, 0.25);
        float2 slope = float2(jzz * a.x - jxz * a.y, jxx * a.y - jxz * a.x) / det;
        slope = FiniteSigned(float3(slope, 0.0)).xy;

        float2 m = clamp(float2(-2.0 * slope.x, 2.0 * slope.y), -1.0, 1.0);
        float x = 0.5 + 0.5 * m.x, y = 0.5 + 0.5 * m.y;
        return float4(x, y, 1.0, x);
    }
    ENDCG

    SubShader
    {
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_full
            #pragma fragment fragSpectrum
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_full
            #pragma fragment fragFFT
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_full
            #pragma fragment fragCompose
            #pragma target 3.5
            ENDCG
        }
    }

    Fallback Off
}
