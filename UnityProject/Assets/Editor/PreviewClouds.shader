// The preview's stand-in for Volumetric Weather's clouds, with the interface the mod relies on: a
// mesh renderer named "VolumetricCloudsVolume" round the camera, a Transparent+100 material that
// draws the sky's clouds in premultiplied alpha (Blend One OneMinusSrcAlpha, as VW's raymarch),
// hidden behind buildings by the depth texture while _UseDepth is 1 -- which Real Glass's sky
// camera turns off for its own draw. Not VW's clouds: a flat layer of fractal noise at 1500 m.
Shader "RealGlass/Preview/Clouds"
{
    Properties
    {
        _UseDepth ("Use the depth texture", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _UseDepth;
            float4 _PreviewCloudLight;   // rgb: the clouds' sunlit colour
            sampler2D_float _CameraDepthTexture;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    s += a * Noise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return s;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 dir = normalize(i.worldPos - origin);
                if (dir.y < 0.01)
                    return float4(0, 0, 0, 0);

                float t = (1500.0 - origin.y) / dir.y;
                if (t <= 0.0)
                    return float4(0, 0, 0, 0);

                if (_UseDepth > 0.5)
                {
                    float eye = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    if (eye < _ProjectionParams.z * 0.98 && eye / max(dot(dir, forward), 1e-4) < t)
                        return float4(0, 0, 0, 0);
                }

                float2 p = (origin.xz + dir.xz * t) / 2200.0;
                float cover = saturate((Fbm(p) - 0.47) * 3.5) * saturate(dir.y * 10.0);
                float shade = lerp(0.62, 1.0, saturate(Fbm(p * 1.7 + 5.3) * 1.3));
                return float4(_PreviewCloudLight.rgb * shade * cover, cover);
            }
            ENDCG
        }
    }
}
