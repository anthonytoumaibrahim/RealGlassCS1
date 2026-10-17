// The game's own water surface, read as its shader reads it (Custom/Water/Default, disassembled
// 2026-10-06: the forward vertex program and the opaque water's forward pixel program; the
// constants' names and offsets read from the blob). The foam layer is drawn on the game's own water
// meshes with the game's own per-patch property block (TerrainPatch.SetWaterMaterialProperties), so
// it finds every input here under the game's names, and puts its vertices exactly where the game
// puts the water's.
//
//   _MainTex        the water's surface heights, 16 bits in .rg (the older simulation frame) and
//                   .ba (the newer); _TerrainStatus.y blends them
//   _TerrainHeight  the terrain's heights, 16 bits in .rg (and a second set in .ba: the pixel
//                   program takes the lower of the two)
//   _HeightMapping  the water heights' texel (.x, .z: in the surface's uv) and height scale (.y, m)
//   _TerrainMapping the same for the terrain's heights
//   _SurfaceMapping world xz -> the patch's uv: xz * .z + .xy
//   _SurfaceAOld/New (WaterSimulation.FillSurfaceMap, IL 2026-10-06), 32 m a texel:
//                   (128 + 127 nx, pollution, 128 + 127 nz) -- the water surface's normal
//   _SurfaceBOld/New (128 + flow x, depth, 128 + flow z) -- the flow (the game's units: its
//                   shader treats 2 x - 1 as -1..1) and the depth, 255 = 16 m
//
// Both programs read a 16-bit height as r 255/256 + g/257 of its two 8-bit channels, and interpolate
// the four texels round a point themselves (point reads at the texel centres): reproduced literally,
// so the heights here are the game's to the last bit of float maths.

#ifndef RG_WATER_GAME
#define RG_WATER_GAME

sampler2D _MainTex;
// The ripples the water is shaded with: the game's global, or ours on its material (WaterRipples).
sampler2D _WaterNormal;
sampler2D _TerrainHeight;
sampler2D _SurfaceAOld;
sampler2D _SurfaceANew;
sampler2D _SurfaceBOld;
sampler2D _SurfaceBNew;
float4 _HeightMapping;
float4 _TerrainMapping;
float4 _SurfaceMapping;
float4 _TerrainStatus;
float4 _SimulationTime;
float4 _MainTex_ST;

// A 16-bit height from two 8-bit channels (0..1 of the mapping's scale).
float RGGameDecode(float2 rg)
{
    return dot(rg, float2(0.996094, 0.003891));
}

// The four texels round uv (texel size t in uv), read at their centres, and the weights between
// them -- the game's own steps: f = uv / t with the sign of fmod, the corner at uv - f t.
void RGGameCorners(float2 uv, float2 t, out float2 c00, out float2 w)
{
    float2 p = uv / t;
    float2 f = p >= -p ? frac(abs(p)) : -frac(abs(p));
    c00 = uv - f * t + 0.5 * t;
    w = saturate((f * t) / t);
}

// The water's surface height at uv in both frames (m): x the older, y the newer.
float2 RGGameWaterHeights(float2 uv)
{
    float2 t = _HeightMapping.xz;
    float2 c00, w;
    RGGameCorners(uv, t, c00, w);
    float4 s00 = tex2Dlod(_MainTex, float4(c00, 0.0, 0.0));
    float4 s10 = tex2Dlod(_MainTex, float4(c00 + float2(t.x, 0.0), 0.0, 0.0));
    float4 s01 = tex2Dlod(_MainTex, float4(c00 + float2(0.0, t.y), 0.0, 0.0));
    float4 s11 = tex2Dlod(_MainTex, float4(c00 + t, 0.0, 0.0));
    float2 h00 = float2(RGGameDecode(s00.xy), RGGameDecode(s00.zw));
    float2 h10 = float2(RGGameDecode(s10.xy), RGGameDecode(s10.zw));
    float2 h01 = float2(RGGameDecode(s01.xy), RGGameDecode(s01.zw));
    float2 h11 = float2(RGGameDecode(s11.xy), RGGameDecode(s11.zw));
    float2 bottom = h00 + w.x * (h10 - h00);
    float2 top = h01 + w.x * (h11 - h01);
    return (bottom + w.y * (top - bottom)) * _HeightMapping.y;
}

