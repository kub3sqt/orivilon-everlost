#ifndef EVERLOST_VALLEY_MIST_INCLUDED
#define EVERLOST_VALLEY_MIST_INCLUDED
// Shared by the opaque fullscreen pass and transparent water. No temporal noise.
//
// 24.10.2026: two layers.
//  1) Low layer above the sea (base height, falloff, extinction, max opacity) – analytic, as before.
//  2) Valley pockets: BiomeAtmosphere/ValleyMistField publishes a small world-anchored map around the
//     camera (r = valley floor height, g = how much the place is a valley, b = mist thickness in m).
//     Mist fills each valley from its own floor, also high inside mountain ranges, and stays off
//     ridges and open plains. The ray is integrated in a few fixed (non-jittered) steps.
float4 _EverlostValleyMist;      // base height, falloff height, extinction, maximum opacity
float4 _EverlostValleyMistColor;
float4 _EverlostValleyPocket;    // x = extinction at the floor, y = max opacity, z = origin x, w = origin z
float4 _EverlostValleyPocket2;   // x = 1 / map size (m), y = lift above floor (m), z = max ray length (m), w = enabled
float4 _EverlostMistSun;         // xyz = direction towards the sun, w = forward-scatter strength
float4 _EverlostMistSunColor;
TEXTURE2D(_EverlostValleyFloor);
SAMPLER(sampler_EverlostValleyFloor);

#define EVERLOST_MIST_START 35.0
#define EVERLOST_MIST_STEPS 12

float ValleyMistPrimitive(float height)
{
    float relative = height - _EverlostValleyMist.x;
    return relative <= 0.0 ? relative
        : _EverlostValleyMist.y * (1.0 - exp(-relative / _EverlostValleyMist.y));
}

float ValleySeaDepth(float3 start, float3 positionWS, float distanceWS, float startDistance)
{
    if (_EverlostValleyMist.z <= 0.0) return 0.0;
    float dy = positionWS.y - start.y;
    float averageDensity = abs(dy) < 0.02
        ? exp(-max(0.0, start.y - _EverlostValleyMist.x) / _EverlostValleyMist.y)
        : (ValleyMistPrimitive(positionWS.y) - ValleyMistPrimitive(start.y)) / dy;
    float patch = 0.86 + 0.14 * sin(positionWS.x * 0.009 + sin(positionWS.z * 0.006));
    return max(0.0, distanceWS - startDistance) * saturate(averageDensity) * _EverlostValleyMist.z * patch;
}

float ValleyPocketDensity(float3 p)
{
    float2 uv = (p.xz - _EverlostValleyPocket.zw) * _EverlostValleyPocket2.x;
    if (any(uv < 0.0) || any(uv > 1.0)) return 0.0;
    float4 f = SAMPLE_TEXTURE2D_LOD(_EverlostValleyFloor, sampler_EverlostValleyFloor, uv, 0);
    float above = p.y - (f.r + _EverlostValleyPocket2.y);
    // Soft top: dense below the lifted floor, exponential fade above it.
    float d = above <= 0.0 ? 1.0 : exp(-above / max(f.b, 1.0));
    float patch = 0.75 + 0.25 * sin(p.x * 0.011 + 1.3 * sin(p.z * 0.008));
    return f.g * d * patch;
}

// Optical depth of the valley pockets along the ray (fixed steps, stable from frame to frame).
float ValleyPocketDepth(float3 start, float3 positionWS)
{
    if (_EverlostValleyPocket2.w < 0.5 || _EverlostValleyPocket.x <= 0.0) return 0.0;
    float3 ray = positionWS - start;
    float len = length(ray);
    float marchLen = min(len, _EverlostValleyPocket2.z);
    if (marchLen <= 1.0) return 0.0;
    float3 dir = ray / len;
    // Quadratic spacing: fine steps near the camera, coarse far away where the distance fog takes over.
    float sum = 0.0;
    [unroll]
    for (int k = 0; k < EVERLOST_MIST_STEPS; k++)
    {
        float u = (k + 0.5) / EVERLOST_MIST_STEPS;
        sum += ValleyPocketDensity(start + dir * (u * u * marchLen)) * (2.0 * u / EVERLOST_MIST_STEPS);
    }
    return sum * marchLen * _EverlostValleyPocket.x;
}

void ValleyMistTerms(float3 positionWS, out half opacity, out half3 color)
{
    opacity = 0.0;
    color = _EverlostValleyMistColor.rgb;
    if ((_EverlostValleyMist.z <= 0.0 && _EverlostValleyPocket2.w < 0.5) || unity_OrthoParams.w > 0.5) return;
    float3 ray = positionWS - _WorldSpaceCameraPos;
    float distanceWS = length(ray);
    // Keep nearby grass crisp; mist starts a little way from the camera.
    float startDistance = min(EVERLOST_MIST_START, distanceWS);
    float3 dir = ray / max(distanceWS, 0.001);
    float3 start = _WorldSpaceCameraPos + dir * startDistance;

    float sea = ValleySeaDepth(start, positionWS, distanceWS, startDistance);
    float pocket = ValleyPocketDepth(start, positionWS);
    half seaOpacity = min(_EverlostValleyMist.w, 1.0 - exp(-sea));
    half pocketOpacity = min(_EverlostValleyPocket.y, 1.0 - exp(-pocket));
    opacity = 1.0 - (1.0 - seaOpacity) * (1.0 - pocketOpacity);

    // Forward scattering: mist towards a low sun glows warm (sunrise / sunset valleys).
    half sunAmount = pow(saturate(dot(dir, _EverlostMistSun.xyz)), 6.0) * _EverlostMistSun.w;
    color = lerp(color, _EverlostMistSunColor.rgb, saturate(sunAmount));
}

half ValleyMistOpacity(float3 positionWS)
{
    half opacity; half3 color;
    ValleyMistTerms(positionWS, opacity, color);
    return opacity;
}
#endif
