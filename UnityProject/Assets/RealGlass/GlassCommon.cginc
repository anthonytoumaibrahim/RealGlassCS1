// Real Glass -- everything a glass pixel needs: what counts as glass, the pane it belongs to and
// how that pane is bent, Fresnel, the screen-space trace, what fills a reflection the screen
// cannot give, and the sun's glint. Included by Glass.shader. (Behind the glass there is nothing
// of ours: by day a dark inside, by night the game's own window -- Glass.shader.)
//
// Sources (credited in README.md and LICENSE):
//  - McGuire & Mara, "Efficient GPU Screen-Space Ray Tracing", JCGT 3(4) 2014 (BSD 2-Clause):
//    TraceScreenRay is a port of their traceScreenSpaceRay1.
//  - Uludag, "Hi-Z Screen-Space Cone-Traced Reflections", GPU Pro 5 (2014): TraceHiZ walks a
//    min-depth pyramid of the screen, whole cells at a time where nothing can be hit; with the
//    maximum beside each minimum (Tevs, Ihrke & Seidel, "Maximum Mipmaps", I3D 2008) it also
//    crosses whole cells a ray passes behind.
//  - Schlick 1994 (Fresnel), Walter et al. 2007 (GGX), Jimenez 2014 (interleaved gradient noise),
//    Hoskins (hash without sine).
//  - Pane bending: insulating-glass "pillowing" (a plate's first mode, Navier) and the roller
//    wave of toughened glass (ASTM C1651).
//  - Layering of the reflection (screen first, then a live cube, one face a frame): Mirror's Edge
//    Catalyst, "Lighting the City of Glass" (GDC 2016); the sky cube: GTA V's per-frame cubemap,
//    with the clouds drawn into it as Unreal's Sky Light captures volumetric clouds.
//  - What is mirrored is last frame's FINISHED picture, reprojected (Killzone Shadow Fall, GDC
//    2014): fog, clouds, the lamps' glow and the other glass are in the reflections.
//  - The buildings round, off screen: a top-down height map of the city round the view (as
//    CRYENGINE's height-map ambient occlusion and Unreal's heightfield sky occlusion of
//    reflections read one), walked cell by cell (Amanatides & Woo, Eurographics 1987): CityHit.
//  - Steady glints: specular anti-aliasing (Kaplanyan et al. 2016; Tokuyoshi & Kaplanyan 2019),
//    Filament's constants.

#ifndef REALGLASS_COMMON_INCLUDED
#define REALGLASS_COMMON_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------------------------
// Uniforms. GlassUniforms.cs is the one place that writes them.
// ---------------------------------------------------------------------------------------------

float4 _RGGlass;      // x reflectance head-on (F0, the chosen glass), y reflectance edge-on, z clarity (VLT), w strength
float4 _RGGlass2;     // x silvery edge-on 0..1, y -, z -, w -
float4 _RGColorMap;   // xy the game's object colour map (width, height): where a building's colour index points
float4 _RGFinished;   // x 1 while _RGSceneFinished holds last frame's finished picture
float4 _RGTint;       // rgb glass tint (linear, brightest channel 1), a own-colour mix 0..1
float4 _RGMask;       // x shininess threshold, y night-window weight, z show mask (1), w coat tint mix
float4 _RGRefl;       // x reflection brightness, y roughness 0..1, z sun glint, w screen-edge fade 0..0.5
float4 _RGTrace;      // x trace on (1), y max steps, z stride (px), w thickness scale
float4 _RGFill;       // x off-screen fill (0 mirrored screen, 1 city colour, 2 sky), y ground level (world y), z cube mips, w -
float4 _RGPane;       // x pane width (m), y pillow bow (m), z tilt (rad), w roller-wave peak (m)
float4 _RGPane2;      // x bow bias -1 (all sunk) .. +1 (all bulging), y roller-wave length (m), z storey height (m), w -
float4 _RGLight;      // x daylight 0 (night) .. 1 (day), y the inside by day (x the sky's light), z -, w -
float4 _RGSunDir;     // xyz towards the sun (world), w sun above the horizon 0..1
float4 _RGSunColor;   // rgb the sun's radiance (colour x intensity)
float4 _RGAmbSky;     // rgb light from the sky (trilight "sky")
float4 _RGAmbGround;  // rgb light from below (trilight "ground")
float4 _RGFeatures;   // x the buildings round in the reflections, y -, z -, w steady glints
float4 _RGDebug;      // x: 0 off; the GPU preview's checks (1 trace, 2 depth, 3 screen copy, 5 the finished
                      // picture reprojected, magenta off it; 6 the city walk: green a building met, blue none,
                      // red no map; 8 the painted pane found: green, its border yellow, red none; 9 the
                      // hierarchical depth at level y, red where it is wrong). The mod never sets it.

