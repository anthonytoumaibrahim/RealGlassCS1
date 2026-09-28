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
//    Catalyst, "Lighting the City of Glass" (GDC 2016); the sky cube: GTA V's per-frame cubemap,
//    with the clouds drawn into it as Unreal's Sky Light captures volumetric clouds.
//  - What is mirrored is last frame's FINISHED picture, reprojected (Killzone Shadow Fall, GDC
//    2014): fog, clouds, the lamps' glow and the other glass are in the reflections.
//  - Lamp colours: black bodies (Kim et al.'s cubic fit of the Planckian locus, 2002), computed in
//    GlassUniforms.cs; with the warm shift the author chose, about 2700-2900 K.
//  - The buildings round, off screen: a top-down height map of the city round the view (as
//    CRYENGINE's height-map ambient occlusion and Unreal's heightfield sky occlusion of
//    reflections read one), walked cell by cell (Amanatides & Woo, Eurographics 1987): CityHit.
//  - Pictured rooms: interior cubemaps -- a room painted onto its own walls from its centre, as
//    Unreal's InteriorCubemap samples a captured room and as Marvel's Spider-Man paints its desks
//    onto the walls (van Dongen 2018); the rooms rendered for this mod (Assets/Editor/RoomBaker.cs).
//  - Steady glints: specular anti-aliasing (Kaplanyan et al. 2016; Tokuyoshi & Kaplanyan 2019),
//    Filament's constants.

#ifndef REALGLASS_COMMON_INCLUDED
#define REALGLASS_COMMON_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------------------------
// Uniforms. GlassUniforms.cs is the one place that writes them.
// ---------------------------------------------------------------------------------------------

float4 _RGGlass;      // x reflectance head-on (F0, the chosen glass), y reflectance edge-on, z clarity (VLT), w strength
float4 _RGGlass2;     // x silvery edge-on 0..1, y -, z floor band (m), w -
float4 _RGColorMap;   // xy the game's object colour map (width, height): where a building's colour index points
float4 _RGLamps[5];   // lamp colours, luma 1: 5000, 4400, 3800, 3200, 2700 K (seen with a 4000 K white)
float4 _RGFinished;   // x 1 while _RGSceneFinished holds last frame's finished picture
float4 _RGTint;       // rgb glass tint (linear, brightest channel 1), a own-colour mix 0..1
float4 _RGMask;       // x shininess threshold, y night-window weight, z show mask (1), w coat tint mix
float4 _RGRefl;       // x reflection brightness, y roughness 0..1, z sun glint, w screen-edge fade 0..0.5
float4 _RGTrace;      // x trace on (1), y max steps, z stride (px), w thickness scale
float4 _RGFill;       // x off-screen fill (0 mirrored screen, 1 city colour, 2 sky), y ground level (world y), z cube mips, w -
float4 _RGPane;       // x pane width (m), y pillow bow (m), z tilt (rad), w roller-wave peak (m)
float4 _RGPane2;      // x bow bias -1 (all sunk) .. +1 (all bulging), y roller-wave length (m), z -, w -
float4 _RGRoom;       // x floor height (m), y room width (m), z room depth (m), w rooms on (1)
float4 _RGRoom2;      // x day brightness, y blinds share, z furniture, w lit rooms at night (x the game's lit window)
float4 _RGLight;      // x daylight 0 (night) .. 1 (day), y the shift of every lamp's warmth, z ceiling lights by day, w -
float4 _RGSunDir;     // xyz towards the sun (world), w sun above the horizon 0..1
float4 _RGSunColor;   // rgb the sun's radiance (colour x intensity)
float4 _RGAmbSky;     // rgb light from the sky (trilight "sky")
float4 _RGAmbGround;  // rgb light from below (trilight "ground")
float4 _RGFeatures;   // x the buildings round in the reflections, y pictured rooms, z rooms follow the painted windows, w steady glints
float4 _RGDebug;      // x: 0 off; the GPU preview's checks (1 trace, 2 depth, 3 screen copy, 4 plain-glass towers in
                      // white, 5 the finished picture reprojected, magenta off it; 6 the city walk: green a building
                      // met, blue none, red no map; 7 the pictured room kinds; 8 the painted window found: green
                      // yes, red the storey grid). The mod never sets it.

