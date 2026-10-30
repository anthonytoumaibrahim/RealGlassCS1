// Stand-ins, in the foam preview, for the passes after the water that tell the sky from the rest by the
// camera's depth texture (2026-10-28, the author's tsunami screenshots: a wave in front of the sky was
// filled with fog and covered with clouds, as the game's water is not in that texture).
//   0 THE GAME'S FOG (Hidden/DayNight/Fog, [ImageEffectOpaque], read from its disassembly in Volumetric
//     Weather's GameHorizon.cs): on a pixel of sky below the horizon it fills with its colour (the void
//     past the map's edge); above it, with Volumetric Weather's band at a millimetre, nothing; elsewhere
//     a distance fog (here 1 - exp(-d / 6 km): a stand-in's figure, not the game's).
//   1 VOLUMETRIC WEATHER'S SKY LAYER AND CLOUDS (Transparent, ZTest Always): over pixels of sky only
//     (its depth test: eye depth at 98% of the far plane or more), a flat overcast of 80%.
Shader "Hidden/RealGlassPreviewFogSky"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _PreviewFogColour;
    float4 _PreviewCloudColour;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 dir : TEXCOORD0;
        float4 screen : TEXCOORD1;
    };

    v2f vert(float4 vertex : POSITION)
    {
        v2f o;
        o.pos = mul(UNITY_MATRIX_VP, float4(_WorldSpaceCameraPos + vertex.xyz * 500.0, 1.0));
        #if defined(UNITY_REVERSED_Z)
        o.pos.z = 1.0e-6 * o.pos.w;
        #else
        o.pos.z = 0.999999 * o.pos.w;
        #endif
        o.dir = vertex.xyz;
        o.screen = ComputeScreenPos(o.pos);
        return o;
    }

    float Eye(v2f i)
    {
        return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(i.screen.xy / i.screen.w, 0.0, 0.0)));
    }

    float4 fragFog(v2f i) : SV_Target
    {
        float eye = Eye(i);
        bool sky = eye >= 0.98 * _ProjectionParams.z;
        float amount = sky ? (normalize(i.dir).y < 0.0 ? 1.0 : 0.0) : 1.0 - exp(-eye / 6000.0);
        return float4(_PreviewFogColour.rgb, amount);
    }

    float4 fragClouds(v2f i) : SV_Target
    {
        bool sky = Eye(i) >= 0.98 * _ProjectionParams.z;
        return float4(_PreviewCloudColour.rgb, sky && normalize(i.dir).y >= 0.0 ? 0.8 : 0.0);
    }
    ENDCG

    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFog
            #pragma target 3.5
            ENDCG
        }
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragClouds
            #pragma target 3.5
            ENDCG
        }
    }
}