// The city from above (CityMap.cs): per cell the highest roof over it and whose it is.
sampler2D _RGCityMap;         // rg the roof's world height in 1/32 m, ba the building's id (0 none): 4 m cells
sampler2D _RGCityCoarse;      // the same for blocks of 8 x 8 cells: each block's highest roof
float4 _RGCityMapST;          // xy the map's corner (world x, z), z metres a cell, w 1 while a map is bound
float4 _RGCityMapInfo;        // x cells a side, y cells a block, z the highest roof in the map (world y)

float4 _RGBudget;     // x city walk cells, y its blocks, z -, w steps each way up and down to a painted pane's edges
float4 _RGBudget2;    // x steps each way across to a painted pane's sides, y steps of the Hi-Z trace

sampler2D _RGScene;           // the lit opaque picture, copied before any glass (mipmapped): what is UNDER a pane
float4 _RGScene_TexelSize;
sampler2D _RGSceneFinished;   // last frame's finished picture (fog, clouds, lamps, glass; mipmapped): what a pane MIRRORS
float4x4 _RGPrevVP;           // last frame's camera (OpenGL-convention projection x view): where a point was on it
sampler2D _RGCity;            // 1x1: the average colour of the lower half of the screen
samplerCUBE _RGSky;           // the live sky (or the game's environment map)
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
sampler2D _RGHiZ;             // the scene's eye depth (m): mip k the nearest (r) and farthest (g) of each 2 x 2 of mip k - 1 (GlassScreen.cs)
float4 _RGHiZInfo;            // x levels built, yz level 0's size (px), w 1 while built

// ---------------------------------------------------------------------------------------------
// Small helpers.
// ---------------------------------------------------------------------------------------------