// The city from above (CityMap.cs): per cell the highest roof over it and whose it is.
sampler2D _RGCityMap;         // rg the roof's world height in 1/32 m, ba the building's id (0 none): 4 m cells
sampler2D _RGCityCoarse;      // the same for blocks of 8 x 8 cells: each block's highest roof
float4 _RGCityMapST;          // xy the map's corner (world x, z), z metres a cell, w 1 while a map is bound
float4 _RGCityMapInfo;        // x cells a side, y cells a block, z the highest roof in the map (world y)

// The pictured rooms (Assets/Editor/RoomBaker.cs): twelve rooms by day and by night, each a cube of
// six 112 px faces seen from its centre; face f of capture c (room k by day 2k, by night 2k + 1) is
// tile t = 6c + f at (t mod 9, t div 9) of the 1024 x 2048 atlas, rows from the bottom.
sampler2D _RGRooms;
#define RG_ROOM_TILE 112.0
#define RG_ROOM_PER_ROW 9.0
#define RG_ROOM_ATLAS float2(1024.0, 2048.0)
#define RG_ROOM_DAY_MEAN 0.25
#define RG_ROOM_NIGHT_MEAN 0.30
static const float3 RG_ROOM_SIZE = float3(12.0, 3.4, 12.0);   // the modelled room: along the facade, up, in

sampler2D _RGScene;           // the lit opaque picture, copied before any glass (mipmapped): what is UNDER a pane
float4 _RGScene_TexelSize;
sampler2D _RGSceneFinished;   // last frame's finished picture (fog, clouds, lamps, glass; mipmapped): what a pane MIRRORS
float4x4 _RGPrevVP;           // last frame's camera (OpenGL-convention projection x view): where a point was on it
sampler2D _RGCity;            // 1x1: the average colour of the lower half of the screen
samplerCUBE _RGSky;           // the live sky (or the game's environment map)
sampler2D _RGBuildings;       // per building, laid out as the game's object colour map: r lamp warmth, a 1 = known
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
// Which building a pixel is: its place in the game's object colour map (the colour index every
// distance tier carries, a sub-building its parent's), turned into a number of its own, and its
// lamps.
// ---------------------------------------------------------------------------------------------

float BuildingSeed(float2 colourIndex)
{
    // The index is a texel centre, (k + 0.5) / size: exact on every tier.
    float2 cell = floor(colourIndex * _RGColorMap.xy);
    return Hash12(cell + float2(0.37, 0.71));
}

// A lamp's colour, luma 1: warmth 0 (5000 K) .. 1 (2700 K).
float3 LampColour(float warmth)
{
    float f = saturate(warmth) * 4.0;
    int i = min((int)f, 3);
    return lerp(_RGLamps[i].rgb, _RGLamps[i + 1].rgb, f - i);
}

// What the building is, as BuildingLights.cs files it: offices 0.2, industry 0.1, shops 0.65, homes
// 0.9, anything else 0.45. A building the table does not know gets a value of its own, from its seed.
float BuildingUse(float2 colourIndex, float seed)
{
    float4 d = tex2Dlod(_RGBuildings, float4(colourIndex, 0.0, 0.0));
    return d.a > 0.5 ? d.r : 0.15 + 0.7 * seed;
}

// 0 cool (offices) .. 1 warm (homes): what the building is, the shift on top (GlassUniforms.LampShift).
float BuildingWarmth(float2 colourIndex, float seed)
{
    return saturate(BuildingUse(colourIndex, seed) + _RGLight.y);
}

// A building's colour index from its id (RenderManager.GetColorLocation), and back.
float2 ColourIndexOf(float id)
{
    float W = _RGColorMap.x;
    float row = floor((id + 0.5) / W);
    return float2((id - row * W + 0.5) / W, (row + 0.5) / _RGColorMap.y);
}

