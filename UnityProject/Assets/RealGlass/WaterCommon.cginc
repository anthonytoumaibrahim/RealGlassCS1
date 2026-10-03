// The Water tab's shared maths (RealGlass/Water/WaterOptics.cs has the model and its sources).
#ifndef RG_WATER_COMMON
#define RG_WATER_COMMON

// The rough sea's reflectance of the sky (Cox & Munk slopes, Schlick's Fresnel for water): entry i
// at x = i / 32, mu = x^2, mu the sine of how far below the level one looks. x: rho.
float4 _RGSeaTable[33];

float RGSeaRho(float mu)
{
    float x = sqrt(saturate(mu)) * 32.0;
    int i0 = min(31, (int)floor(x));
    return lerp(_RGSeaTable[i0].x, _RGSeaTable[i0 + 1].x, x - i0);
}

#endif