// Dave Hoskins, "Hash without Sine" (MIT): stable for the cell indices a city has (|n| < 1e5).
float Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Jimenez 2014 ("Next Generation Post Processing in Call of Duty: Advanced Warfare"): a per-pixel
// jitter that does not clump like white noise.
float Ign(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float Luma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 NormaliseTint(float3 c)
{
    return c / max(max(c.r, max(c.g, c.b)), 0.02);
}

// ---------------------------------------------------------------------------------------------
// Which building a pixel is: its place in the game's object colour map (the colour index every
// distance tier carries, a sub-building its parent's), as the building's id -- the city walk's
// own building, which it must not meet.
// ---------------------------------------------------------------------------------------------

float IdOf(float2 colourIndex)
{
    // The index is a texel centre, (k + 0.5) / size: exact on every tier.
    float2 cell = floor(colourIndex * _RGColorMap.xy);
    return cell.x + cell.y * _RGColorMap.x;
}

// ---------------------------------------------------------------------------------------------
// What counts as glass: the asset's own specular map, read the way the game reads it
// (spec = 1 - _XYSMap.z), plus -- optionally -- the windows it lights at night (_ACIMap.b > 0.48).
// _ACIMap.r is the asset's cut-out: where the game discards, so do we.
// ---------------------------------------------------------------------------------------------

float GlassMask(float4 xys, float4 aci)
{
    float spec = 1.0 - xys.z;
    float t = _RGMask.x;
    float m = smoothstep(t - 0.04, t + 0.04, spec);
    float night = smoothstep(0.46, 0.56, aci.b) * _RGMask.y;
    m = max(m, night * smoothstep(t - 0.35, t - 0.1, spec));
    return m * step(aci.r, 0.5);
}

// ---------------------------------------------------------------------------------------------
// The facade and its panes. The frame comes from the GEOMETRIC normal: t runs along the facade,
// level; floors are level whatever the facade does. A pane is paneWidth wide and one storey tall.
// ---------------------------------------------------------------------------------------------

float3 FacadeTangent(float3 n)
{
    float2 h = float2(n.z, -n.x);            // up x n, level
    float l = length(h);
    return l > 1e-3 ? float3(h.x / l, 0.0, h.y / l) : float3(1.0, 0.0, 0.0);
}

// A facade's identity for the hashes: its heading, quantised to 5 degrees, so one flat facade is
// one pattern and the next one round the corner is another.
float FacadeId(float3 n)
{
    return floor((atan2(n.z, n.x) + 3.14159265) * 11.459);
}

// The pane normal: the facade normal bent by the pane's pillow (both panes of an insulating unit
// bow with its gas: w = w0 sin(pi u) sin(pi v), the first mode of a plate under uniform pressure),
// its install tilt and, close up, the roller wave. footprint = metres a pixel covers: a bend
// finer than the pixel is averaged away (never aliased). painted: the window the asset painted
// round the pixel (left, right along t; bottom, top), 0 when none -- then its panes are that
// window's, split into panes about the pane width wide; else a grid of pane width by storey.
float3 PaneNormal(float3 n, float3 t, float3 P, float baseY, float footprint, float4 painted, out float2 paneCell)
{
    float w = _RGPane.x;
    float h = _RGPane2.z;
    float fid = FacadeId(n);
    float2 c = float2(dot(P, t) / w, (P.y - baseY) / h);
    float2 which = float2(0.0, 0.0);
    if (painted.y > painted.x)
    {
        float2 span = float2(painted.y - painted.x, painted.w - painted.z);
        float2 count = max(round(span / float2(w, h)), 1.0);
        w = span.x / count.x;
        h = span.y / count.y;
        c = float2((dot(P, t) - painted.x) / w, (P.y - painted.z) / h);
        which = round(painted.xz * 4.0);   // one window's panes are not the next one's
    }
    paneCell = floor(c) + which;
    float2 f = c - floor(c);
    float2 r = Hash22(paneCell + float2(fid * 17.0, fid * 3.0));
    float2 r2 = Hash22(paneCell.yx + float2(fid, 91.0));

    // Pillow: amplitude in metres, sign from the weather bias and the pane's own seal.
    float sign1 = (r.x * 2.0 - 1.0) + _RGPane2.x;
    float w0 = _RGPane.y * (0.35 + 0.65 * r.y) * clamp(sign1 * 1.5, -1.0, 1.0);
    const float PI = 3.14159265;
    float su = sin(PI * f.x), cu = cos(PI * f.x);
    float sv = sin(PI * f.y), cv = cos(PI * f.y);
    // A pane that covers less than ~3 pixels bends nothing you could see: fade the slope out.
    float pillowVis = saturate(min(w, h) / max(footprint * 3.0, 1e-4) - 1.0);
    float du = w0 * PI / w * cu * sv * pillowVis;   // dw / dx (x along t)
    float dv = w0 * PI / h * su * cv * pillowVis;   // dw / dy

    // Install tilt: a few tenths of a degree, the reason a neighbour's edge steps at every pane.
    float tiltVis = saturate(min(w, h) / max(footprint * 2.0, 1e-4));
    du += (r2.x * 2.0 - 1.0) * _RGPane.z * tiltVis;
    dv += (r2.y * 2.0 - 1.0) * _RGPane.z * tiltVis;

    // Roller wave: across one direction of the pane (the furnace's), ~0.35 m long.
    float lambda = max(_RGPane2.y, 0.05);
    float waveVis = saturate(lambda / max(footprint * 4.0, 1e-4) - 0.5);
    float along = (r.x > 0.5) ? c.x * w : c.y * h;
    float slope = _RGPane.w * 2.0 * PI / lambda * cos(2.0 * PI * along / lambda + r2.x * 6.283) * waveVis;
    if (r.x > 0.5) du += slope; else dv += slope;

    // Roof glass (skylights, atria) has no storeys to be bowed between: bend facades only.
    float vertical = saturate((1.0 - abs(n.y)) * 3.0);
    float3 up = float3(0.0, 1.0, 0.0);
    return normalize(n - (du * t + dv * up) * vertical);
}

// ---------------------------------------------------------------------------------------------
// Fresnel: Schlick's curve, rescaled so edge-on is the "glancing" setting rather than 1 (a raised
// camera sees every facade edge-on; physical 1 there read as "too bright" in the first attempt).
// In colour: head-on the coating's tint (F0 x coat), and the rise toward edge-on the same for
// every colour -- so a coated pane turns silvery edge-on, as real ones do. _RGGlass2.x says how
// far: 0 keeps the tint at every angle (the look before this), 1 is Schlick's curve per channel.
// Fs, the luminance, is the same either way: what the glass lets through does not change.
// ---------------------------------------------------------------------------------------------

float3 GlassFresnel(float cosTheta, float F0, float3 coat, out float Fs)
{
    float f = pow(1.0 - saturate(cosTheta), 5.0);
    float Fmax = max(_RGGlass.y, F0);
    Fs = F0 + (Fmax - F0) * f;
    float3 physical = F0 * coat + (Fmax - F0 * coat) * f;
    return lerp(Fs * coat, physical, _RGGlass2.x);
}

// ---------------------------------------------------------------------------------------------
// The screen-space trace: McGuire & Mara's DDA (BSD 2-Clause), in Unity's view space (-z ahead).
// "Pixel" coordinates come from UNITY_MATRIX_P followed by ComputeScreenPos's affine map, so the
// Direct3D flip of render textures is handled exactly as for this pixel's own screen position,
// and the homogeneous interpolation of the original stays valid.
// ---------------------------------------------------------------------------------------------

float4 ToPixelH(float3 cs)
{
    float4 clip = mul(UNITY_MATRIX_P, float4(cs, 1.0));
    float4 s = ComputeScreenPos(clip);
    return float4(s.x * _ScreenParams.x, s.y * _ScreenParams.y, 0.0, clip.w);
}

float SceneZ(float2 pixel)
{
    float2 uv = (floor(pixel) + 0.5) / _ScreenParams.xy;
    return -LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0.0, 0.0)));
}