float IdOf(float2 colourIndex)
{
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
        int steps = phase == 0 ? 64 : 24;

        [loop]
        for (int i = 0; i < steps; i++)
        {
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
// own colours, its haze -- for the body. By day that body, lighter on its sunny side, its glass
// mirroring the sky above its horizon and the city below, a darker band at every floor slab; at
// night darker, with its windows lit or not bay by bay and storey by storey, in its lamps' colour.
// span: pixels a storey of it covers in the reflection -- under a couple, the average.
float3 NeighbourRadiance(float3 H, float3 R, float3 nb, float hitId, float span, float roughness, float3 seen)
{
    float2 ci = ColourIndexOf(hitId);
    float seed = BuildingSeed(ci);
    float3 lamp = LampColour(BuildingWarmth(ci, seed));
    float3 city = tex2Dlod(_RGCity, float4(0.5, 0.5, 0.0, 0.0)).rgb;
    float detail = saturate(span * 0.5 - 0.5);

    float3 Rn = reflect(R, nb);
    float3 mirrored = Rn.y > 0.0 ? SkyRadiance(Rn, max(roughness, 0.3)) : city;
    float sunSide = saturate(dot(nb, _RGSunDir.xyz)) * saturate(_RGSunDir.w);
    float3 body = seen * (0.7 + 0.6 * sunSide);
    float storeyH = _RGRoom.x;
    float y = max(H.y - _RGFill.y, 0.0);
    float along = dot(H.xz, float2(nb.z, -nb.x));
    float2 w = frac(float2(along / 3.0, y / storeyH));
    float band = step(w.y, 0.2) * detail;
    float3 day = lerp(body, mirrored * 0.45 + body * 0.35, 0.6) * (1.0 - 0.3 * band);

    float storey = floor(y / storeyH);
    float bay = floor(along / 3.0);
    float lit = step(Hash12(float2(bay * 1.37 + hitId * 0.071, storey * 7.13 + seed * 91.0)), 0.5);
    float glassArea = step(0.12, w.x) * step(w.x, 0.88) * step(0.28, w.y) * step(w.y, 0.92);
    float windows = lerp(0.5 * 0.5, lit * glassArea, detail);
    float3 night = seen * 0.35 + lamp * windows * 1.2;
    return lerp(night, day, _RGLight.x);
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
// roomDepth in; desks and blinds on top; on a tower of plain glass, the floor slab's band at the
// top of every storey. By day the rooms are lit by the sky through the window (bright at the
// glass, dim at the back) and most offices keep their ceiling lights on. At night the GAME still
// decides which windows are lit, how brightly and when (its own pixel, read from the screen
// copy); the rooms say how that light falls, in the colour of the building's lamps.
// ---------------------------------------------------------------------------------------------

struct RoomHit
{
    float3 albedo;
    float depth;      // metres behind the glass
    float ceiling;    // 1 on a ceiling
    float panel;      // 1 on a ceiling light panel
    float desk;       // 1 on a desk top
    float onFloor;    // 1 on the floor
    float slab;       // 1 on the floor slab's edge: the band between storeys
    float3 local;     // the point met, in the room: x along the facade 0..W, y up 0..ceilH, z in 0..D
    float atGlass;    // the pixel's own height above the room's floor, at the glass
    float ceilH;      // the room's floor-to-ceiling height here
    float bay;        // which room along the facade
    float storey;     // which storey (counted from the building's base)
};

// slab: metres at the top of every storey taken by the slab and the ceiling void (0: rooms from
// floor to floor). seed: the building's. homes: 1 where the building is homes (its storeys are
// flats, a few offices), 0 where it is not (offices, a few flats). window: the floor and ceiling
// (world y) of the window the asset painted round this pixel, when one was found (else 0, 0: the
// storeys count up from the base).
RoomHit TraceRoom(float3 P, float3 V, float3 n, float3 t, float baseY, float fid, float detail, float slab,
                  float seed, float homes, float2 window, out float roomSeed)
{
    RoomHit o;
    o.albedo = float3(0.0, 0.0, 0.0);
    o.depth = 0.0;
    o.ceiling = 0.0;
    o.panel = 0.0;
    o.desk = 0.0;
    o.onFloor = 0.0;
    o.slab = 0.0;

    float W = _RGRoom.y, H = _RGRoom.x, D = _RGRoom.z;
    float x = dot(P, t);
    float y = P.y - baseY;
    float3 d = float3(dot(V, t), V.y, -dot(V, n));
    d.z = max(d.z, 0.02);

    float ix = floor(x / W);
    float iy = floor(y / H);
    float ly0 = y - iy * H;            // height above this storey's floor, at the glass
    float ceilH = H - slab;            // the room's ceiling; above it the ceiling void and the slab
    if (window.y > window.x + 0.5)
    {
        // The window the asset painted sets this room's floor and ceiling.
        iy = floor((window.x - baseY) / H + 0.5);
        ly0 = P.y - window.x;
        ceilH = window.y - window.x;
    }
    o.local = float3(0.0, ly0, 0.0);
    o.atGlass = ly0;
    o.ceilH = ceilH;
    o.bay = ix;
    o.storey = iy;
    // A storey is one tenant: its kind, its finishes and whether its lights are on belong to the
    // floor, not to each room -- towers read in floors, not in a checkerboard. Every building its own.
    roomSeed = Hash12(float2(fid * 131.0 + seed * 977.0, iy));
    float r2 = Hash12(float2(iy + 17.0, fid * 7.0 + floor(ix / 4.0) + seed * 131.0));

    float dx = abs(d.x) > 1e-4 ? d.x : 1e-4;
    float dy = abs(d.y) > 1e-4 ? d.y : -1e-4;
    float tx = ((dx > 0.0 ? ix + 1.0 : ix) * W - x) / dx;
    float ty = ((dy > 0.0 ? ceilH : 0.0) - ly0) / dy;
    float tz = D / d.z;
    float tHit = min(tx, min(ty, tz));
    float lx = x + d.x * tHit - ix * W;   // in the room: 0..W
    float lz = d.z * tHit;                // 0..D
    o.local = float3(lx, clamp(ly0 + d.y * tHit, 0.0, ceilH), lz);

    // Storey kinds: offices (grey carpet, white ceiling with light panels) and flats (wood,
    // warm walls): mostly what the building is, a few of the other.
    bool office = roomSeed < lerp(0.9, 0.1, homes);
    float3 floorC = office ? lerp(float3(0.16, 0.17, 0.18), float3(0.30, 0.29, 0.27), r2)
                           : lerp(float3(0.28, 0.18, 0.11), float3(0.45, 0.33, 0.22), r2);
    float3 wallC = office ? lerp(float3(0.62, 0.63, 0.64), float3(0.78, 0.77, 0.74), frac(r2 * 7.1))
                          : lerp(float3(0.66, 0.60, 0.52), float3(0.80, 0.74, 0.64), frac(r2 * 5.3));
    float3 backC = wallC * lerp(0.7, 1.0, frac(roomSeed * 13.7));
    float3 ceilC = float3(0.78, 0.78, 0.77);

    o.depth = lz;
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
            o.onFloor = 1.0;
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
                o.onFloor = 0.0;
            }
        }
    }

    // Above the ceiling: the slab's edge, right behind the glass. (One exit: fxc takes an early
    // return in an inlined helper for "potentially uninitialized".)
    if (ly0 > ceilH)
    {
        o.albedo = float3(0.035, 0.037, 0.04);
        o.slab = 1.0;
        o.depth = 0.0;
        o.ceiling = 0.0;
        o.panel = 0.0;
        o.desk = 0.0;
        o.onFloor = 0.0;
        o.local = float3(0.0, ly0, 0.0);
    }
    return o;
}

