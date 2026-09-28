// Real Glass -- everything a glass pixel needs: what counts as glass, the pane it belongs to and
// how that pane is bent, Fresnel, the screen-space trace, what fills a reflection the screen
// cannot give, the rooms behind the glass, and the sun's glint. Included by Glass.shader.
//
// Sources (credited in README.md and LICENSE):
//  - McGuire & Mara, "Efficient GPU Screen-Space Ray Tracing", JCGT 3(4) 2014 (BSD 2-Clause):
//    TraceScreenRay is a port of their traceScreenSpaceRay1.
//  - van Dongen, "Interior Mapping", CGI 2008: RoomRadiance.
//  - Schlick 1994 (Fresnel), Walter et al. 2007 (GGX), Jimenez 2014 (interleaved gradient noise),
//    Hoskins (hash without sine).
//  - Pane bending: insulating-glass "pillowing" (a plate's first mode, Navier) and the roller
//    wave of toughened glass (ASTM C1651).
//  - Layering of the reflection (screen first, then a live cube, one face a frame): Mirror's Edge
//    Catalyst, "Lighting the City of Glass" (GDC 2016); the sky cube: GTA V's per-frame cubemap.

#ifndef REALGLASS_COMMON_INCLUDED
#define REALGLASS_COMMON_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------------------------
// Uniforms. GlassUniforms.cs is the one place that writes them.
// ---------------------------------------------------------------------------------------------

float4 _RGGlass;      // x reflectance head-on (F0), y reflectance edge-on, z clarity (VLT), w strength
float4 _RGTint;       // rgb glass tint (linear, brightest channel 1), a own-colour mix 0..1
float4 _RGMask;       // x shininess threshold, y night-window weight, z show mask (1), w coat tint mix
float4 _RGRefl;       // x reflection brightness, y roughness 0..1, z sun glint, w screen-edge fade 0..0.5
float4 _RGTrace;      // x trace on (1), y max steps, z stride (px), w thickness scale
float4 _RGFill;       // x off-screen fill (0 mirrored screen, 1 city colour, 2 sky), y ground level (world y), z cube mips, w -
float4 _RGPane;       // x pane width (m), y pillow bow (m), z tilt (rad), w roller-wave peak (m)
float4 _RGPane2;      // x bow bias -1 (all sunk) .. +1 (all bulging), y roller-wave length (m), z -, w -
float4 _RGRoom;       // x floor height (m), y room width (m), z room depth (m), w rooms on (1)
float4 _RGRoom2;      // x day brightness, y blinds share, z furniture, w night rooms
float4 _RGLight;      // x daylight 0 (night) .. 1 (day), y night warmth 0..1, z ceiling lights by day, w -
float4 _RGSunDir;     // xyz towards the sun (world), w sun above the horizon 0..1
float4 _RGSunColor;   // rgb the sun's radiance (colour x intensity)
float4 _RGAmbSky;     // rgb light from the sky (trilight "sky")
float4 _RGAmbGround;  // rgb light from below (trilight "ground")
float4 _RGDebug;      // x: 0 off; the GPU preview's checks (1 trace, 2 depth, 3 screen copy). The mod never sets it.