// Returns true on a hit; hitUV is where to read the picture. clipped: the ray left the screen
// before its length ran out (what the off-screen fill is for).
bool TraceScreenRay(float3 csOrig, float3 csDir, float jitter, out float2 hitUV, out float hitDistance, out bool clipped)
{
    hitUV = float2(0.0, 0.0);
    hitDistance = 0.0;
    clipped = false;

    float stride = _RGTrace.z;
    float maxSteps = _RGTrace.y;
    float maxDistance = 3000.0;
    float nearPlaneZ = -_ProjectionParams.y;

    // Clip to the near plane.
    float rayLength = ((csOrig.z + csDir.z * maxDistance) > nearPlaneZ) ?
        (nearPlaneZ - csOrig.z) / csDir.z : maxDistance;
    float3 csEnd = csOrig + csDir * rayLength;

    float4 H0 = ToPixelH(csOrig);
    float4 H1 = ToPixelH(csEnd);
    float k0 = 1.0 / H0.w, k1 = 1.0 / H1.w;
    float3 Q0 = csOrig * k0, Q1 = csEnd * k1;
    float2 P0 = H0.xy * k0, P1 = H1.xy * k1;

    // Clip the far end to the screen, so no step is spent outside it (P, Q and k are all linear
    // in the same parameter, so one alpha moves all three).
    float2 lo = float2(0.5, 0.5), hi = _ScreenParams.xy - 0.5;
    float2 dP = P1 - P0;
    float alpha = 1.0;
    if (P1.x > hi.x) alpha = min(alpha, (hi.x - P0.x) / dP.x);
    if (P1.x < lo.x) alpha = min(alpha, (lo.x - P0.x) / dP.x);
    if (P1.y > hi.y) alpha = min(alpha, (hi.y - P0.y) / dP.y);
    if (P1.y < lo.y) alpha = min(alpha, (lo.y - P0.y) / dP.y);
    alpha = saturate(alpha);
    clipped = alpha < 0.999;
    P1 = lerp(P0, P1, alpha);
    Q1 = lerp(Q0, Q1, alpha);
    k1 = lerp(k0, k1, alpha);

    // A degenerate line covers at least one pixel.
    P1 += (dot(P1 - P0, P1 - P0) < 0.0001) ? float2(0.01, 0.01) : float2(0.0, 0.0);
    float2 delta = P1 - P0;

    // Permute so the primary iteration is in x.
    bool permute = false;
    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        delta = delta.yx; P0 = P0.yx; P1 = P1.yx;
    }

    float stepDir = sign(delta.x);
    float invdx = stepDir / delta.x;
    float3 dQ = (Q1 - Q0) * invdx;
    float dk = (k1 - k0) * invdx;
    float2 dPs = float2(stepDir, delta.y * invdx);

    dPs *= stride; dQ *= stride; dk *= stride;
    P0 += dPs * jitter; Q0 += dQ * jitter; k0 += dk * jitter;

    float3 Q = Q0;
    float end = P1.x * stepDir;
    float k = k0, stepCount = 0.0, prevZMaxEstimate = csOrig.z;
    float rayZMin = prevZMaxEstimate, rayZMax = prevZMaxEstimate;
    float sceneZMax = rayZMax + 100000.0;
    float2 P = P0;
    float2 hitPixel = P0;
    float thick = 1.0;
    bool hit = false;

    [loop]
    for (; (P.x * stepDir) <= end && stepCount < maxSteps; P += dPs, Q.z += dQ.z, k += dk, stepCount += 1.0)
    {
        rayZMin = prevZMaxEstimate;
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        prevZMaxEstimate = rayZMax;
        if (rayZMin > rayZMax) { float tmp = rayZMin; rayZMin = rayZMax; rayZMax = tmp; }

        hitPixel = permute ? P.yx : P;
        sceneZMax = SceneZ(hitPixel);
        // A pixel of the depth buffer is a slab this thick. Crossing a surface inside one step is
        // caught whatever the stride (the test takes the step's whole z range); the thickness
        // only decides what a ray may pass BEHIND: a metre near, 1% of the depth far away.
        thick = max(1.0, -sceneZMax * 0.01) * _RGTrace.w;
        if (rayZMax >= sceneZMax - thick && rayZMin < sceneZMax)
        {
            hit = true;
            break;
        }
    }

    if (hit)
    {
        // Refine towards the surface: a few halvings of the last stride.
        float2 Pa = P - dPs;
        float za = Q.z - dQ.z, ka = k - dk;
        float2 Pb = P;
        float zb = Q.z, kb = k;
        [unroll]
        for (int i = 0; i < 4; i++)
        {
            float2 Pm = (Pa + Pb) * 0.5;
            float zm = (za + zb) * 0.5, km = (ka + kb) * 0.5;
            float rz = zm / km;
            float2 pix = permute ? Pm.yx : Pm;
            float sz = SceneZ(pix);
            if (rz < sz) { Pb = Pm; zb = zm; kb = km; }
            else { Pa = Pm; za = zm; ka = km; }
        }
        hitPixel = permute ? Pb.yx : Pb;
        hitUV = hitPixel / _ScreenParams.xy;
        float hitZ = zb / kb;
        hitDistance = abs(hitZ - csOrig.z) / max(abs(csDir.z), 1e-3);
        clipped = false;
    }
    return hit;
}

