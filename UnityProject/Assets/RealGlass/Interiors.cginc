// Real Glass -- the rooms behind the glass (Glass/Interiors.cs, Glass/RoomAtlas.cs). Included by
// Glass.shader after GlassCommon.cginc. What a window shows of the room behind it: a picture of a
// room (by night lit by its own lamp, by day by the sky), the room's curtain or blind in front of it,
// and whether its light is on -- each chosen per room, so no two neighbours agree by more than
// chance.
//
// Sources (credited in README.md and THIRD-PARTY-NOTICES.md):
//  - The layers -- a room picture, a curtain or blind of its own that lets light through at night,
//    the glass in front; per room a picture from a group of its building's kind; far away one flat
//    picture: Forza Horizon 4's windows (Harwood, "A window into Playground Games' latest shader
//    development", Game Developer 2019).
//  - The room a box behind the window, its walls, floor and ceiling met by the view's ray: Joost van
//    Dongen, "Interior Mapping", CGI 2008; each room ONE picture of its box seen from in front of the
//    window, read back through that picture's camera: SimCity (Maxis 2013, Andrew Willmott), as Ben
//    Golus wrote it up (Unity forums, "Interior Mapping", 2016). RoomPictureUV.
//  - Lit windows scattered at random, some office buildings a whole floor at a time: Young's
//    procedural city (2009). Which rooms are lit, by the hour: Interiors.cs.
//  - The lamps' colours: Mitchell Charity's blackbody colour table (CIE 1931 2 deg, linear sRGB).
//  - The pictures: Poly Haven's indoor panoramas (CC0), painted on the box from the room's middle and
//    graded by tools/interiors.

#ifndef REALGLASS_INTERIORS_INCLUDED
#define REALGLASS_INTERIORS_INCLUDED

float4 _RGRooms;          // x rooms on every building (1), y by day too (1), z the clock (game hours), w game hours a room keeps its light (0: for good)
float4 _RGRooms2;         // x a lit room's average light (brightness x Interiors.LitRoomLuminance), y the coloured lights' share, z the curtains' share, w pictures loaded (1)
float4 _RGRooms3;         // x room depth: 0 a flat picture on the glass .. 1 a box room behind it; y 1: up close the storey grid too (the LODs' rooms)
float4 _RGRoomShare;      // the share of rooms lit now: x homes, y offices and industry, z shops, w hotels, leisure, public
float4 _RGRoomAtlasInfo;  // x tiles a row, y a night tile's pixels, z a day tile's pixels, w the back wall's share of a picture
float4 _RGRoomPools[8];   // per kind of room (Interiors.Pool*): x its first tile, y how many, z its box's aspect, w its depth (half-heights)
sampler2D _RGRoomAtlas;   // the rooms by night, each lit by its own lamps in a neutral white (sRGB DXT1)
sampler2D _RGRoomAtlasDay;// the same rooms by day, in the same places

#define RG_ROOM_TILE_MEAN 0.18   // every picture's mean luminance, linear (tools/interiors/render-rooms.py)
#define RG_ROOM_OVERSCAN 1.25    // a room's front this much larger than the window the asset painted

// Charity's blackbody colours, linear sRGB, red 1 (bbr_color.txt, 2 deg).
static const float3 RG_K2200 = float3(1.0, 0.3126, 0.0219);
static const float3 RG_K2700 = float3(1.0, 0.4250, 0.0889);
static const float3 RG_K3000 = float3(1.0, 0.4870, 0.1411);
static const float3 RG_K3500 = float3(1.0, 0.5809, 0.2433);
static const float3 RG_K4000 = float3(1.0, 0.6636, 0.3583);
static const float3 RG_K5000 = float3(1.0, 0.7992, 0.6045);
static const float3 RG_K6500 = float3(1.0, 0.9436, 0.9621);

// A random number per (seed, k), 0..1.
float RoomRand(float seed, float k)
{
    return Hash12(float2(seed * 4096.0, k));
}

