// The city in the map's water, by day and by night (RealGlass/Water/WaterCityReflection.cs has the
// model and its sources; ported from RealLight's CityReflection, 2026-10-03, which checked it against
// the CPU in an editor test). Drawn from a command buffer after the opaque image effects, before
// Bloom, so reflected lights glow as the lights do. Only on the map's water at the sea's level: the
// pixels whose depth lies at that plane or beyond it (the water or its bed). Three passes:
//   0 TRACE (a cube round the camera, into a half-size buffer with mips): the ray reflected by the
//     waves at the water's point (WaveSlope; the flat sea's mirror ray with the waves at 0), its
//     slope divided by the Length slider (the reflection drawn longer), followed through the depth
//     texture; where it meets the city, the city's colour (a half-size copy of the frame) in place
//     of the sky the water's cube already shows, x the rough sea's reflectance at the view's angle.
//     rgb = that x a, a = 1 on the water (0 elsewhere).
//   1 STREAKS (a Blit): a Gaussian up and down, the waves too short for the screen. Read from the
//     mip whose texels are half a sigma, 17 taps over +-3 sigma. Weighted by a: only water is
//     averaged (normalised convolution), so the shore does not darken the reflection's edge.
//   2 COMPOSITE (the cube, onto the frame): on the water, the streaked rgb / a. Added (Blend One
//     One); never taking a pixel below black.
Shader "RealGlass/CityReflection"
{
    // Blit binds its source as the material's main texture, which needs the property declared
    // (without it the streak pass reads Unity's grey stand-in: RealLight's editor test).
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "WaterCommon.cginc"

    // 64 steps over 1 m .. 8 km grow 15% each: at 32 (34%) a step near a roof's edge jumped from in
    // front of the facade to over and behind it, and the reflection's top was eaten (RealLight's
    // editor test, a 60 m wall).
    #define RG_MARCH 64
    #define RG_REFINE 6
    #define RG_STREAK 8
    #define RG_WAVES 16

    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    // The frame as the game drew it, just before (a half-size copy): what the rays meet.
    sampler2D _RGWaterFrame;

    // The traced reflection after the streaks (half size).
    sampler2D _RGWaterStreaked;

    // The streaks' source (Blit).
    sampler2D _MainTex;

    // The theme's cube: the sky the water shows where no city is met.
    samplerCUBE _RGWaterTheme;

    // x: the water's level (world y); y: the city reflection's strength; z: the share of the theme's
    // sky the water's cube shows (the Sky slider while ours is on the water, 0 otherwise); w: the
    // theme cube's mip to read it at.
    float4 _RGWaterGeom;

    // x: how far a ray is followed (m); y: the least thickness of a surface (m); z: the screen's
    // edge fade (share); w: the reflection's length (1 = a mirror: WaterOptics.MirrorSlope; below 1,
    // unset included, read as 1).
    float4 _RGWaterMarch;

    // x: the streaks' sigma up and down (uv); y: a texel's height in the half-size buffers (uv; a
    // command buffer's Blit left _MainTex_TexelSize at 0 in RealLight's editor test).
    float4 _RGWaterStreak;

    // x: the waves' strength (the slider; 0: the flat sea's mirror).
    float4 _RGWaterWave;

    // The waves (WaterOptics.Waves): row i = the wave vector's x, z (cycles per metre), whole cycles
    // per hour, the slope at 100%; row RG_WAVES + i = the phase (cycles), the wavelength (m).
    float4 _RGWaterWaves[2 * RG_WAVES];

    // The game's own clock (SimulationManager.Update): y = the share of its hour gone. It runs with
    // the game's speed and stops while the game is paused, as the game's water does.
    float4 _SimulationTime2;

    // rgb: the light the game's water lights its cube with (the sun on the level + the sky's
    // ambient, linear).
    float4 _RGWaterLight;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 dir : TEXCOORD0;
        float4 screen : TEXCOORD1;
    };

    v2f vert(float4 vertex : POSITION)
    {
        v2f o;
        float3 world = _WorldSpaceCameraPos + vertex.xyz * 500.0;
        o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
        #if defined(UNITY_REVERSED_Z)
        o.pos.z = 1.0e-6 * o.pos.w;
        #else
        o.pos.z = 0.999999 * o.pos.w;
        #endif
        o.dir = vertex.xyz;
        o.screen = ComputeScreenPos(o.pos);
        return o;
    }

    // A world point on the screen: uv, and its eye depth (clip w); w <= the near plane: behind.
    float3 Project(float3 q)
    {
        float4 clip = mul(UNITY_MATRIX_VP, float4(q, 1.0));
        float4 screen = ComputeScreenPos(clip);
        return float3(screen.xy / max(clip.w, 1e-6), clip.w);
    }

    bool OnScreen(float3 s)
    {
        return s.z > _ProjectionParams.y && s.x >= 0.0 && s.x <= 1.0 && s.y >= 0.0 && s.y <= 1.0;
    }

    // How far the world point q (on the screen at s) lies behind what the depth texture shows there
    // (m; negative: in front). -1e9 where that is the sky, or the water itself or its bed (at or
    // below the level: at a low angle the next pixel's water lies metres nearer, and a ray just
    // leaving the water would meet it).
    float Behind(float3 s, float3 q)
    {
        float raw = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(s.xy, 0.0, 0.0));
        if (Linear01Depth(raw) > 0.9999)
            return -1e9;
        float eye = LinearEyeDepth(raw);
        // The scene's height from this texel's depth along q's ray: off by the texel's own slant,
        // so the margin grows with the distance.
        float sceneY = _WorldSpaceCameraPos.y + (q.y - _WorldSpaceCameraPos.y) * eye / s.z;
        if (sceneY <= _RGWaterGeom.x + max(0.5, 0.01 * eye))
            return -1e9;
        return s.z - eye;
    }

    // The map's water along d at uv: what the game drew lies at the sea's plane or beyond it (its
    // bed), and is not the void. tFlat: the plane's distance.
    bool Water(float3 d, float2 uv, out float tFlat)
    {
        tFlat = 0.0;
        float h = _WorldSpaceCameraPos.y - _RGWaterGeom.x;
        if (d.y >= -1e-4 || h <= 0.5)
            return false;
        tFlat = h / -d.y;
        float raw = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0.0, 0.0));
        if (Linear01Depth(raw) > 0.9999)
            return false;
        float3 forward = -normalize(UNITY_MATRIX_V[2].xyz);
        float scene = LinearEyeDepth(raw) / max(dot(d, forward), 1e-3);
        return scene >= tFlat * (1.0 - 0.005) - 2.0;
    }

    // The water's slope (dh/dx, dh/dz) at the world point xz: the waves at least 4 pixels long on the
    // screen there, each fading out between 4 and 2 -- the shorter ones are the streaks'. Judged in
    // each wave's own direction (dx, dy: the step to the next pixel, m): toward the horizon the
    // water is foreshortened along the view, so the waves running across it stay and those running
    // along it go. Phases in cycles, frac before the cosine: the map's far corners are tens of
    // thousands of cycles out.
    float2 WaveSlope(float2 xz, float2 dx, float2 dy)
    {
        float2 slope = float2(0.0, 0.0);
        float hour = _SimulationTime2.y;
        [unroll]
        for (int w = 0; w < RG_WAVES; w++)
        {
            float4 wave = _RGWaterWaves[w];
            float4 more = _RGWaterWaves[RG_WAVES + w];
            float perPixel = max(abs(dot(wave.xy, dx)), abs(dot(wave.xy, dy)));
            float fade = saturate(0.5 / max(perPixel, 1e-6) - 1.0);
            float cycles = frac(dot(wave.xy, xz) - wave.z * hour + more.x);
            // The wave's direction: its wave vector x its length.
            slope += wave.xy * more.y * (wave.w * fade * cos(6.2831853 * cycles));
        }
        return slope * _RGWaterWave.x;
    }

    float4 fragTrace(v2f i) : SV_Target
    {
        float3 d = normalize(i.dir);
        float2 uv = i.screen.xy / i.screen.w;

        // Where the view meets the flat sea, and the step to the next pixel there -- before any
        // branch: the derivatives need every pixel of the quad.
        float h = _WorldSpaceCameraPos.y - _RGWaterGeom.x;
        float2 xz = _WorldSpaceCameraPos.xz + d.xz * (h / max(-d.y, 1e-4));
        float2 xzDx = ddx(xz), xzDy = ddy(xz);

        float tFlat;
        if (!Water(d, uv, tFlat))
            return float4(0.0, 0.0, 0.0, 0.0);

        float k = RGSeaRho(-d.y) * _RGWaterGeom.y;
        float3 p = float3(_WorldSpaceCameraPos.x + d.x * tFlat, _RGWaterGeom.x, _WorldSpaceCameraPos.z + d.z * tFlat);
        // The ray the waves reflect (a ray turned below the level meets the next wave: mirrored back
        // up), and the one followed: its slope divided by the length (the reflection drawn that many
        // times as long). The sky the city replaces is the flat sea's mirror: the water's cube.
        float3 mirror = float3(d.x, -d.y, d.z);
        float3 bent = mirror;
        if (_RGWaterWave.x > 0.0)
        {
            float2 slope = FiniteSigned(float3(WaveSlope(xz, xzDx, xzDy), 0.0)).xy;
            bent = reflect(d, normalize(float3(-slope.x, 1.0, -slope.y)));
        }
        float3 r = normalize(float3(bent.x, abs(bent.y) / max(_RGWaterMarch.w, 1.0), bent.z));

        // The mirror ray through the depth texture: geometric steps, then bisection.
        float tMax = _RGWaterMarch.x;
        float t0 = max(1.0, tFlat * 0.002);
        float grow = pow(tMax / t0, 1.0 / (RG_MARCH - 1));
        float tPrev = 0.0;
        float t = t0;
        float hit = -1.0;
        [loop]
        for (int s = 0; s < RG_MARCH; s++)
        {
            float3 wq = p + r * t;
            float3 q = Project(wq);
            if (!OnScreen(q))
                break;
            float behind = Behind(q, wq);
            if (behind > 0.0 && behind < max(_RGWaterMarch.y, (t - tPrev) * 1.5))
            {
                hit = t;
                break;
            }
            tPrev = t;
            t *= grow;
        }

        // Water that meets nothing: the cube's sky stays (nothing added), but it is water.
        if (hit < 0.0 || k <= 0.0)
            return float4(0.0, 0.0, 0.0, 1.0);

        float lo = tPrev, hi = hit;
        [loop]
        for (int b = 0; b < RG_REFINE; b++)
        {
            float mid = 0.5 * (lo + hi);
            float3 wm = p + r * mid;
            if (Behind(Project(wm), wm) > 0.0)
                hi = mid;
            else
                lo = mid;
        }

        float3 at = Project(p + r * hi);
        float fade = saturate(min(min(at.x, 1.0 - at.x), min(at.y, 1.0 - at.y)) / max(_RGWaterMarch.z, 1e-4));
        fade *= 1.0 - smoothstep(0.7 * tMax, tMax, hi);

        float3 c = Finite(tex2Dlod(_RGWaterFrame, float4(at.xy, 0.0, 0.0)).rgb);
        if (_RGWaterGeom.z > 0.0)
            c -= _RGWaterGeom.z * RGSkyLight(texCUBElod(_RGWaterTheme, float4(mirror, _RGWaterGeom.w)).rgb * _RGWaterLight.rgb, mirror);
        return float4(FiniteSigned(c * (k * fade)), 1.0);
    }

    // The Gaussian up and down: 2 x RG_STREAK + 1 taps over +-3 sigma, from the mip whose texels are
    // half a sigma.
    float4 fragStreaks(v2f_img i) : SV_Target
    {
        float sigma = _RGWaterStreak.x;
        // Under a third of a texel the taps fall on one texel: sharp.
        float texels = max(sigma / max(_RGWaterStreak.y, 1e-6), 1e-4);
        float lod = max(0.0, log2(texels / 2.0));
        float2 step = float2(0.0, texels < 0.3 ? 0.0 : 3.0 * sigma / RG_STREAK);
        float4 sum = float4(0.0, 0.0, 0.0, 0.0);
        float weights = 0.0;
        [unroll]
        for (int k = -RG_STREAK; k <= RG_STREAK; k++)
        {
            float x = 3.0 * k / RG_STREAK;
            float w = exp(-0.5 * x * x);
            sum += w * tex2Dlod(_MainTex, float4(i.uv + step * k, 0.0, lod));
            weights += w;
        }
        return sum / weights;
    }

    float4 fragComposite(v2f i) : SV_Target
    {
        float3 d = normalize(i.dir);
        float2 uv = i.screen.xy / i.screen.w;
        float tFlat;
        if (!Water(d, uv, tFlat))
            return float4(0.0, 0.0, 0.0, 0.0);

        float4 c = tex2Dlod(_RGWaterStreaked, float4(uv, 0.0, 0.0));
        if (c.a <= 1e-3)
            return float4(0.0, 0.0, 0.0, 0.0);
        float3 added = FiniteSigned(c.rgb / c.a);

        // Never below black (by day a dark building over a bright sky takes light away).
        float3 base = Finite(tex2Dlod(_RGWaterFrame, float4(uv, 0.0, 0.0)).rgb);
        return float4(max(added, -0.98 * base), 0.0);
    }
    ENDCG

    SubShader
    {
        // 0: trace, into the half-size buffer.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragTrace
            #pragma target 3.5
            ENDCG
        }

        // 1: the streaks, up and down.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragStreaks
            #pragma target 3.5
            ENDCG
        }

        // 2: onto the water.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One, Zero One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            #pragma target 3.5
            ENDCG
        }
    }
}