// ---------------------------------------------------------------------------------------------
// The same ray over the hierarchical depth (Uludag, GPU Pro 5). Along McGuire & Mara's ray, P
// (pixels) and k = 1 / eye depth are both linear on screen, so where the ray leaves a cell and
// where it reaches a depth are exact. A cell holds nothing to hit when the ray stays in front of
// its NEAREST depth all the way through it, or behind its FARTHEST by more than a pixel's
// thickness (Tevs et al.'s maximum mipmaps): the ray crosses it whole and climbs a level (a cell
// of level 7 is 128 pixels). Otherwise it descends -- first, moving away, to that nearest depth --
// and a pixel (level 0) that passes neither test is a hit, the plain trace's test exactly.
// ---------------------------------------------------------------------------------------------

// A cell of a level: its nearest (x) and farthest (y) depth (clamped to the level's size: an odd
// edge's last cell holds the rest).
float2 HiZRange(float2 cell, float level)
{
    float2 size = max(floor(_RGHiZInfo.yz / exp2(level)), 1.0);
    return tex2Dlod(_RGHiZ, float4((min(cell, size - 1.0) + 0.5) / size, 0.0, level)).rg;
}

// A pixel of the depth buffer is a slab this thick, as the plain trace has it: a metre near, 1%
// of the depth far away. Rising with the depth, so a cell's farthest bounds all its pixels'.
float TraceThickness(float depth)
{
    return max(1.0, depth * 0.01) * _RGTrace.w;
}

