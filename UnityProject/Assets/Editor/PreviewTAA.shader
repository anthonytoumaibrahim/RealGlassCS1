// The water preview's stand-in for the temporal anti-aliasing Render It! runs (Unity's Post-processing
// Stack v1, TemporalAntialiasing: Halton(2, 3) jitter of the projection, the current frame read
// un-jittered and sharpened, the history clipped to the box of two half-texel corner reads widened by
// a luminance nudge, then blended), for a camera that does not move: no motion vectors, the
// stationary blending throughout. Its job is to measure flicker, not to ship.
Shader "Hidden/RealGlassPreviewTAA"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _HistoryTex;
            // xy: this frame's jitter (uv).
            float4 _Jitter;
            // x: sharpen; y: stationary blending; z: 1 on the first frame (no history yet).
            float4 _TAA;

            float Max3(float3 c)
            {
                return max(c.x, max(c.y, c.z));
            }

            float4 FastToneMap(float4 c)
            {
                return float4(c.rgb / (Max3(c.rgb) + 1.0), c.a);
            }

            float4 FastToneMapInverse(float4 c)
            {
                return float4(c.rgb / max(1.0 - Max3(c.rgb), 1e-4), c.a);
            }

            float4 ClipToAABB(float4 color, float3 minimum, float3 maximum)
            {
                float3 center = 0.5 * (maximum + minimum);
                float3 extents = 0.5 * (maximum - minimum);
                float3 offset = color.rgb - center;
                float3 ts = abs(extents / (offset + 0.0001));
                float t = saturate(min(ts.x, min(ts.y, ts.z)));
                color.rgb = center + offset * t;
                return color;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 k = _MainTex_TexelSize.xy;
                float2 uv = i.uv - _Jitter.xy;
                float4 color = tex2D(_MainTex, uv);
                float4 topLeft = tex2D(_MainTex, uv - k * 0.5);
                float4 bottomRight = tex2D(_MainTex, uv + k * 0.5);
                float4 corners = 4.0 * (topLeft + bottomRight) - 2.0 * color;
                color += (color - corners * 0.166667) * 2.718282 * _TAA.x;
                color = max(0.0, color);
                if (_TAA.z > 0.5)
                    return color;

                float4 average = FastToneMap((corners + color) * 0.142857);
                topLeft = FastToneMap(topLeft);
                bottomRight = FastToneMap(bottomRight);
                color = FastToneMap(color);
                float4 history = FastToneMap(tex2D(_HistoryTex, i.uv));
                float nudge = 4.0 * abs(Luminance(average.rgb) - Luminance(color.rgb));
                float4 minimum = min(bottomRight, topLeft) - nudge;
                float4 maximum = max(topLeft, bottomRight) + nudge;
                history = ClipToAABB(history, minimum.xyz, maximum.xyz);
                return FastToneMapInverse(lerp(color, history, _TAA.y));
            }
            ENDCG
        }
    }
}
