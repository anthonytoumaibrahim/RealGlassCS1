// Real Glass: the vertex program of a full-screen pass (a command buffer's Blit or a clip-space
// quad), as UnityCG's vert_img -- except that the uv crosses to the fragment as a FLOAT.
// UnityCG.cginc's v2f_img carries it as half2: Direct3D 11 and desktop OpenGL promote half to
// float, but Apple's GPUs honour it (11 bits of mantissa: 1/2048 at u = 1), and a copy or a depth
// pyramid read a texel off at a 4K screen's right edge, and a ripple's phase off by a few
// hundredths of a cycle, were the result on a Mac (audit, 2026-10-11).
#ifndef RG_FULLSCREEN_INCLUDED
#define RG_FULLSCREEN_INCLUDED

#include "UnityCG.cginc"

struct v2f_full
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

v2f_full vert_full(appdata_img v)
{
    v2f_full o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    return o;
}

#endif