bool TraceHiZ(float3 csOrig, float3 csDir, float jitter, out float2 hitUV, out float hitDistance, out bool clipped)
{
    hitUV = float2(0.0, 0.0);
    hitDistance = 0.0;
    clipped = false;

    float maxDistance = 3000.0;
    float nearPlaneZ = -_ProjectionParams.y;
    float rayLength = ((csOrig.z + csDir.z * maxDistance) > nearPlaneZ) ?
        (nearPlaneZ - csOrig.z) / csDir.z : maxDistance;
    float3 csEnd = csOrig + csDir * rayLength;

    // The ray on screen in the pyramid's pixels, and 1 / eye depth along it (a perspective
    // camera's w is the eye depth).
    float2 toPyramid = _RGHiZInfo.yz / _ScreenParams.xy;
    float4 H0 = ToPixelH(csOrig);
    float4 H1 = ToPixelH(csEnd);
    float k0 = 1.0 / H0.w, k1 = 1.0 / H1.w;
    float2 P0 = H0.xy * k0 * toPyramid, P1 = H1.xy * k1 * toPyramid;

    // Clipped to the screen, as the plain trace.
    float2 lo = float2(0.5, 0.5), hi = _RGHiZInfo.yz - 0.5;
    float2 dP = P1 - P0;
    float alpha = 1.0;
    if (P1.x > hi.x) alpha = min(alpha, (hi.x - P0.x) / dP.x);
    if (P1.x < lo.x) alpha = min(alpha, (lo.x - P0.x) / dP.x);
    if (P1.y > hi.y) alpha = min(alpha, (hi.y - P0.y) / dP.y);
    if (P1.y < lo.y) alpha = min(alpha, (lo.y - P0.y) / dP.y);
    alpha = saturate(alpha);
    bool leaves = alpha < 0.999;
    P1 = lerp(P0, P1, alpha);
    k1 = lerp(k0, k1, alpha);
    dP = P1 - P0;
    float dk = k1 - k0;

    // No axis quite still (its cell edges would never be met); 0.01 px past an edge is in the
    // next cell.
    float2 ahead = step(0.0, dP);
    float2 safe = (ahead * 2.0 - 1.0) * max(abs(dP), 1e-4);
    float2 nudge = 0.01 / abs(safe);
    float len = max(abs(dP.x), abs(dP.y));

    // A pixel or two out (the origin is already off the glass), jittered as the plain trace.
    float t = (1.0 + jitter) / max(len, 1.0);
    float topLevel = _RGHiZInfo.x - 1.0;
    float level = min(2.0, topLevel);
    // The budget's steps, scaled by the trace quality as the plain trace's are (Ultra's 80 = all).
    float steps = min(_RGBudget2.y * saturate(_RGTrace.y / 80.0), 128.0);
    bool hit = false;
    float tHit = 0.0;

    [loop]
    for (int i = 0; i < 128; i++)
    {
        if (i >= steps || t > 1.0)
            break;
        float size = exp2(level);
        float2 cell = floor((P0 + dP * t) / size);
        float2 tEdge = ((cell + ahead) * size - P0) / safe;
        float tOut = min(tEdge.x, tEdge.y);
        float tNext = tOut + (tEdge.x < tEdge.y ? nudge.x : nudge.y);
        tOut = min(tOut, 1.0);
        float zIn = 1.0 / (k0 + dk * t);
        float zOut = 1.0 / (k0 + dk * tOut);
        // A pixel is judged on the depth texture itself, as the plain trace judges it (the pyramid
        // holds half floats); a coarser cell on the pyramid's nearest and farthest.
        float2 range = level > 0.5 ? HiZRange(cell, level) : (-SceneZ((cell + 0.5) / toPyramid)).xx;
        if (max(zIn, zOut) < range.x || min(zIn, zOut) > range.y + TraceThickness(range.y))
        {
            // In front of everything in the cell all the way through it, or behind all of it.
            t = tNext;
            level = min(level + 1.0, topLevel);
        }
        else if (level > 0.5)
        {
            if (dk < 0.0 && zIn < range.x)
                t = max(t, (1.0 / range.x - k0) / dk);
            level -= 1.0;
        }
        else
        {
            hit = true;
            tHit = abs(dk) > 1e-12 ? clamp((1.0 / range.x - k0) / dk, t, tOut) : t;
            break;
        }
    }

    if (hit)
    {
        hitUV = (P0 + dP * tHit) / _RGHiZInfo.yz;
        float hitZ = -1.0 / (k0 + dk * tHit);
        hitDistance = abs(hitZ - csOrig.z) / max(abs(csDir.z), 1e-3);
    }
    else
    {
        clipped = leaves && t > 1.0;
    }
    return hit;
}

// The reflections' trace: over the hierarchical depth where GlassScreen built one (the budget has
// it and a float target exists), else the plain one.
bool TraceReflection(float3 csOrig, float3 csDir, float jitter, out float2 hitUV, out float hitDistance, out bool clipped)
{
    bool hit;
    if (_RGHiZInfo.w > 0.5 && _RGBudget2.y > 0.5)
        hit = TraceHiZ(csOrig, csDir, jitter, hitUV, hitDistance, clipped);
    else
        hit = TraceScreenRay(csOrig, csDir, jitter, hitUV, hitDistance, clipped);
    return hit;
}

// ---------------------------------------------------------------------------------------------
// What a reflection shows when the screen has nothing for it.
// ---------------------------------------------------------------------------------------------

// The screen's own picture, mirrored back in at the edge a ray leaves by (the "smart invert"):
// a facade seen from above then shows streets and roofs, not empty light.
float2 MirrorUV(float2 uv)
{
    float2 m = abs(frac(uv * 0.5) * 2.0 - 1.0);   // triangle wave: 0..1..0 over 0..2
    return 1.0 - m;
}

float3 SkyRadiance(float3 R, float roughness)
{
    return texCUBElod(_RGSky, float4(R, roughness * _RGFill.z)).rgb;
}

