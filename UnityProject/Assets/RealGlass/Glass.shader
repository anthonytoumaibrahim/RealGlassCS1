// Real Glass: the glass of a building, drawn a second time over the game's own draw of it.
//
// The mod draws every building again, right after the game does, with the same mesh, matrix and
// data (Harmony postfixes on the game's three building paths): RG_NEAR = BuildingAI.RenderMesh
// (one building), RG_LOD = Building.RenderLod (up to 8 instances of one asset's LOD mesh, placed by
// _BuildingLocations[int(color.a * 8)]), RG_GROUP = RenderGroup/MegaRenderGroup.RenderMesh (a
// 384 m block of the city baked into one mesh, world-space normals, atlas uvs). The vertex rules
// below are the game's own (read from the disassembly of Custom/Buildings/Building/Default), so
// this draw lands on the game's depth exactly; Offset -1 -1 settles the last bit.
//
// Queue 2499, the last of the opaque range: Unity draws it forward after the deferred final pass
// and BEFORE the game's fog ([ImageEffectOpaque]), so the glass is fogged like the rest of the
// city. What is not glass is discarded; glass is REPLACED (alpha = mask x strength).
Shader "RealGlass/Glass"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _XYSMap ("Normal XY, specular", 2D) = "gray" {}
        _ACIMap ("Alpha, colour, illumination", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+499" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile RG_NEAR RG_LOD RG_GROUP
            #include "GlassCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _XYSMap;
            sampler2D _ACIMap;

#if defined(RG_LOD)
            float4x4 _BuildingLocations[8];
            float4 _BuildingStates[8];
#elif defined(RG_NEAR)
            float4 _BuildingState;
#endif

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
                float4 texcoord2 : TEXCOORD2;
                float4 texcoord3 : TEXCOORD3;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 worldPos : TEXCOORD0;     // xyz position, w the building's base height
                float4 worldNormal : TEXCOORD1;  // xyz geometric normal, w what the building's state leaves of its glass
                float2 uv : TEXCOORD2;
                float4 rect : TEXCOORD3;         // the asset's rectangle in the LOD atlas (LOD, GROUP)
                float4 screenPos : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 local = v.vertex;
                float4 state;
                float4x4 place;
                float baseWeight;
#if defined(RG_LOD)
                int index = (int)(v.color.a * 8.0);      // Color32.a = k * 32 (BuildingInfoBase.InitMeshData)
                place = _BuildingLocations[index];
                state = _BuildingStates[index];
                baseWeight = 1.0;
#elif defined(RG_GROUP)
                place = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                state = float4(0.0, 100000.0, 0.0, 1.0);   // baked: nothing squashed, nothing extended
                baseWeight = 0.0;
#else
                place = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                state = _BuildingState;
                baseWeight = v.color.a;
#endif
                // The game's rule: a building under construction is squashed above its progress
                // height (state.y); its base (the ring at y = -1) is stretched down to the ground.
                float y = min(local.y, local.y * 0.01 + abs(state.y));
                float isBase = abs(y + 1.0) <= 0.04 ? 1.0 : 0.0;
                y *= 1.0 + isBase * baseWeight * (state.w - 1.0);
                local.y = y;

                float4 placed = mul(place, local);
                float4 world = mul(unity_ObjectToWorld, placed);
                o.pos = mul(UNITY_MATRIX_VP, world);

                float3 n = mul((float3x3)place, v.normal);
#if defined(RG_GROUP)
                float3 wn = n;                              // baked in world space already
                float baseY = _RGFill.y;                    // no building origin in a baked block
#else
                float3 wn = UnityObjectToWorldNormal(n);
                float baseY = mul(unity_ObjectToWorld, float4(place._m03, place._m13, place._m23, 1.0)).y;
#endif
                // Burned (state.x: fire damage over half, 0..1) glass is not glass any more; a
                // negative state.y is the game's collapse/flood clip.
                float stateFade = step(0.0, state.y) * saturate(1.0 - 1.5 * state.x);

                o.worldPos = float4(world.xyz, baseY);
                o.worldNormal = float4(wn, stateFade);
#if defined(RG_NEAR)
                o.uv = v.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
                o.rect = float4(0.0, 0.0, 1.0, 1.0);
#else
                o.uv = v.texcoord.xy;
                // abs, as the game's group shader takes it: a baked block flags things in the
                // rectangle's sign (its vertex program: o5 = |uv3.xy, uv4.xy|, and uv4.x <= 0 a flag).
                o.rect = abs(float4(v.texcoord2.xy, v.texcoord3.xy));
#endif
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
#if !defined(RG_NEAR)
                // Stay inside this asset's rectangle of the atlas, as the game does, so a coarse
                // mip never reads the neighbouring asset.
                float2 fp = max(abs(ddx(uv)), abs(ddy(uv)));
                fp = min(fp, i.rect.zw * 0.25);
                uv = clamp(uv, i.rect.xy + fp, i.rect.xy + i.rect.zw - fp);
#endif
                float4 xys = tex2D(_XYSMap, uv);
                float4 aci = tex2D(_ACIMap, uv);
                float mask = GlassMask(xys, aci) * i.worldNormal.w;
                clip(mask - 0.004);

                if (_RGMask.z > 0.5)
                    return float4(1.0, 0.0, 1.0, max(mask, 0.35));

                float3 P = i.worldPos.xyz;
                float baseY = i.worldPos.w;
                float3 n = normalize(i.worldNormal.xyz);
                float3 toP = P - _WorldSpaceCameraPos;
                float dist = length(toP);
                float3 V = toP / max(dist, 1e-3);
                float footprint = max(length(fwidth(P)), 1e-4);

                float3 t = FacadeTangent(n);
                float2 paneCell;
                float3 np = PaneNormal(n, t, P, baseY, footprint, paneCell);
                float facing = dot(np, -V);
                if (facing < 0.01)
                    np = normalize(np + V * (facing - 0.01));   // a bent pane never faces away
                float cosT = saturate(dot(np, -V));
                float F = GlassFresnel(cosT);
                float3 R = reflect(V, np);
                float rough = _RGRefl.y;

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 gamePixel = tex2Dlod(_RGScene, float4(suv, 0.0, 0.0)).rgb;

                // What the glass reflects: the screen where the ray finds something, else the
                // ground at the building's feet (below the horizon) or the sky.
                float hz = smoothstep(-0.03, 0.03, R.y);
                float3 sky = SkyRadiance(R, rough);
                float3 fallback = sky;
                if (hz < 1.0)
                    fallback = lerp(GroundFill(P, R, rough, baseY), sky, hz);

                if (_RGDebug.x > 1.5)
                {
                    if (_RGDebug.x < 2.5)
                    {
                        // The depth texture here, against this pixel's own depth: grey bands every
                        // 50 m of what the texture says; red where it disagrees with us by > 2 m.
                        float sz = -SceneZ(suv * _ScreenParams.xy);
                        float own = -mul(UNITY_MATRIX_V, float4(P, 1.0)).z;
                        return float4(frac(sz / 50.0).xxx * (abs(sz - own) > 2.0 ? float3(1.0, 0.2, 0.2) : float3(1.0, 1.0, 1.0)), 1.0);
                    }
                    return float4(gamePixel, 1.0);
                }

                float3 refl = fallback;
                float traceState = 0.0;   // 1 hit, 2 left the screen, 3 ran out of steps
                if (_RGTrace.x > 0.5)
                {
                    float3 origin = P + n * max(0.3, dist * 0.003);
                    float3 csOrig = mul(UNITY_MATRIX_V, float4(origin, 1.0)).xyz;
                    float3 csDir = normalize(mul((float3x3)UNITY_MATRIX_V, R));
                    float2 hitUV;
                    float hitDistance;
                    bool clipped;
                    if (TraceScreenRay(csOrig, csDir, Ign(i.pos.xy), hitUV, hitDistance, clipped))
                    {
                        float2 e = min(hitUV, 1.0 - hitUV);
                        float edge = saturate(min(e.x, e.y) / max(_RGRefl.w, 1e-3));
                        float lod = rough * 5.0 * saturate(hitDistance / 150.0);
                        float3 onScreen = tex2Dlod(_RGScene, float4(hitUV, 0.0, lod)).rgb;
                        refl = lerp(fallback, onScreen, edge);
                        traceState = 1.0;
                    }
                    else
                    {
                        traceState = clipped ? 2.0 : 3.0;
                    }
                }
                if (_RGDebug.x > 0.5)
                    return float4(traceState == 1.0 ? float3(0.0, 1.0, 0.0) : (traceState == 2.0 ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.2, 1.0)), 1.0);

                // The glass's colour: the chosen tint, and as much of the building's own painted
                // glass colour as the "own colour" setting keeps. The data sheets' numbers are
                // LUMINOUS (visible light transmittance / reflectance, eye-weighted), so both
                // filters are scaled to a luminance of 1: "clarity 24%" lets 24% of the light
                // through, whatever the colour; "reflectivity 20%" reflects 20%.
                float3 own = NormaliseTint(tex2D(_MainTex, uv).rgb);
                float3 tint = NormaliseTint(lerp(_RGTint.rgb, own, _RGTint.a));
                float3 filter = lerp(float3(1.0, 1.0, 1.0), tint, 0.6);
                filter /= max(Luma(filter), 0.05);
                float3 coat = lerp(float3(1.0, 1.0, 1.0), tint, _RGMask.w);
                coat /= max(Luma(coat), 0.05);

                // Through the glass.
                float3 dayRoom = float3(0.0, 0.0, 0.0);
                float3 nightRoom = gamePixel;
                float roomsOn = _RGRoom.w * saturate(1.0 - abs(n.y) * 2.0);
                if (roomsOn > 0.0)
                {
                    float3 d, nt;
                    RoomRadiance(P, V, n, t, baseY, footprint, paneCell, gamePixel, d, nt);
                    dayRoom = d * roomsOn;
                    nightRoom = lerp(gamePixel * _RGRoom2.w, nt, roomsOn);
                }
                else
                {
                    dayRoom = _RGAmbSky.rgb * 0.12 * _RGRoom2.x;
                    nightRoom = gamePixel * _RGRoom2.w;
                }
                float daylight = _RGLight.x;
                // By night the game's lit windows are already painted as seen from outside: they
                // keep their brightness and take only a hint of the glass's colour.
                float3 nightFilter = lerp(float3(1.0, 1.0, 1.0), tint, 0.3);
                nightFilter /= max(Luma(nightFilter), 0.05);
                float3 through = dayRoom * (_RGGlass.z * filter) * daylight
                               + nightRoom * nightFilter * (1.0 - daylight);

                float3 L = F * refl * coat * _RGRefl.x
                         + (1.0 - F) * through
                         + SunGlint(np, V, F, rough) * coat;

                return float4(L, mask * _RGGlass.w);
            }
            ENDCG
        }
    }
}
