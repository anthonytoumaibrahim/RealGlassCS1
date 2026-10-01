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
// far: the baked second uv) -- what the city walk knows its own building by.
//
// Queue 2499, the last of the opaque range: Unity draws it forward after the deferred final pass
// and BEFORE the game's fog ([ImageEffectOpaque]), so the glass is fogged like the rest of the
// city. What is not glass is discarded; glass is REPLACED (alpha = mask x strength). Behind it,
// by day, a dark inside; by night the game's own picture of the window, untouched -- which
// windows are lit, how brightly and in what colour stays each asset's and the game's.
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

            // Texture coordinates per metre ALONG the facade (xy) and UP it (zw), from the pixel's
            // own derivatives: the screen-space changes of uv and of the facade position (along,
            // up) give the 2 x 2 map between them.
            float4 UvPerMetre(float2 uv, float3 P, float3 t)
            {
                float2 duvx = ddx(uv), duvy = ddy(uv);
                float3 dPx = ddx(P), dPy = ddy(P);
                float2 sx = float2(dot(dPx, t), dPx.y);
                float2 sy = float2(dot(dPy, t), dPy.y);
                float det = sx.x * sy.y - sy.x * sx.y;
                return abs(det) > 1e-10 ? float4(duvx * sy.y - duvy * sx.y, duvy * sx.x - duvx * sy.x) / det : float4(0.0, 0.0, 0.0, 0.0);
            }