// Where a world point is on screen, in the convention the copies are read with: on this frame's
// camera, or -- lastFrame -- on last frame's, which the finished picture was drawn with. False
// behind the camera.
bool ScreenUV(float3 W, bool lastFrame, out float2 uv)
{
    // One exit: fxc takes early returns in inlined helpers for "potentially uninitialized".
    float4 clip = mul(UNITY_MATRIX_VP, float4(W, 1.0));
    float4 s = ComputeScreenPos(clip);
    float4 c = mul(_RGPrevVP, float4(W, 1.0));
    uv = lastFrame ? c.xy / max(c.w, 1e-4) * 0.5 + 0.5 : s.xy / max(s.w, 1e-4);
    return (lastFrame ? c.w : clip.w) > _ProjectionParams.y;
}

// What the glass mirrors at a place on screen: the finished picture (fog, clouds, lamps, the other
// glass) when there is one, else the copy taken before them.
float3 MirroredPicture(float2 uv, float lod, bool finished)
{
    float3 c;
    if (finished)
        c = tex2Dlod(_RGSceneFinished, float4(uv, 0.0, lod)).rgb;
    else
        c = tex2Dlod(_RGScene, float4(uv, 0.0, lod)).rgb;
    return c;
}

// Below the horizon and nothing hit: the ground at the building's feet, where the reflected ray
// meets it, is almost always what a facade shows from a city-builder's camera. Found on screen it
// is read there; off screen it is mirrored in (mode 0), or the city's average colour (mode 1).
float3 GroundFill(float3 P, float3 R, float roughness, float groundY)
{
    float3 city = tex2Dlod(_RGCity, float4(0.5, 0.5, 0.0, 0.0)).rgb;
    float3 result = city;
    if (_RGFill.x > 1.5)
    {
        result = SkyRadiance(R, roughness);
    }
    else if (_RGFill.x < 0.5)
    {
        float drop = max(P.y - groundY, 1.0);
        float t = min(drop / max(-R.y, 0.02), 4000.0);
        float3 G = P + R * t;
        bool finished = _RGFinished.x > 0.5;
        float2 uv;
        if (ScreenUV(G, finished, uv))
        {
            float outside = max(max(-uv.x, uv.x - 1.0), max(-uv.y, uv.y - 1.0));
            float2 muv = MirrorUV(uv);
            // Sharp where the ground point is on screen; blurred the further outside it had to
            // be mirrored from (a mirrored street is a plausible street, not the right one).
            float lod = saturate(outside * 4.0) * 1.5 + saturate(outside) * 2.5 + roughness * 3.0;
            float3 mirrored = MirroredPicture(muv, lod, finished);
            result = lerp(mirrored, city, saturate(outside - 0.6));
        }
    }
    return result;
}

// ---------------------------------------------------------------------------------------------
// The buildings round a facade, where the screen cannot show them: the reflected ray walked across
// the city seen from above (CityMap.cs), cell by cell -- Amanatides & Woo's grid traversal (1987):
// from one cell to the next is two compares and an add. The first cell of another building whose
// roof the ray passes under is what the glass mirrors. 4 m cells for the first ~250 m, then 32 m
// blocks, each its highest roof, out to ~1 km. The same kind of top-down map as CRYENGINE's
// height-map ambient occlusion and Unreal's heightfield sky occlusion of reflections.
// ---------------------------------------------------------------------------------------------

float2 DecodeCity(float4 cell)
{
    float4 b = floor(cell * 255.0 + 0.5);
    return float2((b.r * 256.0 + b.g) * (1.0 / 32.0), b.b + b.a * 256.0);
}

