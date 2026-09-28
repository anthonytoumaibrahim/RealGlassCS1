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
// Which building a pixel belongs to comes from the same place on every tier: its colour index, the
// building's texel in the game's object colour map (near: _ObjectIndex, middle: _ObjectIndices[],
// far: the baked second uv). It seeds the building's rooms and finds its lamps (_RGBuildings), so
// neither changes as a building moves from one tier to the next.
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
            float4 _ObjectIndices[8];
#elif defined(RG_NEAR)
            float4 _BuildingState;
            float4 _ObjectIndex;
#endif

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
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
                float2 building : TEXCOORD5;     // the building's colour index: its texel in the object colour map
            };

            // Texture coordinates per metre UP the facade, from the pixel's own derivatives: the
            // screen-space changes of uv and of the facade position (along, up) give the 2 x 2 map
            // between them.
            float2 UvPerMetreUp(float2 uv, float3 P, float3 t)
            {
                float2 duvx = ddx(uv), duvy = ddy(uv);
                float3 dPx = ddx(P), dPy = ddy(P);
                float2 sx = float2(dot(dPx, t), dPx.y);
                float2 sy = float2(dot(dPy, t), dPy.y);
                float det = sx.x * sy.y - sy.x * sx.y;
                return abs(det) > 1e-10 ? (duvx * (-sy.x) + duvy * sx.x) / det : float2(0.0, 0.0);
            }

            // A tower of plain glass: the asset's texture one storey above and one below is glass
            // too, lit (or not) exactly alike -- it painted no floors or windows of its own here, so
            // the rooms' storeys are the only ones it can show (their floor bands, their lights floor
            // by floor). Anywhere else the asset's own windows lead. Only while a storey is a few
            // pixels tall: further out the texture is an average and cannot tell.
            float GlassBox(float2 uv, float illum, float footprint, float4 rect, float2 upUV)
            {
                float2 up = upUV * _RGRoom.x;
                float2 uvUp = uv + up, uvDown = uv - up;
#if !defined(RG_NEAR)
                uvUp = clamp(uvUp, rect.xy, rect.xy + rect.zw);
                uvDown = clamp(uvDown, rect.xy, rect.xy + rect.zw);
#endif
                float bUp = tex2D(_ACIMap, uvUp).b;
                float bDown = tex2D(_ACIMap, uvDown).b;
                float glassUp = 1.0 - tex2D(_XYSMap, uvUp).z;
                float glassDown = 1.0 - tex2D(_XYSMap, uvDown).z;
                float even = 1.0 - saturate((max(abs(bUp - illum), abs(bDown - illum)) - 0.03) * 20.0);
                float glassy = smoothstep(_RGMask.x - 0.1, _RGMask.x, min(glassUp, glassDown));
                float sure = saturate(_RGRoom.x / (footprint * 4.0) - 0.5);
                return even * glassy * sure * step(1e-14, dot(up, up));
            }

#if defined(RG_NEAR)
            // The window the asset painted round this pixel: where its glass stops above and below,
            // looked for on a fixed grid of world heights (so every pixel of one window finds the
            // same edges), up to 4 m each way. x the room's floor, y its ceiling (world y); 0, 0 when
            // none was found and the storeys count up from the base. A tall window is glass from
            // floor to ceiling; a short one sits on a sill.
            float2 PaintedWindow(float2 uv, float3 P, float2 upUV)
            {
                const float stepM = 0.25;
                float y0 = floor(P.y / stepM) * stepM;
                float threshold = _RGMask.x - 0.08;
                float top = 0.0, bottom = 0.0;
                bool up = false, down = false;
                [loop]
                for (int k = 1; k <= 16; k++)
                {
                    if (!up)
                    {
                        float yk = y0 + k * stepM;
                        float spec = 1.0 - tex2Dlod(_XYSMap, float4(uv + upUV * (yk - P.y), 0.0, 0.0)).z;
                        if (spec < threshold)
                        {
                            top = yk - stepM * 0.5;
                            up = true;
                        }
                    }
                    if (!down)
                    {
                        float yk = y0 - (k - 1) * stepM;
                        float spec = 1.0 - tex2Dlod(_XYSMap, float4(uv + upUV * (yk - P.y), 0.0, 0.0)).z;
                        if (spec < threshold)
                        {
                            bottom = yk + stepM * 0.5;
                            down = true;
                        }
                    }
                    if (up && down)
                        break;
                }

                float2 room = float2(0.0, 0.0);
                float h = top - bottom;
                if (up && down && h > 0.9 && h < 7.0)
                {
                    float floorY = h >= 2.2 ? bottom : bottom - 0.9;
                    room = float2(floorY, h >= 2.2 ? top : max(top + 0.3, floorY + 2.6));
                }
                return room;
            }