// The building's kind and its asset's own switch from its _RGAssets texel's a channel (Interiors.cs):
// kind + 16 x switch (0 the global one, 1 off, 2 on); 255 not looked at yet (a home, the global switch).
void RoomKind(float a, out float kind, out float assetSwitch)
{
    float a8 = floor(a * 255.0 + 0.5);
    bool unseen = a8 > 254.5;
    kind = unseen ? 0.0 : fmod(a8, 16.0);
    assetSwitch = unseen ? 0.0 : floor(a8 / 16.0);
}

// Which pictures a room of this kind on this storey takes (Interiors.Pool*), and which share of the
// hour it follows (one-hot: homes, offices, shops, the rest).
float RoomPool(float kind, float storey, out float4 group)
{
    bool ground = storey < 0.5;
    float pool = 0.0;
    group = float4(1.0, 0.0, 0.0, 0.0);
    if (kind > 0.5 && kind < 1.5)        { pool = 1.0; group = float4(0.0, 1.0, 0.0, 0.0); }   // offices
    else if (kind > 1.5 && kind < 2.5)   { if (ground) { pool = 2.0; group = float4(0.0, 0.0, 1.0, 0.0); } }   // shop, homes above
    else if (kind > 2.5 && kind < 3.5)   { pool = ground ? 4.0 : 3.0; group = float4(0.0, 0.0, 0.0, 1.0); }   // hotel: lobby, rooms
    else if (kind > 3.5 && kind < 4.5)   { pool = 4.0; group = float4(0.0, 0.0, 0.0, 1.0); }   // leisure
    else if (kind > 4.5 && kind < 5.5)   { pool = 5.0; group = float4(0.0, 0.0, 0.0, 1.0); }   // public
    else if (kind > 5.5 && kind < 6.5)   { pool = 6.0; group = float4(0.0, 1.0, 0.0, 0.0); }   // industry
    else if (kind > 6.5 && kind < 7.5)
    {
        pool = ground ? 2.0 : 1.0;                                                          // shop, offices above
        group = ground ? float4(0.0, 0.0, 1.0, 0.0) : float4(0.0, 1.0, 0.0, 0.0);
    }
    return pool;
}

// A room's lamp: a share of coloured lights (LED strips, RGB bulbs), the rest the whites real rooms
// have -- homes mostly warm, offices neutral to cool, shops between. Luminance 1.
float3 LampColour(float u, float pool, float coloured)
{
    float3 c;
    if (u < coloured)
    {
        float k = floor(u / max(coloured, 1e-4) * 6.0);
        c = k < 0.5 ? float3(0.55, 0.10, 1.00)      // purple
          : k < 1.5 ? float3(1.00, 0.12, 0.45)      // pink
          : k < 2.5 ? float3(0.05, 0.85, 1.00)      // cyan
          : k < 3.5 ? float3(0.12, 0.25, 1.00)      // blue
          : k < 4.5 ? float3(0.15, 1.00, 0.25)      // green
          :           float3(1.00, 0.08, 0.04);     // red
        return c / Luma(c);
    }

    u = saturate((u - coloured) / max(1.0 - coloured, 1e-4));
    bool work = pool > 0.5 && pool < 1.5 || pool > 4.5;          // offices, public, industry
    bool shop = pool > 1.5 && pool < 2.5 || pool > 3.5 && pool < 4.5;
    if (work)
        c = u < 0.05 ? RG_K3000 : (u < 0.25 ? RG_K3500 : (u < 0.70 ? RG_K4000 : (u < 0.93 ? RG_K5000 : RG_K6500)));
    else if (shop)
        c = u < 0.15 ? RG_K2700 : (u < 0.40 ? RG_K3000 : (u < 0.75 ? RG_K4000 : RG_K5000));
    else
        c = u < 0.10 ? RG_K2200 : (u < 0.45 ? RG_K2700 : (u < 0.67 ? RG_K3000 : (u < 0.77 ? RG_K3500 :
            (u < 0.87 ? RG_K4000 : (u < 0.95 ? RG_K5000 : RG_K6500)))));
    return c / Luma(c);
}

