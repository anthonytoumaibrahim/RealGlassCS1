// Real Glass: light that is a number, on every graphics API.
//
// A NaN or an infinity in a picture the glass reuses (the copies, the city colour, last frame's
// finished picture) spreads to a whole block of every coarser mip and from frame to frame. The
// first guard was min(max(c, 0), 1000): exact on Direct3D 11, whose min and max return the other
// operand for a NaN (IEEE 754-2008 minNum / maxNum) -- but Metal compiles with fast math by
// default, which may assume there are no NaNs or infinities at all, and OpenGL's min/max leave a
// NaN undefined. So the test is on the BITS, as integers (IEEE 754 binary32: an exponent of all
// ones is an infinity with a zero mantissa, a NaN with any other), which no float optimisation
// may assume away; only then the float clamp, on values that are numbers by then.

#ifndef REALGLASS_FINITE_INCLUDED
#define REALGLASS_FINITE_INCLUDED

// Light: NaN and negatives (and -inf) to 0, +inf to a ceiling no light in the game comes near.
float3 Finite(float3 c)
{
    uint3 u = asuint(c);
    uint3 mag = u & 0x7FFFFFFFu;
    c = mag == 0x7F800000u ? float3(1000.0, 1000.0, 1000.0) : c;   // an infinity: the ceiling (sign below)
    c = (u >> 31) != 0u ? float3(0.0, 0.0, 0.0) : c;              // negative: none
    c = mag > 0x7F800000u ? float3(0.0, 0.0, 0.0) : c;            // NaN: none
    return min(c, 1000.0);
}

// A signed amount (light taken away as well as added): NaN to 0, infinities to +-1000.
float3 FiniteSigned(float3 c)
{
    uint3 u = asuint(c);
    uint3 mag = u & 0x7FFFFFFFu;
    c = mag > 0x7F800000u ? float3(0.0, 0.0, 0.0) : c;            // NaN: none
    c = mag == 0x7F800000u ? float3(1000.0, 1000.0, 1000.0) * sign(c) : c;
    return clamp(c, -1000.0, 1000.0);
}

// Any other amount held between lo and hi (a depth, in metres): NaN and -inf to lo, +inf to hi.
float FiniteIn(float x, float lo, float hi)
{
    uint u = asuint(x);
    uint mag = u & 0x7FFFFFFFu;
    x = mag == 0x7F800000u ? ((u >> 31) != 0u ? lo : hi) : x;
    x = mag > 0x7F800000u ? lo : x;
    return clamp(x, lo, hi);
}

// A coverage: NaN to 0 (nothing of ours: the game's own pixel stays), then 0..1.
float FiniteAlpha(float a)
{
    uint mag = asuint(a) & 0x7FFFFFFFu;
    a = mag > 0x7F800000u ? 0.0 : a;
    return saturate(a);
}

// normalize, with a stand-in for a vector too short to have a direction (normalize(0) is 0 / 0).
float3 SafeNormalize(float3 v, float3 fallback)
{
    float l2 = dot(v, v);
    return l2 > 1e-12 ? v * rsqrt(l2) : fallback;
}

// a / b with b kept off zero (its sign kept): an edge case's 0 / 0 or x / 0 never happens.
float SafeDiv(float a, float b)
{
    return a / (abs(b) > 1e-6 ? b : (b < 0.0 ? -1e-6 : 1e-6));
}

#endif