#endif

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
                o.building = _ObjectIndices[index].xy;
#elif defined(RG_GROUP)
                place = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                state = float4(0.0, 100000.0, 0.0, 1.0);   // baked: nothing squashed, nothing extended
                baseWeight = 0.0;
                o.building = v.texcoord1.xy;               // BuildingAI.PopulateGroupData: uvs2 = the colour index
#else
                place = float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
                state = _BuildingState;
                baseWeight = v.color.a;
                o.building = _ObjectIndex.xy;
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

                // This building: its lamps, what it is, whether it is a plain glass box, and the
                // window it painted round this pixel. (The derivatives outside every branch.)
                float2 upUV = UvPerMetreUp(uv, P, t);
                float seed = BuildingSeed(i.building);
                float F0 = _RGGlass.x;
                float clarity = _RGGlass.z;
                float warmth = BuildingWarmth(i.building, seed);
                float use = BuildingUse(i.building, seed);
                float box = GlassBox(uv, aci.b, footprint, i.rect, upUV);
                float2 window = float2(0.0, 0.0);
#if defined(RG_NEAR)
                if (_RGFeatures.z > 0.5 && _RGRoom.w > 0.5 && box < 0.5 && footprint < 1.0)
                    window = PaintedWindow(uv, P, upUV);