// The average lamp of a kind of room: what a room too small to see shows.
float3 AverageLamp(float pool)
{
    bool work = pool > 0.5 && pool < 1.5 || pool > 4.5;
    float3 c = work ? RG_K4000 : (pool > 1.5 && pool < 2.5 || pool > 3.5 && pool < 4.5 ? RG_K3500 : RG_K2700);
    return c / Luma(c);
}

// Detail finer than about three pixels fades to its average: folds, slats, a scalloped hem.
float Fine(float period, float footprint)
{
    return saturate(period / max(footprint * 3.0, 1e-5) - 0.5);
}

// The room's curtain or blind, hanging just behind the glass. xy: 0..1 across the window (from its
// bottom left); size: the window in metres. Returns how much of it covers the point; albedo, its
// colour; trans, the share of the room's light it lets through at night.
float RoomCurtain(float seed, float pool, float2 xy, float2 size, float footprint, out float3 albedo, out float trans)
{
    albedo = float3(0.8, 0.8, 0.8);
    trans = 0.0;
    float share = _RGRooms2.z;
    bool work = pool > 0.5 && pool < 1.5 || pool > 4.5;
    bool shop = pool > 1.5 && pool < 2.5 || pool > 3.5 && pool < 4.5;
    if (shop)
        share *= 0.3;
    float u = RoomRand(seed, 31.0);
    if (u >= share)
        return 0.0;
    float t = u / max(share, 1e-4);
    float2 m = xy * size;
    float r1 = RoomRand(seed, 32.0), r2 = RoomRand(seed, 33.0), r3 = RoomRand(seed, 34.0);

    // Blinds: roller (a translucent sheet) or venetian (slats), drawn from the top down.
    bool roller = shop || (work ? t >= 0.6 : (t >= 0.82 && t < 0.91));
    bool venetian = !shop && (work ? t < 0.6 : t >= 0.91);
    if (roller || venetian)
    {
        float drop = roller ? lerp(0.2, 0.95, r1) : lerp(0.35, 1.0, r1);
        float covered = step(1.0 - drop, xy.y);
        float3 tone = lerp(float3(0.62, 0.60, 0.56), float3(0.90, 0.89, 0.86), r2);
        if (roller)
        {
            albedo = r3 < 0.2 ? tone * float3(0.55, 0.62, 0.70) : tone;
            trans = 0.35;
            return covered * 0.97;
        }
        // Venetian slats 5 cm apart, open by r3: a stripe close up, its share further off.
        float open = lerp(0.15, 0.55, r3);
        float slat = step(open, frac(m.y / 0.05));
        float k = Fine(0.05, footprint);
        albedo = tone;
        trans = 0.25;
        return covered * lerp(1.0 - open, slat, k) * 0.98;
    }

    // Fabric: tulle across the window, drapes at its sides, or both.
    bool tulle = t < 0.56 || (t >= 0.76 && t < 0.82 && r3 < 0.3);
    float sideL = 0.0, sideR = 0.0;
    if (t >= 0.30 && t < 0.68)       { sideL = lerp(0.12, 0.25, r1); sideR = lerp(0.12, 0.25, r2); }   // drawn back
    else if (t >= 0.68 && t < 0.76)  { sideL = lerp(0.30, 0.45, r1); sideR = lerp(0.30, 0.45, r2); }   // half drawn
    else if (t >= 0.76 && t < 0.82)  { sideL = 0.5 - 0.03 * r1; sideR = 0.5 - 0.03 * r2; }             // closed
    bool drape = xy.x < sideL || xy.x > 1.0 - sideR;

    if (drape)
    {
        // A drape's colour: one of the colours fabric comes in.
        float pick = floor(RoomRand(seed, 35.0) * 10.0);
        float3 c = pick < 0.5 ? float3(0.42, 0.12, 0.15)     // burgundy
                 : pick < 1.5 ? float3(0.36, 0.35, 0.18)     // olive
                 : pick < 2.5 ? float3(0.13, 0.19, 0.31)     // navy
                 : pick < 3.5 ? float3(0.78, 0.72, 0.60)     // beige
                 : pick < 4.5 ? float3(0.35, 0.24, 0.17)     // brown
                 : pick < 5.5 ? float3(0.69, 0.55, 0.24)     // gold
                 : pick < 6.5 ? float3(0.48, 0.48, 0.47)     // grey
                 : pick < 7.5 ? float3(0.18, 0.36, 0.36)     // teal
                 : pick < 8.5 ? float3(0.70, 0.48, 0.48)     // rose
                 :              float3(0.89, 0.85, 0.76);    // cream
        float folds = 1.0 + 0.35 * sin(6.2832 * m.x / 0.16 + r3 * 6.2832) * Fine(0.16, footprint);
        albedo = c * c * folds;   // the palette is sRGB-ish: squared, about linear
        trans = 0.14 * folds;
        return 0.96;
    }

    if (tulle)
    {
        // A sheer, ivory to white, softly folded; its hem a little above the sill, scalloped.
        float hem = 0.03 + 0.015 * sin(6.2832 * m.x / 0.25) * Fine(0.25, footprint);
        float below = step(xy.y, hem);
        float folds = 1.0 + 0.2 * sin(6.2832 * m.x / 0.09 + r1 * 6.2832) * Fine(0.09, footprint);
        albedo = lerp(float3(0.78, 0.75, 0.66), float3(0.86, 0.86, 0.84), r2) * folds;
        trans = 0.7 * folds;
        return (1.0 - below) * 0.55 * folds;
    }
    return 0.0;
}

