// Real Glass: the glass of a vehicle, drawn a second time over the game's own draw of it.
//
// The game already knows which of a vehicle's pixels are windows: its vehicle shaders (Default,
// Train, Aircraft, Ship; their disassembly, 2026-10-23) take spec = 1 - _XYSMap.z over 0.8..0.9 as
// glass -- flattening the normal map there and showing a floor texture through it (_FloorParams,
// _VehicleFloorDiffuse) -- except in the top 0.2 m of each deck. This draw takes that as its mask (in
// full from 0.82, so none of the floor is left: GameVehicleGlass), so nothing becomes glass that the
// game does not already show as a window. Its LAMPS get a
// lens too: the game marks them in the illumination channel (_ACIMap.b away from 0.5; the vehicle
// shaders light them by the vehicle's _LightState), and a lens is glass over the game's own lamp.
//
// Behind a window, a dark inside: the sky's light through the glass, a share of it (the game's
// floor, added unlit, glowed through every window at night: his "we don't want lights inside"; the
// small light inside a few vehicles at night went too, 2026-10-23: "it's not needed"). Where the
// GAME lights a window -- a train's inside at night (Custom/Vehicles/Vehicle/Train: _ACIMap.b under
// 0.476, 3x a warm white after dusk) -- that stays the game's at night, as the buildings' lit windows
// do. Behind a lens, the game's lamp, lit or not. The glass is automotive, not architectural
// (VehicleGlassLook.cs): soda-lime (n ~1.52), a few per cent reflected head-on, clear -- or, on the
// share of vehicles the Vehicles tab's "Tinted windows" asks for, chosen per vehicle by
// _RGVehicleSeed (a car keeps its glass as it drives), behind a tint film of the car's own shade.
//
// The Reflections slider scales the REFLECTANCE (the glass's and the coat's, capped at a mirror), and
// what is under them lets through only the rest: a strong setting is a mirror, not a veil of light.
//
// The PAINT gets a clear coat (Filament's clear-coat layer; his ask, 2026-10-23: the whole car
// reflective "with the exception of the wheels and the non-shiny parts"): lacquer, n 1.5, its
// reflection on top and the game's paint under it times 1 - Fc. The paint is what the game recolours
// (the colour mask): tyres, rims, trim and grilles are not -- and the specular map cannot say, most
// vehicle authors leave the paint's at 0.
//
// Drawn with GPU instancing (one draw per vehicle model in view: Graphics.DrawMeshInstanced) or one
// by one where instancing is missing; either way the vehicle's own mesh at the body matrix the game
// drew it with. Queue 2499 like the buildings' glass: after the deferred final pass, before the fog.
Shader "RealGlass/VehicleGlass"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _XYSMap ("Normal XY, specular", 2D) = "gray" {}
        _ACIMap ("Alpha, colour, illumination", 2D) = "black" {}
        _FloorParams ("Floor (the vehicle's own)", Vector) = (0.5, 2.0, 2.5, 5.0)
        _RGVehicleShader ("The game's vehicle shader (0 Default, 1 Train, 2 windows only: Aircraft, Ship)", Float) = 0
        _RGVehicleST ("The game material's _MainTex_ST", Vector) = (1, 1, 0, 0)
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
            // Premultiplied: a window replaces the pixel by its coverage, the coat adds its reflection
            // and takes Fc of the paint under it.
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "GlassCommon.cginc"

            sampler2D _MainTex;
            sampler2D _XYSMap;
            sampler2D _ACIMap;
            float4 _RGVehicleST;    // the game material's _MainTex_ST: its vertex program scales the uv by it
            float4 _FloorParams;    // the vehicle material's: x floor height, y deck height (local y, m), zw the floor texture's size
            float _RGVehicleShader; // which of the game's vehicle shaders drew it: 0 Default (cars, buses, trucks), 1 Train,
                                    // 2 Aircraft / Ship (their windows; their lamps' code not read: no lenses)

            // VehicleGlassLook.cs writes these.
            float4 _RGVehicle;      // clear glass's: x reflectance head-on (F0), y reflectance edge-on, z clarity (VLT); w strength
            float4 _RGVehicle2;     // x reflectance gain (the Reflections slider x 3), y roughness, z sun glint, w the edge-on reflectance asked for
            float4 _RGVehicle3;     // x the share of vehicles with tinted windows, y lamps get a lens (1), z w the lightest and darkest film's VLT
            float4 _RGVehicleTint;  // rgb the light clear glass lets through (linear, brightest 1)
            float4 _RGVehicleFilm;  // rgb the light a tint film lets through (linear, brightest 1)
            float4 _RGVehicle4;     // x the paint's clear coat (0 none .. 1), y its roughness, z what is paint (0 the colour mask,
                                    // 1 also an unmasked body light enough to be a livery), w the cabin's light (x the sky's)

            // Each vehicle's own random number, 0..1 (VehicleGlassDraw: from its id), per instance.
            UNITY_INSTANCING_CBUFFER_START(RGVehicle)
                UNITY_DEFINE_INSTANCED_PROP(float, _RGVehicleSeed)
            UNITY_INSTANCING_CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 worldPos : TEXCOORD0;     // xyz position, w the vehicle's ground (its origin's world y)
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float4 local : TEXCOORD4;        // xyz this point in the vehicle's own space (y: the decks' rule), w its seed
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.worldPos = float4(world.xyz, mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).y);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.texcoord.xy * _RGVehicleST.xy + _RGVehicleST.zw;
                o.screenPos = ComputeScreenPos(o.pos);
                o.local = float4(v.vertex.xyz, UNITY_ACCESS_INSTANCED_PROP(_RGVehicleSeed));
                return o;
            }

            // Where this point is in its deck, 0 at the floor .. 1 at the top. Decks: the first from the
            // floor (x) to the deck height (y), then every y above it (the game's vehicle shaders).
            float DeckPart(float y, out float period)
            {
                float4 F = _FloorParams;
                float below = F.y >= y ? 1.0 : 0.0;
                float base = below > 0.5 ? F.x : F.y;
                period = max(below > 0.5 ? F.y - F.x : F.y, 1e-3);
                float q = (y - base) / period;
                return q >= 0.0 ? frac(abs(q)) : -frac(abs(q));
            }

            // The game's own vehicle glass (its final pass): the shine over 0.8, in every deck but its top
            // 0.2 m -- ours in full from 0.82. The game calls it a window by a smoothstep over 0.8..0.9 and
            // shows its floor there at that share: _VehicleFloorDiffuse met by the view ray on the deck's
            // floor, cut off at a hard-edged rectangle (_FloorParams.zw) and added unlit. Taking only that
            // share, as the game does, left w (1 - w) of the floor under the glass: on a vehicle whose
            // windows are painted at 0.8..0.9 (a quarter of the surveyed cars), a square of floor and seats
            // showing through every window, glowing at night (2026-10-28). Full from 0.82, where the game's
            // share is still a tenth, nothing of it is left; under 0.82 the glass's edge, a texel at most.
            float GameVehicleGlass(float spec, float y)
            {
                float period;
                float part = DeckPart(y, period);
                float h = part * period;
                float inDeck = period - 0.2 >= h ? 1.0 : 0.0;
                return saturate((spec - 0.8) * 50.0) * inDeck;
            }

            // The game's lamps (its final passes): _ACIMap.b over ~0.545 the head and tail lights on both
            // shaders (lit by _LightState.x at the front, .y at the rear); under ~0.4545 the indicators on
            // Default -- on Train that range is its lit inside, not a lamp (TrainLitWindow).
            float GameLamp(float b)
            {
                float headTail = _RGVehicleShader < 1.5 ? smoothstep(0.525, 0.565, b) : 0.0;
                float indicator = _RGVehicleShader < 0.5 ? smoothstep(0.475, 0.435, b) : 0.0;
                return max(headTail, indicator);
            }

            // A train's window the game lights after dusk (Train's final pass: 3 x (1, 1, 0.8) x
            // saturate(1 - 2.1 b)).
            float TrainLitWindow(float b)
            {
                return abs(_RGVehicleShader - 1.0) < 0.5 ? saturate(1.0 - 2.1 * b) : 0.0;
            }

            // What of the body is paint under a clear coat: what the game recolours (_ACIMap.g under 0.5,
            // the colour mask), and -- _RGVehicle4.z -- an unmasked body light enough to be a livery (a
            // police car's, a truck's), not the near-black of tyres, trim and grilles. Not the specular
            // map: most vehicle authors leave the paint's at 0 (the asset survey, 2026-10-23).
            float CoatPaint(float aciG, float3 albedo)
            {
                float masked = smoothstep(0.6, 0.4, aciG);
                float livery = smoothstep(0.015, 0.05, Luma(albedo)) * _RGVehicle4.z;
                return max(masked, livery);
            }

            // Schlick (1994) from the glass's head-on reflectance to its edge-on one (uncoated glass: the
            // same for every colour), as physics has it.
            float VehicleFresnel(float cosTheta, float F0, float Fmax)
            {
                float m = 1.0 - saturate(cosTheta);
                float m2 = m * m;
                return F0 + (max(Fmax, F0) - F0) * m2 * m2 * m;
            }

            // Head-on reflectance of a slab of soda-lime letting vlt through (VehicleGlassLook.SlabReflectance:
            // the two surfaces and the light passing between them; a dark pane reflects less).
            float SlabReflectance(float vlt)
            {
                const float r = 0.0426;
                const float k = (1.0 - r) * (1.0 - r);
                float t = vlt / k;
                t = saturate(vlt * (1.0 - r * r * t * t) / k);
                return r + k * r * t * t / (1.0 - r * r * t * t);
            }

            // The sun's glint (GGX, Walter et al. 2007), capped as the buildings' (GlassCommon SunGlint),
            // at the vehicle glass's own strength.
            float3 VehicleGlint(float3 n, float3 V, float F, float roughness)
            {
                float3 L = _RGSunDir.xyz;
                float nl = saturate(dot(n, L));
                float3 H = SafeNormalize(L - V, L);
                float nh = saturate(dot(n, H));
                float nv = max(dot(n, -V), 0.05);
                float a = max(roughness * roughness, 0.0016);
                float a2 = a * a;
                float d = nh * nh * (a2 - 1.0) + 1.0;
                float D = a2 / (3.14159265 * d * d);
                float spec = min(D * F * 0.25 / nv, 60.0);
                return _RGSunColor.rgb * spec * nl * saturate(_RGSunDir.w) * _RGVehicle2.z * SunThrough();
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 xys = tex2D(_XYSMap, i.uv);
                float4 aci = tex2D(_ACIMap, i.uv);
                float3 albedo = tex2D(_MainTex, i.uv).rgb;
                float opaque = step(aci.r, 0.5);
                float window = GameVehicleGlass(1.0 - xys.z, i.local.y);
                float lamp = GameLamp(aci.b) * _RGVehicle3.y;
                float glass = max(window, lamp) * opaque;
                // The clear coat over the paint (Filament's clear-coat layer: the paint under it times
                // 1 - Fc, the coat's reflection on top), wherever the body is not glass. On the Default
                // vehicle shader only (cars, vans, trucks, buses): its colour mask is the one read.
                float paint = _RGVehicle4.x > 0.0 && _RGVehicleShader < 0.5 ? CoatPaint(aci.g, albedo) * opaque * (1.0 - max(window, lamp)) : 0.0;
                clip(max(glass, paint) - 0.004);
                // How much of this pixel's glass is a lamp's lens (the rest is a window).
                float lens = saturate(lamp / max(glass, 1e-3));
                float coatShare = paint / max(glass + paint, 1e-3);

                if (_RGMask.z > 0.5)   // the mask view, as the buildings': glass magenta, the coat cyan
                {
                    float shown = max(max(glass, paint), 0.35);
                    return float4(lerp(float3(1.0, 0.0, 1.0), float3(0.0, 1.0, 1.0), coatShare) * shown, shown);
                }

                float strength = _RGVehicle.w;
                clip(strength - 0.004);

                float3 P = i.worldPos.xyz;
                float groundY = i.worldPos.w;
                float3 toP = P - _WorldSpaceCameraPos;
                float dist = length(toP);
                float3 V = toP / max(dist, 1e-3);
                float3 n = SafeNormalize(i.worldNormal, float3(0.0, 1.0, 0.0));
                float facing = dot(n, -V);
                if (facing < 0.01)
                    n = normalize(n + V * (facing - 0.01));   // a smoothed normal never faces away
                float cosT = saturate(dot(n, -V));

                // This vehicle's glass: clear, or -- the share _RGVehicle3.x of vehicles, by its own number --
                // behind a tint film of its own shade, from the lightest to the darkest, evenly in proportion.
                // Road vehicles only (the Default shader: cars, vans, trucks, buses): no tinted trains.
                float seed = i.local.w;
                bool tinted = seed < _RGVehicle3.x && _RGVehicleShader < 0.5;
                float vlt = tinted ? _RGVehicle3.z * pow(_RGVehicle3.w / max(_RGVehicle3.z, 1e-3), frac(seed * 97.13)) : _RGVehicle.z;
                float F0 = tinted ? SlabReflectance(vlt) : _RGVehicle.x;
                float Fmax = tinted ? _RGVehicle2.w : _RGVehicle.y;
                float3 tint = NormaliseTint(tinted ? _RGVehicleFilm.rgb : _RGVehicleTint.rgb);
                float3 filter = tint / max(Luma(tint), 0.05);
                // The Reflections slider's gain on the reflectance, capped at a mirror: what the glass
                // reflects, the window's inside loses. A lamp's lens (clear, untinted) keeps physics' share
                // for the lamp under it, so a lit lamp shines through at any setting.
                float FsLens = VehicleFresnel(cosT, _RGVehicle.x, _RGVehicle.y);
                float Fs = min(lerp(VehicleFresnel(cosT, F0, Fmax), FsLens, lens) * _RGVehicle2.x, 1.0);
                float3 R = reflect(V, n);

                // Steady glints, as the buildings' (Kaplanyan et al. 2016; Filament's constants): a
                // windscreen's curve within a pixel widens the reflection instead of flickering.
                float3 dnx = ddx(n), dny = ddy(n);
                float kernel = min(2.0 * 0.15 * (dot(dnx, dnx) + dot(dny, dny)), 0.2) * _RGFeatures.w;
                float rough = lerp(_RGVehicle2.y, _RGVehicle4.y, coatShare);
                float alpha = rough * rough;
                rough = sqrt(sqrt(saturate(alpha * alpha + kernel)));

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 gamePixel = Finite(tex2Dlod(_RGScene, float4(suv, 0.0, 0.0)).rgb);

                // What the glass mirrors when the screen has nothing: the road under the vehicle below the
                // horizon, the sky above it -- or a building standing in the ray's way (the city walk; a
                // vehicle is no building: nothing of the map is its own).
                float hz = smoothstep(-0.03, 0.03, R.y);
                float3 sky = SkyRadiance(R, rough);
                float3 fallback = sky;
                if (hz < 1.0)
                    fallback = lerp(GroundFill(P, R, rough, groundY), sky, hz);
                float cityState = 0.0;   // 0 no map, 1 a building met, 2 none (the preview's Debug=6)
                if (_RGFeatures.x > 0.5 && _RGCityMapST.w > 0.5)
                {
                    float3 cityOrigin = P + n * max(0.05, dist * 0.002);
                    float tCity, hitId;
                    float3 hitN;
                    cityState = 2.0;
                    if (CityHit(cityOrigin, R, -1.0, groundY - 1.0, tCity, hitId, hitN))
                    {
                        cityState = 1.0;
                        float footprint = max(length(fwidth(P)), 1e-4);
                        float span = _RGPane2.z * dist / max((dist + tCity) * footprint, 1e-4) / (1.0 + rough * 8.0);
                        float3 hitPoint = cityOrigin + R * tCity;
                        bool finishedPicture = _RGFinished.x > 0.5;
                        float2 hitScreen;
                        float3 seen = CityColour();
                        if (ScreenUV(hitPoint, finishedPicture, hitScreen) && all(hitScreen > 0.0) && all(hitScreen < 1.0))
                            seen = MirroredPicture(hitScreen, 2.5 + rough * 3.0, finishedPicture);
                        fallback = NeighbourRadiance(hitPoint, R, hitN, span, rough, seen);
                    }
                }

                float3 refl = fallback;
                float traceState = 0.0;   // 1 hit, 2 left the screen, 3 ran out of steps (the preview's Debug=1)
                if (_RGTrace.x > 0.5)
                {
                    float3 origin = P + n * max(0.05, dist * 0.002);
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

                // The preview's checks, as the buildings' (_RGDebug.x; the mod never sets it): 1 the trace
                // (green hit, red left the screen, blue out of steps), 6 the city walk (green a building met,
                // blue none, red no map), 7 what the reflection is (white trace hit, yellow a building of the
                // walk, brown the road below the horizon, sky blue the sky).
                if (_RGDebug.x > 0.5 && _RGDebug.x < 1.5)
                    return float4(traceState == 1.0 ? float3(0.0, 1.0, 0.0) : (traceState == 2.0 ? float3(1.0, 0.0, 0.0) : float3(0.0, 0.2, 1.0)), 1.0);
                if (_RGDebug.x > 5.5 && _RGDebug.x < 6.5)
                    return float4(cityState > 1.5 ? float3(0.05, 0.1, 1.0) : (cityState > 0.5 ? float3(0.1, 1.0, 0.1) : float3(1.0, 0.0, 0.0)), 1.0);
                if (_RGDebug.x > 6.5 && _RGDebug.x < 7.5)
                {
                    float3 what = traceState == 1.0 ? float3(1.0, 1.0, 1.0) :
                                  (cityState == 1.0 ? float3(1.0, 0.85, 0.1) : (hz < 0.5 ? float3(0.45, 0.28, 0.12) : float3(0.35, 0.65, 1.0)));
                    return float4(what, 1.0);
                }

                // Through a window: a dark inside -- a share of the sky's light (the cabin's, _RGVehicle4.w),
                // through the glass's clarity and colour. A train's window the game lights after dusk stays
                // the game's at night.
                float daylight = _RGLight.x;
                float3 through = min(vlt * filter, 1.0) * (1.0 - Fs);
                float3 windowBehind = _RGAmbSky.rgb * _RGVehicle4.w * through;
                float gameLit = saturate(TrainLitWindow(aci.b) * 4.0) * (1.0 - daylight);
                windowBehind = lerp(windowBehind, gamePixel * through, gameLit);

                // Through a lamp's lens: the game's lamp, lit or not (clear plastic, untinted).
                float3 lensBehind = gamePixel * 0.92 * (1.0 - FsLens);
                float3 behind = lerp(windowBehind, lensBehind, lens);

                float3 front = Fs * refl + VehicleGlint(n, V, Fs, rough);
                float coverage = glass * strength;

                // The coat: clear lacquer, n 1.5 (F0 0.04, Filament), untinted, a mirror edge-on; the same
                // gain, the paint under it dimmed by what it reflects.
                float m = 1.0 - cosT;
                float m2 = m * m;
                float Fc = min((0.04 + 0.96 * m2 * m2 * m) * _RGVehicle2.x, 1.0);
                float coat = paint * strength * _RGVehicle4.x;
                float3 coatLight = Fc * refl + VehicleGlint(n, V, Fc, rough);
                return float4(Finite((front + behind) * coverage + coatLight * coat), FiniteAlpha(coverage + Fc * coat));
            }
            ENDCG
        }
    }
}