#endif

                // The glass's colour: the chosen glass, and as much of the building's own painted
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

                float2 paneCell;
                float3 np = PaneNormal(n, t, P, baseY, footprint, paneCell);
                float facing = dot(np, -V);
                if (facing < 0.01)
                    np = normalize(np + V * (facing - 0.01));   // a bent pane never faces away
                float cosT = saturate(dot(np, -V));
                float Fs;
                float3 F = GlassFresnel(cosT, F0, coat, Fs);
                float3 R = reflect(V, np);

                // Steady glints: where the pane's normal changes within the pixel, the glint and the
                // reflection widen to match instead of flickering (Kaplanyan et al. 2016; Tokuyoshi &
                // Kaplanyan 2019; Filament's constants: the kernel's screen-space variance 0.15, at
                // most 0.2 added to alpha squared).
                float3 dnx = ddx(np), dny = ddy(np);
                float kernel = min(2.0 * 0.15 * (dot(dnx, dnx) + dot(dny, dny)), 0.2) * _RGFeatures.w;
                float rough = _RGRefl.y;
                float alpha = rough * rough;
                rough = sqrt(sqrt(saturate(alpha * alpha + kernel)));

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 gamePixel = tex2Dlod(_RGScene, float4(suv, 0.0, 0.0)).rgb;

                // What the glass reflects: the screen where the ray finds something, else the
                // ground at the building's feet (below the horizon) or the sky.
                float hz = smoothstep(-0.03, 0.03, R.y);
                float3 sky = SkyRadiance(R, rough);
                float3 fallback = sky;
                if (hz < 1.0)
                    fallback = lerp(GroundFill(P, R, rough, baseY), sky, hz);

                // The buildings round: where one stands in the reflected ray's way, above the
                // horizon or below it, the glass mirrors that building, not the sky or the street.
                float cityState = 0.0;   // 0 no map, 1 a building met, 2 none
                if (_RGFeatures.x > 0.5 && _RGCityMapST.w > 0.5)
                {
                    float3 cityOrigin = P + n * max(0.3, dist * 0.003);
                    float tCity, hitId;
                    float3 hitN;
                    cityState = 2.0;
                    if (CityHit(cityOrigin, R, IdOf(i.building), baseY - 1.0, tCity, hitId, hitN))
                    {
                        // How many pixels a storey of it covers in the reflection: a storey at the
                        // ray's whole length, over the pixel's angle, blurred by the roughness.
                        float span = _RGRoom.x * dist / max((dist + tCity) * footprint, 1e-4) / (1.0 + rough * 8.0);
                        float3 hitPoint = cityOrigin + R * tCity;
                        // The building met, as the screen shows it there (its colours, its haze),
                        // blurred; the city's average where it is off screen.
                        bool finishedPicture = _RGFinished.x > 0.5;
                        float2 hitScreen;
                        float3 seen = tex2Dlod(_RGCity, float4(0.5, 0.5, 0.0, 0.0)).rgb;
                        if (ScreenUV(hitPoint, finishedPicture, hitScreen) && all(hitScreen > 0.0) && all(hitScreen < 1.0))
                            seen = MirroredPicture(hitScreen, 2.5 + rough * 3.0, finishedPicture);
                        fallback = NeighbourRadiance(hitPoint, R, hitN, hitId, span, rough, seen);
                        cityState = 1.0;
                    }
                }

                if (_RGDebug.x > 1.5 && _RGDebug.x < 6.5)
                {
                    if (_RGDebug.x < 2.5)
                    {
                        // The depth texture here, against this pixel's own depth: grey bands every
                        // 50 m of what the texture says; red where it disagrees with us by > 2 m.
                        float sz = -SceneZ(suv * _ScreenParams.xy);
                        float own2 = -mul(UNITY_MATRIX_V, float4(P, 1.0)).z;
                        return float4(frac(sz / 50.0).xxx * (abs(sz - own2) > 2.0 ? float3(1.0, 0.2, 0.2) : float3(1.0, 1.0, 1.0)), 1.0);
                    }
                    if (_RGDebug.x < 3.5)
                        return float4(gamePixel, 1.0);
                    if (_RGDebug.x < 4.5)
                    {
                        // 4: which buildings are plain glass boxes (white).
                        return float4(lerp(float3(0.05, 0.05, 0.08), float3(1.0, 1.0, 1.0), box), 1.0);
                    }
                    if (_RGDebug.x < 5.5)
                    {
                        // 5: the finished picture where this pane's point was last frame.
                        float2 uvLast;
                        return float4(ScreenUV(P, true, uvLast) ? tex2Dlod(_RGSceneFinished, float4(uvLast, 0.0, 0.0)).rgb : float3(1.0, 0.0, 1.0), 1.0);
                    }
                    // 6: the city walk: green a building met, blue none, red no map.
                    return float4(cityState > 1.5 ? float3(0.05, 0.1, 1.0) : (cityState > 0.5 ? float3(0.1, 1.0, 0.1) : float3(1.0, 0.0, 0.0)), 1.0);
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
                        // The point hit, read in the finished picture where it was last frame
                        // (the copy before the fog at the hit itself when it was off screen).
                        float3 hitWorld = origin + R * hitDistance;
                        float2 uvLast;
                        bool last = _RGFinished.x > 0.5 && ScreenUV(hitWorld, true, uvLast) &&
                                    all(uvLast >= 0.0) && all(uvLast <= 1.0);
                        float3 onScreen = last ? MirroredPicture(uvLast, lod, true) : MirroredPicture(hitUV, lod, false);
                        refl = lerp(fallback, onScreen, edge);
                        traceState = 1.0;
                    }
                    else
                    {
                        traceState = clipped ? 2.0 : 3.0;
                    }
                }
                if (_RGDebug.x > 0.5 && _RGDebug.x < 1.5)
                    return float4(traceState == 1.0 ? float3(0.0, 1.0, 0.0) : (traceState == 2.0 ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.2, 1.0)), 1.0);

                // Through the glass.
                float3 dayRoom = float3(0.0, 0.0, 0.0);
                float3 nightRoom = gamePixel;
                float roomsOn = _RGRoom.w * saturate(1.0 - abs(n.y) * 2.0);
                if (roomsOn > 0.0)
                {
                    float3 d, nt;
                    RoomRadiance(P, V, n, t, baseY, footprint, paneCell, gamePixel, seed, warmth, use, box, window, d, nt);
                    dayRoom = d * roomsOn;
                    nightRoom = lerp(gamePixel * _RGRoom2.w, nt, roomsOn);
                    if (_RGDebug.x > 6.5)
                    {
                        // 7: the pictured rooms by day, lit alike; 8: where the painted window was
                        // found (green, the room's floor and ceiling from it) and where not (red).
                        if (_RGDebug.x < 7.5)
                            return float4(d / max(_RGRoom2.x * max(Luma(_RGAmbSky.rgb), 0.05), 1e-3) * 0.5, 1.0);
                        float found = window.y > window.x + 0.5 ? 1.0 : 0.0;
                        float edge = found * step(frac((P.y - window.x) / max(window.y - window.x, 0.5) + 0.02), 0.04);
                        return float4(lerp(float3(0.8, 0.1, 0.1), float3(0.1, 0.8, 0.2), found) + edge, 1.0);
                    }
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
                float3 through = dayRoom * (clarity * filter) * daylight
                               + nightRoom * nightFilter * (1.0 - daylight);

                float3 L = F * refl * _RGRefl.x
                         + (1.0 - Fs) * through
                         + SunGlint(np, V, Fs, rough) * (F / max(Fs, 1e-4));

                return float4(L, mask * _RGGlass.w);
            }
            ENDCG
        }
    }
}