// A room's light (its lamp, its television, or none) by night: the room's place in the grid, its
// building's kind of rooms, its building's seed. seed: the room's own number; lit, tv: 0..1. The
// same room gives the same answer whichever pixel asks: every tier, near or far, agrees.
float3 RoomLight(float2 cellId, float pool, float4 group, float buildingSeed, out float seed, out float lit, out float tv)
{
    seed = Hash12(cellId + buildingSeed * 4096.0);

    // Offices in half the buildings a whole floor at a time, else three rooms together.
    bool work = pool > 0.5 && pool < 1.5 || pool > 5.5;
    float2 lightCell = cellId;
    if (work)
        lightCell = float2(RoomRand(buildingSeed, 3.0) < 0.5 ? 0.0 : floor(cellId.x / 3.0), cellId.y);
    float lightSeed = Hash12(lightCell + buildingSeed * 4096.0 + 17.0);

    // On or off: the room's own number for this stretch of hours against the share lit now, each
    // room's stretches starting at its own moment (Interiors.cs). At the turn, a moment's change.
    float stretch = _RGRooms.w;
    float phase = RoomRand(lightSeed, 5.0);
    float e = stretch > 0.0 ? _RGRooms.z / stretch + phase : phase;
    float ei = floor(e);
    float share = dot(group, _RGRoomShare);
    float litNow = step(RoomRand(lightSeed, ei + 11.0), share);
    float litBefore = step(RoomRand(lightSeed, ei + 10.0), share);
    lit = lerp(litBefore, litNow, saturate((e - ei) * 200.0));
    // Some lit homes are only a television.
    bool homeLike = pool < 0.5 || pool > 2.5 && pool < 3.5;
    tv = homeLike ? step(RoomRand(lightSeed, ei + 23.0), 0.07) : 0.0;

    float3 lamp = LampColour(RoomRand(seed, 7.0), pool, _RGRooms2.y);
    float bright = (0.55 + 0.9 * RoomRand(seed, 6.0)) * _RGRooms2.x;
    float flicker = 0.75 + 0.25 * sin(_Time.y * 7.0 + seed * 40.0) * sin(_Time.y * 2.3 + seed * 17.0);
    float3 tvLight = float3(0.55, 0.70, 1.0) / Luma(float3(0.55, 0.70, 1.0)) * 0.18 * flicker * _RGRooms2.x;
    return lit * lerp(lamp * bright, tvLight, tv);
}

// The share of a storey that is floor slab and ceiling, dark at night, where the asset painted no
// window of its own (a curtain wall's spandrel): bottom, top.
#define RG_ROOM_SLAB float2(0.14, 0.06)