sampler2D _RGScene;           // the lit opaque picture, copied before any glass (mipmapped)
float4 _RGScene_TexelSize;
sampler2D _RGCity;            // 1x1: the average colour of the lower half of the screen
samplerCUBE _RGSky;           // the live sky (or the game's environment map)
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

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
// finer than the pixel is averaged away (never aliased).
float3 PaneNormal(float3 n, float3 t, float3 P, float baseY, float footprint, out float2 paneCell)
{
    float w = _RGPane.x;
    float h = _RGRoom.x;
    float fid = FacadeId(n);
    float2 c = float2(dot(P, t) / w, (P.y - baseY) / h);
    paneCell = floor(c);
    float2 f = c - paneCell;
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
// ---------------------------------------------------------------------------------------------

float GlassFresnel(float cosTheta)
{
    float f = pow(1.0 - saturate(cosTheta), 5.0);
    return _RGGlass.x + (max(_RGGlass.y, _RGGlass.x) - _RGGlass.x) * f;
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
        float4 clip = mul(UNITY_MATRIX_VP, float4(G, 1.0));
        if (clip.w > _ProjectionParams.y)
        {
            float4 s = ComputeScreenPos(clip);
            float2 uv = s.xy / s.w;
            float outside = max(max(-uv.x, uv.x - 1.0), max(-uv.y, uv.y - 1.0));
            float2 muv = MirrorUV(uv);
            // Sharp where the ground point is on screen; blurred the further outside it had to
            // be mirrored from (a mirrored street is a plausible street, not the right one).
            float lod = saturate(outside * 4.0) * 1.5 + saturate(outside) * 2.5 + roughness * 3.0;
            float3 mirrored = tex2Dlod(_RGScene, float4(muv, 0.0, lod)).rgb;
            result = lerp(mirrored, city, saturate(outside - 0.6));
        }
    }
    return result;
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

// ---------------------------------------------------------------------------------------------
// The rooms behind the glass: interior mapping (van Dongen 2008) in the facade's frame. Floors
// every storey from the building's base, walls every roomWidth along the facade, a back wall
// roomDepth in; desks and blinds on top. By day the rooms are lit by the sky through the window
// (bright at the glass, dim at the back) and most offices keep their ceiling lights on; at night
// a room is lit exactly where the GAME lights that window (its own pixel, read from the screen
// copy), so the game still decides which windows are lit and when -- the rooms only give them
// depth.
// ---------------------------------------------------------------------------------------------

struct RoomHit
{
    float3 albedo;
    float depth;      // metres behind the glass
    float ceiling;    // 1 on a ceiling
    float panel;      // 1 on a ceiling light panel
    float desk;       // 1 on a desk top
};

RoomHit TraceRoom(float3 P, float3 V, float3 n, float3 t, float baseY, float fid, float detail, out float roomSeed)
{
    RoomHit o;
    float W = _RGRoom.y, H = _RGRoom.x, D = _RGRoom.z;
    float x = dot(P, t);
    float y = P.y - baseY;
    float3 d = float3(dot(V, t), V.y, -dot(V, n));
    d.z = max(d.z, 0.02);

    float ix = floor(x / W);
    float iy = floor(y / H);
    // A storey is one tenant: its kind, its finishes and whether its lights are on belong to the
    // floor, not to each room -- towers read in floors, not in a checkerboard.
    roomSeed = Hash12(float2(fid * 131.0, iy));
    float r2 = Hash12(float2(iy + 17.0, fid * 7.0 + floor(ix / 4.0)));

    float dx = abs(d.x) > 1e-4 ? d.x : 1e-4;
    float dy = abs(d.y) > 1e-4 ? d.y : -1e-4;
    float tx = ((dx > 0.0 ? ix + 1.0 : ix) * W - x) / dx;
    float ty = ((dy > 0.0 ? iy + 1.0 : iy) * H - y) / dy;
    float tz = D / d.z;
    float tHit = min(tx, min(ty, tz));
    float3 hp = float3(x, y, 0.0) + d * tHit;
    float lx = hp.x - ix * W;           // in the room: 0..W
    float ly = hp.y - iy * H;           // 0..H
    float lz = hp.z;                    // 0..D

    // Storey kinds: offices (grey carpet, white ceiling with light panels) and flats (wood,
    // warm walls) -- 80 / 20.
    bool office = roomSeed < 0.8;
    float3 floorC = office ? lerp(float3(0.16, 0.17, 0.18), float3(0.30, 0.29, 0.27), r2)
                           : lerp(float3(0.28, 0.18, 0.11), float3(0.45, 0.33, 0.22), r2);
    float3 wallC = office ? lerp(float3(0.62, 0.63, 0.64), float3(0.78, 0.77, 0.74), frac(r2 * 7.1))
                          : lerp(float3(0.66, 0.60, 0.52), float3(0.80, 0.74, 0.64), frac(r2 * 5.3));
    float3 backC = wallC * lerp(0.7, 1.0, frac(roomSeed * 13.7));
    float3 ceilC = float3(0.78, 0.78, 0.77);

    o.depth = lz;
    o.ceiling = 0.0;
    o.panel = 0.0;
    o.desk = 0.0;
    if (tHit == ty)
    {
        if (dy > 0.0)
        {
            o.albedo = ceilC;
            o.ceiling = 1.0;
            // Office ceilings: 0.6 x 1.2 m light panels every 2.4 x 1.8 m.
            float2 g = frac(float2(lx / 2.4, lz / 1.8));
            float inPanel = step(abs(g.x - 0.5), 0.125) * step(abs(g.y - 0.5), 0.33);
            o.panel = office ? inPanel * detail : 0.0;
        }
        else
        {
            o.albedo = floorC;
        }
    }
    else if (tHit == tz)
    {
        o.albedo = backC;
    }
    else
    {
        o.albedo = wallC;
    }

    // Desks: a row of worktops 0.75 m up, 0.6..2.4 m from the glass, in 1.6 m runs.
    if (office && _RGRoom2.z > 0.0 && dy < 0.0)
    {
        float deskY = 0.75;
        float ly0 = y - iy * H;
        float td = (deskY - ly0) / dy;
        if (td > 0.0 && td < tHit)
        {
            float3 dp = float3(x, ly0, 0.0) + d * td;
            float dz = dp.z;
            float along = frac((dp.x - ix * W) / 2.0 + roomSeed);
            float present = step(Hash12(float2(ix * 3.0 + floor(along * 2.0), iy)), _RGRoom2.z);
            if (dz > 0.6 && dz < 2.4 && along < 0.8 && present > 0.5)
            {
                o.albedo = lerp(o.albedo, lerp(float3(0.72, 0.72, 0.70), float3(0.46, 0.34, 0.24), frac(roomSeed * 3.3)), detail);
                o.depth = dz;
                o.desk = detail;
                o.ceiling = 0.0;
                o.panel = 0.0;
            }
        }
    }
    return o;
}

// Blinds hang just inside the glass: a window (one pane, one storey) has them down over the top
// part of its height, or not at all.
float BlindCover(float2 paneCell, float yInStorey, float fid)
{
    float2 r = Hash22(paneCell * float2(1.0, 7.0) + fid * 0.37);
    float drop = 0.25 + 0.75 * r.y;   // share of the height, from the top
    return step(r.x, _RGRoom2.y - 1e-4) * step(1.0 - drop, yInStorey);
}

// The radiance leaving the glass from inside (before the glass's own tint and clarity), by day
// and by night separately (the glass treats them differently: by day the rooms are seen through
// its tint and clarity; the game's lit windows are already painted as seen from outside).
// gamePixel: the game's own colour for this pixel (the screen copy) -- its lit windows at night.
void RoomRadiance(float3 P, float3 V, float3 n, float3 t, float baseY, float footprint,
                  float2 paneCell, float3 gamePixel, out float3 dayOut, out float3 nightOut)
{
    float fid = FacadeId(n);
    float W = _RGRoom.y, H = _RGRoom.x;
    // Rooms smaller than a few pixels are averaged: no shimmer at a distance.
    float detail = saturate(min(W, H) / max(footprint * 6.0, 1e-4) - 0.5);
    float deskDetail = saturate(0.8 / max(footprint * 3.0, 1e-4) - 0.3);

    float roomSeed;
    RoomHit hit = TraceRoom(P, V, n, t, baseY, fid, deskDetail, roomSeed);

    float3 sky = _RGAmbSky.rgb;
    // Day: the window lights the room; the back is a fraction of the front.
    float fall = 0.3 + 0.7 * exp(-hit.depth / 3.5);
    float3 dayLight = sky * fall * (hit.ceiling > 0.5 ? 0.55 : 0.9);
    // Offices keep their lights on in daytime, a floor at a time.
    bool office = roomSeed < 0.8;
    float lightsOn = (office && frac(roomSeed * 29.1) < _RGLight.z) ? 1.0 : 0.0;
    float3 lamp = lerp(float3(0.95, 0.98, 1.0), float3(1.0, 0.82, 0.60), _RGLight.y);
    float3 dayLamps = lamp * lightsOn * (0.12 + 1.2 * hit.panel) * max(Luma(sky), 0.05) * 0.5;
    float3 day = hit.albedo * (dayLight + dayLamps) * _RGRoom2.x;

    // Night: the game's own lit window, shaped into a room. Its mean over a room is kept (the
    // weights average ~1), so a far window is as bright as the game made it.
    float shape = (hit.ceiling > 0.5 ? 1.15 : (hit.desk > 0.5 ? 0.95 : (hit.depth < 0.05 ? 0.9 : 0.85)));
    shape += hit.panel * 2.5;
    float3 night = gamePixel * lerp(1.0, shape * lerp(1.0, 1.35, saturate(hit.albedo.r)), detail) * _RGRoom2.w;

    // Blinds: lit from outside by day, glowing from inside at night.
    float storeyY = frac((P.y - baseY) / H);
    float blind = BlindCover(paneCell, storeyY, fid) * saturate(detail * 2.0);
    float3 blindC = lerp(float3(0.62, 0.60, 0.56), float3(0.82, 0.80, 0.76), frac(roomSeed * 5.7));
    day = lerp(day, blindC * sky * 0.85 * _RGRoom2.x, blind);
    night = lerp(night, gamePixel * 0.8 * _RGRoom2.w, blind);

    // Far away: the average room.
    float3 avgDay = float3(0.42, 0.42, 0.41) * sky * 0.6 * _RGRoom2.x;
    dayOut = lerp(avgDay, day, detail);
    nightOut = lerp(gamePixel * _RGRoom2.w, night, detail);
}

#endif