// Blinds hang just inside the glass: a room has them down over the top part of its windows, or not
// at all -- one room's alike, each pane at its own height near the room's.
float BlindCover(float2 roomCell, float2 paneCell, float yInStorey, float fid)
{
    float2 r = Hash22(roomCell * float2(1.0, 7.0) + fid * 0.37);
    float pane = Hash12(paneCell * float2(3.1, 1.7) + fid * 0.11);
    float drop = saturate(0.25 + 0.75 * r.y + 0.12 * (pane - 0.5));   // share of the height, from the top
    return step(r.x, _RGRoom2.y - 1e-4) * step(1.0 - drop, yInStorey);
}

// ---------------------------------------------------------------------------------------------
// Pictured rooms: which face of a room's cube a direction from its centre meets, and where on it
// (RoomBaker.cs's convention: u along the capturing camera's right, v along its up); where that
// face is in the atlas; and what a storey of a building is.
// ---------------------------------------------------------------------------------------------

float2 RoomFaceUV(float3 d, out float face)
{
    float3 a = abs(d);
    float2 uv;
    if (a.z >= a.x && a.z >= a.y)
    {
        face = d.z > 0.0 ? 0.0 : 1.0;
        uv = float2(d.z > 0.0 ? d.x : -d.x, d.y) / max(a.z, 1e-5);
    }
    else if (a.x >= a.y)
    {
        face = d.x > 0.0 ? 2.0 : 3.0;
        uv = float2(d.x > 0.0 ? -d.z : d.z, d.y) / max(a.x, 1e-5);
    }
    else
    {
        face = d.y > 0.0 ? 4.0 : 5.0;
        uv = float2(d.y > 0.0 ? -d.x : d.x, d.z) / max(a.y, 1e-5);
    }
    return uv * 0.5 + 0.5;
}