// The room's front, the face of its box that holds the window: the box's aspect (A, width over
// height), covering the window (a painted window RG_ROOM_OVERSCAN larger: the room is wider than its
// window; a storey's cell exactly, so its floor and ceiling are the storey's). Its height, metres.
float RoomFrontHeight(float aspect, float2 windowSize, bool grid)
{
    float over = grid ? 1.0 : RG_ROOM_OVERSCAN;
    return windowSize.x / max(windowSize.y, 0.01) < aspect ? windowSize.y * over : windowSize.x * over / aspect;
}

// Where this pixel of glass looks in its room's picture. The room is a box behind the window: x
// across (-A..A), y floor to ceiling (-1..1), z in (0..D), the window a part of its front at z = 0;
// the picture is that box seen from a camera in front of the window, far enough that the back wall
// fills _RGRoomAtlasInfo.w of it (tools/interiors/render-rooms.py, box_view) -- so the ray from
// the eye through this pixel, met with the box's walls, floor, ceiling or back, and projected
// through that camera, is the right place in it: the room moves as a room does when the view does
// (interior mapping, van Dongen 2008; one picture per room, SimCity 2013 as Ben Golus wrote it up).
// _RGRooms3.x blends from the flat picture on the glass (0) to the box (1). Mirrored for half the
// rooms; the window's place in the room's front a room's own (crop).
float2 RoomPictureUV(float aspect, float depth, float3 P, float3 V, float3 n, float3 t, float along,
                     float2 windowMin, float2 windowSize, bool grid, float seed)
{
    // The front, in metres, round the window: the window at a place of the room's own within it.
    float frontH = RoomFrontHeight(aspect, windowSize, grid);
    float2 front = float2(frontH * aspect, frontH);
    float2 slack = max(front - windowSize, 0.0);
    float2 crop = float2(RoomRand(seed, 1.0), lerp(0.3, 0.7, RoomRand(seed, 2.0)));
    float2 onFront = saturate((float2(along, P.y) - windowMin + slack * crop) / front);   // 0..1 across the front

    // Into the box: the front point and the view's direction in the room's frame (half-heights).
    float mirror = RoomRand(seed, 4.0) < 0.5 ? -1.0 : 1.0;
    float3 F = float3((onFront.x * 2.0 - 1.0) * aspect * mirror, onFront.y * 2.0 - 1.0, 0.0);
    float3 d = float3(dot(V, t) * mirror, V.y, max(dot(V, -n), 1e-3));
    float sx = d.x >= 0.0 ? 1.0 : -1.0, sy = d.y >= 0.0 ? 1.0 : -1.0;
    float tx = (sx * aspect - F.x) / (abs(d.x) > 1e-5 ? d.x : sx * 1e-5);
    float ty = (sy - F.y) / (abs(d.y) > 1e-5 ? d.y : sy * 1e-5);
    float tz = depth / d.z;
    float3 H = F + d * max(min(min(tx, ty), tz), 0.0);

    // Through the picture's camera.
    float backShare = clamp(_RGRoomAtlasInfo.w, 0.05, 0.95);
    float cam = backShare * depth / (1.0 - backShare);
    float2 box = H.xy * (cam / (cam + H.z));
    float2 boxUV = float2(box.x / aspect, box.y) * 0.5 + 0.5;
    float2 flatUV = float2(F.x / aspect, F.y) * 0.5 + 0.5;
    return lerp(flatUV, boxUV, saturate(_RGRooms3.x));
}