#if defined(RG_NEAR)
            // The window the asset painted round this pixel: where its glass stops above and below,
            // looked for on a fixed grid of world heights (so every pixel of one window finds the
            // same edges), _RGBudget.w steps (0.25 m) each way. (bottom, top), world y; 0, 0 when
            // none was found (or a window under 0.9 m or over 7 m: not a pane).
            float2 PaintedWindow(float2 uv, float3 P, float2 upUV)
            {
                const float stepM = 0.25;
                float y0 = floor(P.y / stepM) * stepM;
                float threshold = _RGMask.x - 0.08;
                float top = 0.0, bottom = 0.0;
                bool up = false, down = false;
                [loop]
                for (int k = 1; k <= 32; k++)
                {
                    if (k > _RGBudget.w)
                        break;
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

                float h = top - bottom;
                return up && down && h > 0.9 && h < 7.0 ? float2(bottom, top) : float2(0.0, 0.0);
            }

            // The same across: where the asset's glass stops left and right of this pixel along the
            // facade (x = metres along t), on a fixed 0.1 m grid (painted mullions are thin: 0.25 m
            // steps walked over them), _RGBudget2.x steps each way. (left, right), or 0, 0 when
            // either side has none within reach.
            float2 PaintedPaneSpan(float2 uv, float x, float2 alongUV)
            {
                const float stepM = 0.1;
                float x0 = floor(x / stepM) * stepM;
                float threshold = _RGMask.x - 0.08;
                float right = 0.0, left = 0.0;
                bool r = false, l = false;
                [loop]
                for (int k = 1; k <= 32; k++)
                {
                    if (k > _RGBudget2.x)
                        break;
                    if (!r)
                    {
                        float xk = x0 + k * stepM;
                        float spec = 1.0 - tex2Dlod(_XYSMap, float4(uv + alongUV * (xk - x), 0.0, 0.0)).z;
                        if (spec < threshold)
                        {
                            right = xk - stepM * 0.5;
                            r = true;
                        }
                    }
                    if (!l)
                    {
                        float xk = x0 - (k - 1) * stepM;
                        float spec = 1.0 - tex2Dlod(_XYSMap, float4(uv + alongUV * (xk - x), 0.0, 0.0)).z;
                        if (spec < threshold)
                        {
                            left = xk + stepM * 0.5;
                            l = true;
                        }
                    }
                    if (r && l)
                        break;
                }
                return r && l && right - left > 0.4 ? float2(left, right) : float2(0.0, 0.0);
            }
#endif

            // The lines the asset painted on its glass: a floor slab's band, a mullion, a frame --
            // where its colour, or its shine, stands out from the storey round it. A whole facade
            // marked shiny (a full-glass tower) still says where its floors are in its colour map.
            // Unsharp masking (Schreiber 1970): the pixel against a storey-wide average of the same
            // map, which is a coarser mip of it (Williams 1983) -- the pixel's own level biased by
            // how many of its footprints a storey spans, so no texture size is needed. Far away
            // the pixel's own level reaches the storey's and nothing stands out: no shimmer.
            // 0 glass .. 1 the asset's own painting. Top level only: the bias needs derivatives.
            float PaintedWall(float2 uv, float footprint, float3 albedo, float specHere)
            {
                float bias = clamp(log2(_RGPane2.z / footprint), 0.0, 12.0);
                float3 around = tex2Dbias(_MainTex, float4(uv, 0.0, bias)).rgb;
                float specAround = 1.0 - tex2Dbias(_XYSMap, float4(uv, 0.0, bias)).z;

                // Only where the storey round is as shiny as this pixel: where the asset's shine map
                // already parts glass from wall (punched windows, spandrels painted dull) it has
                // said what is glass, and a pane darker than the wall round it is still a pane.
                float allGlass = 1.0 - smoothstep(0.03, 0.08, specHere - specAround);

                // Darker than round it by a fifth and more: a slab band, a shadowed frame. Lighter
                // by over half: a light frame or line. (Glass between dark bands is a little lighter
                // than their average, and a painted sky drifts slowly: neither is a line.)
                float tone = log2((Luma(albedo) + 0.03) / (Luma(around) + 0.03));
                float stroke = max(smoothstep(0.35, 0.75, -tone), smoothstep(0.70, 1.10, tone));

                // Duller than round it, though shiny enough to count as glass: a spandrel.
                float duller = smoothstep(0.06, 0.18, specAround - specHere);
                return max(stroke * allGlass, duller);
            }

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

                // This building's own settings: its asset's, where the player gave it some
                // (AssetTextures.cs, at the colour index every tier carries); 255 in a channel is
                // the global setting. Strength 0: nothing of ours on it at all.
                float4 asset = tex2Dlod(_RGAssets, float4(i.building, 0.0, 0.0));
                float strength = asset.r < 0.999 ? asset.r * (255.0 / 254.0) : _RGGlass.w;
                float detailKept = asset.g < 0.999 ? asset.g * (255.0 / 254.0) : _RGGlass2.y;
                clip(strength - 0.004);

                float3 P = i.worldPos.xyz;
                float baseY = i.worldPos.w;
                float3 n = normalize(i.worldNormal.xyz);
                float3 toP = P - _WorldSpaceCameraPos;
                float dist = length(toP);
                float3 V = toP / max(dist, 1e-3);
                float footprint = max(length(fwidth(P)), 1e-4);
                float3 t = FacadeTangent(n);

                // Close up, the pane the building painted round this pixel: its top and bottom, then
                // its sides. (The derivatives outside every branch.)
                float4 uvPerMetre = UvPerMetre(uv, P, t);
                float F0 = _RGGlass.x;
                float clarity = _RGGlass.z;
                float4 tintOwn = _RGTint;
                if (asset.b < 0.999)
                {
                    // The asset's own glass: slot k = b x 255, the centre of texel k of 256.
                    float u = asset.b * (255.0 / 256.0) + 0.5 / 256.0;
                    tintOwn = tex2Dlod(_RGAssetGlass, float4(u, 0.25, 0.0, 0.0));
                    float4 numbers = tex2Dlod(_RGAssetGlass, float4(u, 0.75, 0.0, 0.0));
                    F0 = numbers.r;
                    clarity = numbers.g;
                }
                float4 painted = float4(0.0, 0.0, 0.0, 0.0);   // the painted pane: left, right (along t), bottom, top
#if defined(RG_NEAR)
                if (_RGBudget2.x > 0.5 && footprint < 0.5)   // where a pane is a few pixels wide
                {
                    float2 glassSpan = PaintedWindow(uv, P, uvPerMetre.zw);
                    if (glassSpan.y > glassSpan.x)
                    {
                        float2 across = PaintedPaneSpan(uv, dot(P, t), uvPerMetre.xy);
                        if (across.y > across.x)
                            painted = float4(across, glassSpan);
                    }
                }
#endif

                // The glass's colour: the chosen glass, and as much of the building's own painted
                // glass colour as the "own colour" setting keeps. The data sheets' numbers are
                // LUMINOUS (visible light transmittance / reflectance, eye-weighted), so both
                // filters are scaled to a luminance of 1: "clarity 24%" lets 24% of the light
                // through, whatever the colour; "reflectivity 20%" reflects 20%.
                float3 albedo = tex2D(_MainTex, uv).rgb;
                float3 own = NormaliseTint(albedo);
                float3 tint = NormaliseTint(lerp(tintOwn.rgb, own, tintOwn.a));
                float lines = PaintedWall(uv, footprint, albedo, 1.0 - xys.z);
                float wall = lines * detailKept;
                if (_RGDebug.x > 9.5 && _RGDebug.x < 10.5)
                    return float4(lerp(float3(0.05, 0.15, 0.6), float3(1.0, 0.85, 0.2), lines), 1.0);   // 10: the painted lines (yellow) on the glass (blue)
                float3 filter = lerp(float3(1.0, 1.0, 1.0), tint, 0.6);
                filter /= max(Luma(filter), 0.05);
                float3 coat = lerp(float3(1.0, 1.0, 1.0), tint, _RGMask.w);
                coat /= max(Luma(coat), 0.05);

                float2 paneCell;
                float3 np = PaneNormal(n, t, P, baseY, footprint, painted, paneCell);
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
                        float span = _RGPane2.z * dist / max((dist + tCity) * footprint, 1e-4) / (1.0 + rough * 8.0);
                        float3 hitPoint = cityOrigin + R * tCity;

                        // A stand-in: the building as the screen shows it there (its colours, its
                        // haze), blurred; the city's average where it is off screen.
                        bool finishedPicture = _RGFinished.x > 0.5;
                        float2 hitScreen;
                        float3 seen = tex2Dlod(_RGCity, float4(0.5, 0.5, 0.0, 0.0)).rgb;
                        if (ScreenUV(hitPoint, finishedPicture, hitScreen) && all(hitScreen > 0.0) && all(hitScreen < 1.0))
                            seen = MirroredPicture(hitScreen, 2.5 + rough * 3.0, finishedPicture);
                        fallback = NeighbourRadiance(hitPoint, R, hitN, span, rough, seen);
                        cityState = 1.0;
                    }
                }

                if (_RGDebug.x > 8.5 && _RGDebug.x < 9.5)
                {
                    // 9: the hierarchical depth at level _RGDebug.y, its nearest in grey bands every
                    // 50 m; red where the level does not hold the depth texture here between its
                    // nearest and farthest (level 0: the depth itself), blue where there is none.
                    if (_RGHiZInfo.w < 0.5)
                        return float4(0.1, 0.2, 1.0, 1.0);
                    float lvl = min(_RGDebug.y, _RGHiZInfo.x - 1.0);
                    float2 pix = suv * _RGHiZInfo.yz;
                    float2 hzr = HiZRange(floor(pix / exp2(lvl)), lvl);
                    float szd = -SceneZ(suv * _ScreenParams.xy);
                    float tol = max(0.05, szd * 0.002);
                    bool wrong = hzr.x > szd + tol || hzr.y < szd - tol;
                    return float4(frac(hzr.x / 50.0).xxx * (wrong ? float3(1.0, 0.1, 0.1) : float3(1.0, 1.0, 1.0)), 1.0);
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
                    if (_RGDebug.x < 4.5)
                        return float4(gamePixel, 1.0);   // 3 (and 4, which was the plain-glass towers): the copy
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
                    if (TraceReflection(csOrig, csDir, Ign(i.pos.xy), hitUV, hitDistance, clipped))
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

                if (_RGDebug.x > 7.5 && _RGDebug.x < 8.5)
                {
                    // 8: the painted pane found round this pixel (green, its border yellow), or not
                    // (red: the panes' own grid).
                    float along = dot(P, t);
                    float found = painted.y > painted.x ? 1.0 : 0.0;
                    float border = found * (min(min(along - painted.x, painted.y - along), min(P.y - painted.z, painted.w - P.y)) < 0.1 ? 1.0 : 0.0);
                    return float4(lerp(lerp(float3(0.8, 0.1, 0.1), float3(0.1, 0.8, 0.2), found), float3(1.0, 0.9, 0.1), border), 1.0);
                }

                // Through the glass. By day the inside of a building, dark (a share of the sky's
                // light, GlassUniforms.InsideByDay), seen through the glass's clarity and colour. By
                // night the game's own picture of this window, untouched: which windows are lit, how
                // brightly and in what colour is each asset's own night texture and the game's
                // timing -- nothing of ours. The reflections go on top either way.
                float daylight = _RGLight.x;
                float3 through = (clarity * filter) * (1.0 - Fs);
                float3 inside = _RGAmbSky.rgb * _RGLight.y * through;

                // The asset's painted lines (PaintedWall): the glass steps aside there and the
                // building shows as its author painted it. (_RGGlass2.z, the preview's other way:
                // the reflection kept over them, the painting under it instead of the dark inside.)
                float coverage = mask * strength;
                if (_RGGlass2.z > 0.5)
                    inside = lerp(inside, gamePixel * (1.0 - Fs), wall);
                else
                    coverage *= 1.0 - wall;

                float3 L = F * refl * _RGRefl.x
                         + lerp(gamePixel, inside, daylight)
                         + SunGlint(np, V, Fs, rough) * (F / max(Fs, 1e-4));

                return float4(L, coverage);
            }
            ENDCG
        }
    }
}