// True when the ray from origin along R runs into a building other than ownId before it leaves the
// map, climbs over every roof or reaches groundY. tHit: metres along R; hitN: the side it met (the
// roof's +y when it came down onto it).
bool CityHit(float3 origin, float3 R, float ownId, float groundY, out float tHit, out float hitId, out float3 hitN)
{
    tHit = 0.0;
    hitId = 0.0;
    hitN = float3(0.0, 1.0, 0.0);
    bool hit = false;
    float top = _RGCityMapInfo.z;
    bool done = R.y >= 0.0 && origin.y >= top;
    float t = 0.0;

    [unroll]
    for (int phase = 0; phase < 2; phase++)
    {
        float block = phase == 0 ? 1.0 : _RGCityMapInfo.y;
        float cellSize = _RGCityMapST.z * block;
        float count = _RGCityMapInfo.x / block;
        float2 p = (origin.xz + R.xz * t - _RGCityMapST.xy) / cellSize;
        float2 d = R.xz / cellSize;
        d = float2(abs(d.x) > 1e-7 ? d.x : 1e-7, abs(d.y) > 1e-7 ? d.y : 1e-7);
        float2 stepDir = float2(d.x > 0.0 ? 1.0 : -1.0, d.y > 0.0 ? 1.0 : -1.0);
        float2 c = floor(p);
        float2 tDelta = abs(1.0 / d);
        float2 tMax = t + (c + max(stepDir, 0.0) - p) / d;
        float side = -1.0;
        float steps = phase == 0 ? _RGBudget.x : _RGBudget.y;

        [loop]
        for (int i = 0; i < 128; i++)
        {
            if (i >= steps)
                break;   // this phase's budget spent: on to the coarse blocks
            if (done || any(c < 0.0) || any(c >= count))
            {
                done = true;
                break;
            }

            float tExit = min(tMax.x, tMax.y);
            float4 texel = phase == 0 ? tex2Dlod(_RGCityMap, float4((c + 0.5) / count, 0.0, 0.0))
                                      : tex2Dlod(_RGCityCoarse, float4((c + 0.5) / count, 0.0, 0.0));
            float2 roof = DecodeCity(texel);
            float yIn = origin.y + R.y * t;
            float yOut = origin.y + R.y * tExit;
            if (roof.y > 0.5 && abs(roof.y - ownId) > 0.5 && min(yIn, yOut) < roof.x)
            {
                hit = true;
                done = true;
                hitId = roof.y;
                if (yIn < roof.x)
                {
                    tHit = t;
                    hitN = side > 0.5 ? float3(0.0, 0.0, -stepDir.y)
                         : (side > -0.5 ? float3(-stepDir.x, 0.0, 0.0) : normalize(float3(-R.x, 0.0, -R.z) + float3(1e-4, 0.0, 0.0)));
                }
                else
                {
                    tHit = (roof.x - origin.y) / min(R.y, -1e-4);
                }
                break;
            }

            // Over every roof and climbing, or down at the ground (the ground fill's): nothing more.
            if ((R.y >= 0.0 && yOut > top) || yOut < groundY)
            {
                done = true;
                break;
            }

            if (tMax.x < tMax.y)
            {
                c.x += stepDir.x;
                t = tMax.x;
                tMax.x += tDelta.x;
                side = 0.0;
            }
            else
            {
                c.y += stepDir.y;
                t = tMax.y;
                tMax.y += tDelta.y;
                side = 1.0;
            }
        }
    }
    return hit;
}

// What a mirrored building looks like from across the street, which the screen never showed from
// that side: its own facade, drawn from what is known of it. seen: the building as the screen shows
// it where the point met falls (blurred; the city's average colour where that is off screen) -- its
// own colours, its haze, and at night its windows as the game lights them. By day that body,
// lighter on its sunny side, its glass mirroring the sky above its horizon and the city below, a
// darker band at every floor slab; by night the seen colour itself: no lights of ours.
// span: pixels a storey of it covers in the reflection -- under a couple, no bands.
float3 NeighbourRadiance(float3 H, float3 R, float3 nb, float span, float roughness, float3 seen)
{
    float3 city = tex2Dlod(_RGCity, float4(0.5, 0.5, 0.0, 0.0)).rgb;
    float detail = saturate(span * 0.5 - 0.5);

    float3 Rn = reflect(R, nb);
    float3 mirrored = Rn.y > 0.0 ? SkyRadiance(Rn, max(roughness, 0.3)) : city;
    float sunSide = saturate(dot(nb, _RGSunDir.xyz)) * saturate(_RGSunDir.w);
    float3 body = seen * (0.7 + 0.6 * sunSide);
    float y = max(H.y - _RGFill.y, 0.0);
    float band = step(frac(y / _RGPane2.z), 0.2) * detail;
    float3 day = lerp(body, mirrored * 0.45 + body * 0.35, 0.6) * (1.0 - 0.3 * band);
    return lerp(seen, day, _RGLight.x);
}

// ---------------------------------------------------------------------------------------------
// The sun's glint: GGX (Walter et al. 2007), capped so a mirror-smooth pane cannot put a firefly
// through the game's bloom.
// ---------------------------------------------------------------------------------------------

float3 SunGlint(float3 n, float3 V, float F, float roughness)
{
    float3 L = _RGSunDir.xyz;
    float nl = saturate(dot(n, L));
    float3 H = normalize(L - V);
    float nh = saturate(dot(n, H));
    float nv = max(dot(n, -V), 0.05);
    float a = max(roughness * roughness, 0.0016);
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    float D = a2 / (3.14159265 * d * d);
    float spec = min(D * F * 0.25 / nv, 60.0);
    return _RGSunColor.rgb * spec * nl * saturate(_RGSunDir.w) * _RGRefl.z;
}

#endif