float2 RoomAtlasUV(float capture, float face, float2 uv, float lod)
{
    float tile = capture * 6.0 + face;
    float row = floor((tile + 0.5) / RG_ROOM_PER_ROW);
    float col = tile - row * RG_ROOM_PER_ROW;
    // Stay inside the tile at this mip: its neighbours in the atlas are other faces.
    float inset = (0.5 * exp2(lod) + 0.5) / RG_ROOM_TILE;
    uv = clamp(uv, inset, 1.0 - inset);
    return (float2(col, row) + uv) * (RG_ROOM_TILE / RG_ROOM_ATLAS);
}

// The pictured room of a storey, from what the building is (BuildingUse): 0 an open-plan office,
// 1 an office of desk clusters, 2 a meeting room, 3 small offices, 4 a living room, 5 a living and
// dining room, 6 a bedroom, 7 a kitchen, 8 a shop, 9 a restaurant, 10 a lobby, 11 an empty floor.
// Offices are a floor at a time (a storey is one tenant), a meeting room here and there; homes are a
// room at a time; shops and offices have a lobby, a shop or a restaurant on the ground floor.
float RoomKind(float use, float storey, float bay, float roomSeed, float seed)
{
    float perBay = Hash12(float2(bay * 1.7 + seed * 53.0, storey * 3.1 + 0.5));
    float office = frac(roomSeed * 7.3);
    float officeKind = office < 0.35 ? 0.0 : (office < 0.65 ? 1.0 : (office < 0.8 ? 3.0 : 2.0));
    officeKind = perBay < 0.12 ? 2.0 : officeKind;
    officeKind = roomSeed < 0.05 ? 11.0 : officeKind;
    float kind;
    if (use > 0.8)
        kind = 4.0 + floor(perBay * 3.999);
    else if (use > 0.55)
        kind = storey < 0.5 ? (perBay < 0.6 ? 8.0 : 9.0) : officeKind;
    else if (use < 0.15)
        kind = roomSeed < 0.5 ? 11.0 : floor(office * 1.999);
    else
        kind = storey < 0.5 ? 10.0 : officeKind;
    return kind;
}