// What the room behind this pixel of glass sends through it, by night and by day, once through the
// glass (nightGlass, dayGlass: what the glass lets through of each). Returns how much of the
// window is the room's: 0 where there are no rooms (switched off, not loaded, an asset of its own
// says no, glass that is not a facade), else 1 (less on a slanted pane).
float RoomsBehind(float3 P, float3 n, float3 t, float3 V, float baseY, float footprint, float4 painted, float buildingId,
                  float assetA, float3 inside, float3 dayGlass, float3 nightGlass, out float3 night, out float3 day)
{
    night = float3(0.0, 0.0, 0.0);
    day = inside;
    float kind, assetSwitch;
    RoomKind(assetA, kind, assetSwitch);
    bool on = _RGRooms2.w > 0.5 && (assetSwitch > 1.5 || (assetSwitch < 0.5 && _RGRooms.x > 0.5));
    float facade = saturate((1.0 - abs(n.y)) * 3.0);
    if (!on || facade <= 0.0)
        return 0.0;

    // The room: the window the asset painted here (close up), else a cell one storey tall and a
    // room wide along the facade. Its place in the grid (column, storey) names it, the same on every
    // tier as far as can be: a painted window by its middle. With _RGRooms3.y, the grid up close too:
    // the LOD models have no painted windows to find, so a building changing model keeps its rooms.
    if (_RGRooms3.y > 0.5)
        painted = float4(0.0, 0.0, 0.0, 0.0);
    float storeyH = max(_RGPane2.z, 2.0);
    float along = dot(P, t);
    float cellW = kind > 0.5 && kind < 1.5 || kind > 6.5 ? 6.0 : (kind > 5.5 && kind < 6.5 ? 8.0 : 3.6);
    float2 cellMin, cellSize, cellId;
    if (painted.y > painted.x)
    {
        cellMin = float2(painted.x, painted.z);
        cellSize = float2(painted.y - painted.x, painted.w - painted.z);
        float2 middle = cellMin + cellSize * 0.5;
        cellId = float2(floor(middle.x / cellW), floor((middle.y - baseY) / storeyH));
    }
    else
    {
        float storey = floor((P.y - baseY) / storeyH);
        float column = floor(along / cellW);
        cellMin = float2(column * cellW, baseY + storey * storeyH);
        cellSize = float2(cellW, storeyH);
        cellId = float2(column, storey);
    }

    float4 group;
    float pool = RoomPool(kind, cellId.y, group);
    float fid = FacadeId(n);
    float buildingSeed = Hash12(float2(fmod(buildingId, 997.0) + 0.5, floor(buildingId / 997.0) + fid * 0.37));
    float seed, lit, tv;
    float3 roomLight = RoomLight(cellId, pool, group, buildingSeed, seed, lit, tv);
    float share = dot(group, _RGRoomShare);
    float3 dark = _RGAmbSky.rgb * 0.03;
    float3 dayDark = _RGAmbSky.rgb * _RGLight.y;

    // Where the asset painted no window, a storey of curtain wall: its floor slab and ceiling dark,
    // the room between them.
    bool grid = !(painted.y > painted.x);
    float slabShare = grid ? RG_ROOM_SLAB.x + RG_ROOM_SLAB.y : 0.0;
    float2 bandMin = cellMin, bandSize = cellSize;
    if (grid)
    {
        bandMin.y += RG_ROOM_SLAB.x * cellSize.y;
        bandSize.y *= 1.0 - slabShare;
    }

    // How many pixels a room spans. Up close: its picture and its curtain. Further out: only its
    // light, read for four rooms under the pixel (a box filter of the pattern, so it does not crawl
    // as the view moves -- TAA); under half a pixel, the light of rooms of its kind on average.
    float roomPx = min(cellSize.x, cellSize.y) / max(footprint, 1e-4);
    float detail = saturate((roomPx - 3.0) / 2.0);
    float pattern = saturate((roomPx - 0.4) / 0.6);
    float fabric = 1.0 - 0.3 * _RGRooms2.z;
    float3 farNight = dark;
    if (detail < 1.0)
    {
        float3 sum = float3(0.0, 0.0, 0.0);
        [unroll]
        for (int k = 0; k < 4; k++)
        {
            float2 offset = float2(k == 1 || k == 3 ? 0.35 : -0.35, k >= 2 ? 0.35 : -0.35) * footprint;
            float2 q = float2(along, P.y) + offset;
            float2 tapId = float2(floor(q.x / cellW), floor((q.y - baseY) / storeyH));
            float4 tapGroup;
            float tapPool = RoomPool(kind, tapId.y, tapGroup);
            float tapSeed, tapLit, tapTv;
            sum += RoomLight(tapId, tapPool, tapGroup, buildingSeed, tapSeed, tapLit, tapTv);
        }
        float3 average = share * AverageLamp(pool) * _RGRooms2.x;
        farNight = lerp(average, sum * 0.25, pattern) * (1.0 - slabShare) * fabric + dark;
    }

    float3 roomNight = farNight;
    float3 roomDay = dayDark;
    float3 albedo = float3(0.0, 0.0, 0.0);
    float cover = 0.0;
    float2 xy = saturate((float2(along, P.y) - bandMin) / bandSize);
    if (detail > 0.0)
    {
        float4 range = _RGRoomPools[(int)pool];
        float tile = range.x + min(floor(RoomRand(seed, 3.0) * range.y), max(range.y - 1.0, 0.0));
        float perRow = max(_RGRoomAtlasInfo.x, 1.0);
        float2 tileXY = float2(fmod(tile, perRow), floor(tile / perRow));
        float2 tileUV = RoomPictureUV(range.z, range.w, P, V, n, t, along, bandMin, bandSize, grid, seed);

        // The level of detail the pixel covers (a little sharper than its longest footprint), the
        // reads kept inside the tile at that level. The picture spans the room's front: a little
        // more than the window.
        float tileMetres = RoomFrontHeight(range.z, bandSize, grid);
        float nightPx = max(_RGRoomAtlasInfo.y, 1.0), dayPx = max(_RGRoomAtlasInfo.z, 1.0);
        float lodN = clamp(log2(max(footprint * 0.7 * nightPx / tileMetres, 1e-4)), 0.0, log2(nightPx));
        float lodD = clamp(log2(max(footprint * 0.7 * dayPx / tileMetres, 1e-4)), 0.0, log2(dayPx));
        float marginN = 0.5 * exp2(lodN) / nightPx, marginD = 0.5 * exp2(lodD) / dayPx;
        float2 uvN = (tileXY + clamp(tileUV, marginN, 1.0 - marginN)) / perRow;
        float2 uvD = (tileXY + clamp(tileUV, marginD, 1.0 - marginD)) / perRow;
        float3 picN = Finite(tex2Dlod(_RGRoomAtlas, float4(uvN, 0.0, lodN)).rgb) / RG_ROOM_TILE_MEAN;
        float3 picD = Finite(tex2Dlod(_RGRoomAtlasDay, float4(uvD, 0.0, lodD)).rgb) / RG_ROOM_TILE_MEAN;

        // By night lit by its lamp (or a television), else dark; by day by the sky's light, as dark
        // as today's inside on average.
        float3 nearNight = picN * (roomLight + dark);
        float3 nearDay = picD * dayDark;

        // The curtain, at the glass: no slide. Lit from behind at night, by the sky by day.
        float trans;
        cover = RoomCurtain(seed, pool, xy, bandSize, footprint, albedo, trans);
        nearNight = lerp(nearNight, roomLight * trans * albedo * 1.2 + dark * albedo, cover);
        nearDay = lerp(nearDay, _RGAmbSky.rgb * albedo * 0.55, cover);

        // The slab and the ceiling, dark.
        float inBand = grid ? step(bandMin.y, P.y) * step(P.y, bandMin.y + bandSize.y) : 1.0;
        nearNight = lerp(dark * 0.5, nearNight, inBand);
        nearDay = lerp(dayDark * 0.5, nearDay, inBand);

        roomNight = lerp(farNight, nearNight, detail);
        roomDay = lerp(dayDark, nearDay, detail);
    }

    if (_RGDebug.x > 10.5 && _RGDebug.x < 11.5)
    {
        // 11: the rooms -- lit (green), dark (red), a television (blue); cells outlined by their kind's
        // pool (brighter edge).
        float2 edge = min(xy, 1.0 - xy) * cellSize;
        float3 dbg = tv > 0.5 && lit > 0.5 ? float3(0.1, 0.3, 1.0) : (lit > 0.5 ? float3(0.1, 0.9, 0.2) : float3(0.6, 0.05, 0.05));
        night = min(edge.x, edge.y) < 0.08 ? float3(1.0, 1.0, 0.2) * (0.3 + pool * 0.1) : dbg;
        day = night;
        return facade;
    }

    night = roomNight * nightGlass;
    day = roomDay * dayGlass;
    return facade;
}

#endif