// The terrain's height at uv (m): the vertex program's (.rg only).
float RGGameTerrainVertex(float2 uv)
{
    float2 t = _TerrainMapping.xz;
    float2 c00, w;
    RGGameCorners(uv, t, c00, w);
    float h00 = RGGameDecode(tex2Dlod(_TerrainHeight, float4(c00, 0.0, 0.0)).xy);
    float h10 = RGGameDecode(tex2Dlod(_TerrainHeight, float4(c00 + float2(t.x, 0.0), 0.0, 0.0)).xy);
    float h01 = RGGameDecode(tex2Dlod(_TerrainHeight, float4(c00 + float2(0.0, t.y), 0.0, 0.0)).xy);
    float h11 = RGGameDecode(tex2Dlod(_TerrainHeight, float4(c00 + t, 0.0, 0.0)).xy);
    float bottom = h00 + w.x * (h10 - h00);
    float top = h01 + w.x * (h11 - h01);
    return (bottom + w.y * (top - bottom)) * _TerrainMapping.y;
}

// The terrain's height at uv (m) as the pixel program takes it: both sets, the lower.
float RGGameTerrainPixel(float2 uv)
{
    float2 t = _TerrainMapping.xz;
    float2 c00, w;
    RGGameCorners(uv, t, c00, w);
    float4 s00 = tex2Dlod(_TerrainHeight, float4(c00, 0.0, 0.0));
    float4 s10 = tex2Dlod(_TerrainHeight, float4(c00 + float2(t.x, 0.0), 0.0, 0.0));
    float4 s01 = tex2Dlod(_TerrainHeight, float4(c00 + float2(0.0, t.y), 0.0, 0.0));
    float4 s11 = tex2Dlod(_TerrainHeight, float4(c00 + t, 0.0, 0.0));
    float2 h00 = float2(RGGameDecode(s00.xy), RGGameDecode(s00.zw));
    float2 h10 = float2(RGGameDecode(s10.xy), RGGameDecode(s10.zw));
    float2 h01 = float2(RGGameDecode(s01.xy), RGGameDecode(s01.zw));
    float2 h11 = float2(RGGameDecode(s11.xy), RGGameDecode(s11.zw));
    float2 bottom = h00 + w.x * (h10 - h00);
    float2 top = h01 + w.x * (h11 - h01);
    float2 h = (bottom + w.y * (top - bottom)) * _TerrainMapping.y;
    return min(h.x, h.y);
}

// The patch's uv of a world point.
float2 RGGameSurfaceUV(float3 world)
{
    return world.xz * _SurfaceMapping.z + _SurfaceMapping.xy;
}

// The surface maps at uv, blended between the two frames as the game blends them.
void RGGameSurface(float2 uv, out float4 a, out float4 b)
{
    float4 a0 = tex2D(_SurfaceAOld, uv);
    float4 b0 = tex2D(_SurfaceBOld, uv);
    a = a0 + _TerrainStatus.y * (tex2D(_SurfaceANew, uv) - a0);
    b = b0 + _TerrainStatus.y * (tex2D(_SurfaceBNew, uv) - b0);
}

// Where the game reads its ripple texture at world xz (the opaque water's forward pixel program,
// 2026-10-06): twice -- at xz x 0.0061 (uvA), and at (z, x) x 0.0057 with its channels swapped (uvB:
// a mirrored, inverted copy) -- each carried along the flow in two phases half a 2.5 s cycle apart
// (Vlachos 2010), crossfaded by blend (the weight on B). speed: the flow's, which scales the slopes.
struct RGGameRippleReads
{
    float2 uvA;
    float2 uvB;
    float blend;
    float speed;
};