// The radiance leaving the glass from inside (before the glass's own tint and clarity), by day
// and by night separately (the glass treats them differently: by day the rooms are seen through
// its tint and clarity; the game's lit windows are already painted as seen from outside).
// gamePixel: the game's own colour for this pixel (the screen copy) -- its lit windows at night.
// seed, warmth, use: the building's (BuildingSeed, BuildingWarmth, BuildingUse). box: 1 on a tower
// of plain glass, whose only storeys are the rooms' (Glass.shader's GlassBox). window: the painted
// window's floor and ceiling (Glass.shader's PaintedWindow), or 0, 0.
void RoomRadiance(float3 P, float3 V, float3 n, float3 t, float baseY, float footprint,
                  float2 paneCell, float3 gamePixel, float seed, float warmth, float use, float box, float2 window,
                  out float3 dayOut, out float3 nightOut)
{
    float fid = FacadeId(n);
    float W = _RGRoom.y, H = _RGRoom.x;
    // Rooms smaller than a few pixels are averaged: no shimmer at a distance.
    float detail = saturate(min(W, H) / max(footprint * 6.0, 1e-4) - 0.5);
    float deskDetail = saturate(0.8 / max(footprint * 3.0, 1e-4) - 0.3);
    // The floor band, on plain glass only: drawn while a storey is several pixels tall, and further
    // out as its share of the storey.
    float slab = min(_RGGlass2.z, H * 0.6) * box;
    float slabShare = slab / H;
    float homes = saturate((warmth - 0.55) * 4.0);

    float roomSeed;
    RoomHit hit = TraceRoom(P, V, n, t, baseY, fid, deskDetail, slab * detail, seed, homes, window, roomSeed);
    float3 lamp = LampColour(warmth);

    float3 sky = _RGAmbSky.rgb;
    // Day: the window lights the room; the back is a fraction of the front.
    float fall = 0.3 + 0.7 * exp(-hit.depth / 3.5);
    float3 dayLight = sky * fall * (hit.ceiling > 0.5 ? 0.55 : 0.9);
    // Offices keep their lights on in daytime, a floor at a time.
    bool office = roomSeed < lerp(0.9, 0.1, homes);
    float lightsOn = (office && frac(roomSeed * 29.1) < _RGLight.z) ? 1.0 : 0.0;
    float3 dayLamps = lamp * lightsOn * (0.12 + 1.2 * hit.panel) * max(Luma(sky), 0.05) * 0.5;
    float3 day = hit.albedo * (dayLight + dayLamps) * _RGRoom2.x;
    if (hit.slab > 0.5)
        day = hit.albedo * sky * _RGRoom2.x;   // the slab's face looks out: the sky lights it

    // Night: the game decides which windows are lit, how brightly and when (its own timing, per
    // building), and every window it lights is a lit room; the room says how that light falls --
    // the ceiling and its panels bright, the floor under them, the back of the room a little less,
    // the desks catching it -- in the colour of the building's lamps with a little of the game's own
    // hue, at _RGRoom2.w times the game's brightness (GlassUniforms.NightRooms: at full, the
    // author's call). The weights average about 1 over a room.
    float gameLum = Luma(gamePixel);
    float3 light = lerp(lamp, gamePixel / max(gameLum, 1e-4), 0.25);
    light /= max(Luma(light), 0.05);

    float w = hit.ceiling > 0.5 ? 1.3 + hit.panel * 5.0
            : (hit.desk > 0.5 ? 1.25
            : (hit.onFloor > 0.5 ? 0.7
            : 0.85 * lerp(1.0, 0.8, saturate(hit.depth / max(_RGRoom.z, 1.0)))));
    w = hit.slab > 0.5 ? 0.04 : w;
    // Where the asset painted windows of its own, the rooms' storeys need not match them: a
    // gentler shape there.
    float structure = lerp(0.6, 1.0, box) * detail;
    float3 lit = light * gameLum * _RGRoom2.w;
    float3 night = lit * lerp(1.0, w, structure);

    if (_RGFeatures.y > 0.5 && hit.slab < 0.5)
    {
        // A pictured room: the point met on this room's walls, floor or ceiling, as a direction
        // from its centre in the modelled room's own measure, read on that room's cube -- by day as
        // the window lights it, by night by its own lamps. Every other room along a storey is the
        // mirror image. Its light in the building's lamp colour; offices and shops keep theirs on
        // by day, a floor at a time.
        float kind = RoomKind(use, hit.storey, hit.bay, roomSeed, seed);
        float3 m = hit.local / float3(W, max(hit.ceilH, 0.5), max(_RGRoom.z, 0.5)) * RG_ROOM_SIZE;
        float3 dir = m - RG_ROOM_SIZE * 0.5;
        dir.x = frac(hit.bay * 0.5) > 0.25 ? -dir.x : dir.x;
        float face;
        float2 faceUV = RoomFaceUV(dir, face);
        float lod = clamp(log2(max(footprint * (1.0 + hit.depth * 0.05) * (RG_ROOM_TILE / 12.0), 1e-3)), 0.0, 3.0);
        float3 dayPic = tex2Dlod(_RGRooms, float4(RoomAtlasUV(kind * 2.0, face, faceUV, lod), 0.0, lod)).rgb;
        float3 nightPic = tex2Dlod(_RGRooms, float4(RoomAtlasUV(kind * 2.0 + 1.0, face, faceUV, lod), 0.0, lod)).rgb;
        float lampsByDay = (kind < 3.5 && frac(roomSeed * 29.1) < _RGLight.z) || (kind > 7.5 && kind < 10.5) ? 1.0 : 0.0;
        day = (dayPic * sky * (0.25 / RG_ROOM_DAY_MEAN)
             + nightPic * lamp * lampsByDay * max(Luma(sky), 0.05) * (0.1 / RG_ROOM_NIGHT_MEAN)) * _RGRoom2.x;
        night = nightPic * lit * (1.0 / RG_ROOM_NIGHT_MEAN);
    }

    // Blinds: lit from outside by day, glowing from inside at night. They hang over the window,
    // not the band.
    float storeyY = saturate(hit.atGlass / max(hit.ceilH, 0.5));
    float blind = BlindCover(float2(hit.bay, hit.storey), paneCell, storeyY, fid) * saturate(detail * 2.0) * (1.0 - hit.slab);
    float3 blindC = lerp(float3(0.62, 0.60, 0.56), float3(0.82, 0.80, 0.76), frac(roomSeed * 5.7));
    day = lerp(day, blindC * sky * 0.85 * _RGRoom2.x, blind);
    night = lerp(night, lit * 0.8, blind);

    // Far away: the average room, the floor band as its share of the storey.
    float3 avgDay = float3(0.42, 0.42, 0.41) * sky * 0.6 * _RGRoom2.x * (1.0 - 0.9 * slabShare);
    dayOut = lerp(avgDay, day, detail);
    nightOut = lerp(lit * (1.0 - slabShare), night, detail);
}

#endif