RGGameRippleReads RGGameRipplePlaces(float2 xz, float4 sb)
{
    RGGameRippleReads r;
    float2 flow = sb.xz * 2.0 - 1.0;
    r.speed = max(abs(flow.x), abs(flow.y));
    float phase = _SimulationTime.x * 0.4 + 0.2 * cos(0.06 * xz.x) * cos(0.06 * xz.y);
    float f = frac(phase);
    float g = frac(phase + 0.5) - 0.5;
    r.uvA = xz * 0.0061 - 0.14 * (f - 0.5) * flow;
    r.uvB = xz.yx * 0.0057 - 0.14 * g * flow.yx;
    r.blend = 2.0 * abs(0.5 - f);
    return r;
}

// The water's normal as the game shades it from those reads: decoded x 2 - 1 (x from .w, y from .y;
// B's swapped), x 0.5, plus the simulated surface's own slope (_SurfaceA), the sum x (the flow's speed
// + 0.5) -- so a ripple texture's slope s (WaterRipples' units: the texture holds -2 s) is shaded as
// s x (speed + 0.5). (Until 2026-10-17 the surface's slope was added after that gain, not under it:
// the game's directional and point-light programs both scale it.) Left out: the foam's damping
// (0.5 - 0.2 foam in place of 0.5) and the rain rings.
float3 RGGameWaterNormalAt(RGGameRippleReads r, float4 sa)
{
    float4 a = tex2D(_WaterNormal, r.uvA);
    float4 b = tex2D(_WaterNormal, r.uvB);
    float2 ripple = lerp(float2(a.w, a.y), float2(b.y, b.w), r.blend) * 2.0 - 1.0;
    float2 tilt = sa.xz * 2.0 - 1.0;
    float2 n2 = (ripple * 0.5 + float2(tilt.x, -tilt.y)) * (r.speed + 0.5);
    n2 /= max(length(n2), 1.0);
    float3 nt = float3(n2, sqrt(max(1.0 - dot(n2, n2), 0.0)));
    return normalize(float3(nt.x, nt.z, -nt.y));
}

// The same in one call. uvA: the first read's uv (its derivatives say how far the ripples are
// averaged away at this pixel).
float3 RGGameWaterNormal(float2 xz, float4 sa, float4 sb, out float2 uvA)
{
    RGGameRippleReads r = RGGameRipplePlaces(xz, sb);
    uvA = r.uvA;
    return RGGameWaterNormalAt(r, sa);
}

// What the game's vertex program makes of a water mesh vertex: its world position (the simulated
// surface plus the game's four swells: 0.19-0.25 m, 57-90 m long, faded in over 0.8-16 m of depth),
// and the water's height (m) in both frames and the terrain's under it.
struct RGGameWaterVertex
{
    float3 world;
    float2 heights;     // the water surface, the older frame and the newer (m)
    float waterHeight;  // blended (m): what the game's pixel program compares the terrain with
    float terrain;      // the terrain under it (m)
};

RGGameWaterVertex RGGameWaterAt(float4 vertex)
{
    RGGameWaterVertex o;
    float3 placed = mul(unity_ObjectToWorld, vertex).xyz;
    float2 uv = placed.xz * _SurfaceMapping.z + _SurfaceMapping.xy;
    o.heights = RGGameWaterHeights(uv);
    o.waterHeight = o.heights.x + _TerrainStatus.y * (o.heights.y - o.heights.x);
    o.terrain = RGGameTerrainVertex(uv);

    float depth = o.waterHeight - o.terrain;
    float s = saturate((depth - 0.8) * 0.065789);
    s = s * s * (3.0 - 2.0 * s);
    float4 phase = float4(placed.x, placed.z, placed.x, placed.z) * float4(0.07, 0.07, -0.11, -0.11) +
                   _SimulationTime.w * float4(15.0, 27.0, 7.0, 12.0);
    float swell = dot(sin(phase), s * float4(0.19, 0.21, 0.25, 0.23));

    // The mesh's own height is replaced: the local y that puts the vertex at the surface.
    float local = (swell + o.waterHeight - unity_ObjectToWorld._m13) / unity_ObjectToWorld._m11;
    o.world = mul(unity_ObjectToWorld, float4(vertex.x, local, vertex.z, vertex.w)).xyz;
    return o;
}

#endif
