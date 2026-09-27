#ifndef DIGHOLEIT_TERRAIN_COMMON
#define DIGHOLEIT_TERRAIN_COMMON

// DIG_LAYERS (4, 8, 12 or 16) must be defined before this file is included: the number of terrain layers the
// shader variant shades. Bake sets the matching _DIGLAYERS_* keyword from the terrain's layer count.

// Baked per-zone data (written by DigZoneBaker.ApplyMaterial). _Control holds the splat weights of terrain
// layers 0-3, _Control1 of layers 4-7, and so on.
UNITY_DECLARE_TEX2D(_Control);
#if DIG_LAYERS > 4
UNITY_DECLARE_TEX2D_NOSAMPLER(_Control1);
#endif
#if DIG_LAYERS > 8
UNITY_DECLARE_TEX2D_NOSAMPLER(_Control2);
#endif
#if DIG_LAYERS > 12
UNITY_DECLARE_TEX2D_NOSAMPLER(_Control3);
#endif
float4 _ControlST;      // world xz -> control uv
sampler2D _HeightTex;
float4 _HeightST;       // world xz -> height uv
float4 _HeightRange;    // original terrain height min, max
float4 _HoleRect;       // world terrain hole: minX, minZ, maxX, maxZ
float4 _TerrainPos;
float _HoleMargin;

float _DugDepth;
float _DugFade;
float _DugOcclusion;
float _DugOcclusionDepth;
float _TriplanarSharpness;

// Texture parameters that work with and without separate samplers: layer textures share one sampler where the
// platform allows it, so 16 layers stay within the sampler limit.
#if defined(UNITY_SEPARATE_TEXTURE_SAMPLER)
    #define DIG_TEX_PARAM Texture2D tex, SamplerState samp
    #define DIG_TEX_ARG(t, s) t, s
    #define DIG_SAMPLE_GRAD(uv, dx, dy) tex.SampleGrad(samp, uv, dx, dy)
#else
    #define DIG_TEX_PARAM sampler2D tex
    #define DIG_TEX_ARG(t, s) t
    #define DIG_SAMPLE_GRAD(uv, dx, dy) tex2Dgrad(tex, uv, dx, dy)
#endif

// Hides the part of the voxel mesh that lies under intact terrain (outside the hole).
void DigClipOutsideHole(float3 worldPos)
{
    float2 p = worldPos.xz;
    float inside = min(min(p.x - _HoleRect.x, _HoleRect.z - p.x), min(p.y - _HoleRect.y, _HoleRect.w - p.y));
    clip(inside + _HoleMargin);
}

// Splat weights of the terrain layers at a point, normalised to sum to 1 (c0 = layers 0-3, c1 = 4-7, ...).
void DigSplatWeights(float3 worldPos, out float4 c0, out float4 c1, out float4 c2, out float4 c3)
{
    float2 uv = worldPos.xz * _ControlST.xy + _ControlST.zw;
    c0 = UNITY_SAMPLE_TEX2D(_Control, uv);
    c1 = 0;
    c2 = 0;
    c3 = 0;
#if DIG_LAYERS > 4
    c1 = UNITY_SAMPLE_TEX2D_SAMPLER(_Control1, _Control, uv);
#endif
#if DIG_LAYERS > 8
    c2 = UNITY_SAMPLE_TEX2D_SAMPLER(_Control2, _Control, uv);
#endif
#if DIG_LAYERS > 12
    c3 = UNITY_SAMPLE_TEX2D_SAMPLER(_Control3, _Control, uv);
#endif
    float sum = dot(c0, 1.0) + dot(c1, 1.0) + dot(c2, 1.0) + dot(c3, 1.0);
    if (sum > 1e-3)
    {
        float inv = 1.0 / sum;
        c0 *= inv;
        c1 *= inv;
        c2 *= inv;
        c3 *= inv;
    }
    else
    {
        c0 = float4(1, 0, 0, 0);
    }
}

// Terrain layers of the chunk's 4 paint slots, from uv0.y (SurfaceNets.PackSlots). 0 = a mesh built before paint
// slots existed, whose slots are terrain layers 0-3.
float4 DigSlotLayers(float packed)
{
    if (packed < 0.5) return float4(0, 1, 2, 3);
    float p = floor(packed + 0.5) - 1.0;
    float4 l;
    l.x = fmod(p, 16.0); p = floor(p / 16.0);
    l.y = fmod(p, 16.0); p = floor(p / 16.0);
    l.z = fmod(p, 16.0); p = floor(p / 16.0);
    l.w = p;
    return l;
}

// Painted weight of terrain layer i: the slot weights (vertex colour) of the slots holding that layer.
float DigPaintWeight(float4 slotWeights, float4 slotLayers, float i)
{
    return dot(slotWeights, step(abs(slotLayers - i), 0.5));
}

// Metres below the terrain surface as it was baked (negative above it).
float DigDepth(float3 worldPos)
{
    float h01 = tex2D(_HeightTex, worldPos.xz * _HeightST.xy + _HeightST.zw).r;
    return lerp(_HeightRange.x, _HeightRange.y, h01) - worldPos.y;
}

// Ambient darkening of dug areas: 1 at the surface, down to 1 - _DugOcclusion at _DugOcclusionDepth below it. Light
// probes sit above the ground, so without this a cave would get the full surface light.
float DigOcclusion(float3 worldPos)
{
    return 1.0 - _DugOcclusion * saturate(DigDepth(worldPos) / max(_DugOcclusionDepth, 1e-3));
}

// 0 on the original surface, 1 once the point is deeper than _DugDepth + _DugFade below it, or faces downward.
float DigAmount(float3 worldPos, float3 worldNormal)
{
    float depth = DigDepth(worldPos);
    float dug = saturate((depth - _DugDepth) / max(_DugFade, 1e-3));
    return max(dug, saturate(-worldNormal.y * 2.0 - 0.4));
}

float3 DigTriWeights(float3 n)
{
    float3 w = pow(abs(n), _TriplanarSharpness);
    return w / max(w.x + w.y + w.z, 1e-5);
}

// Uv of terrain layer projection on the top plane, identical to Unity's terrain shader.
float2 DigLayerUV(float3 worldPos, float4 st)
{
    return (worldPos.xz - _TerrainPos.xz) * st.xy + st.zw;
}

// Triplanar sample with explicit gradients (dpdx/dpdy = ddx/ddy of the world position), so it can sit inside the
// per-layer branches.
float4 DigTriSampleGrad(DIG_TEX_PARAM, float3 p, float4 st, float3 w, float3 dpdx, float3 dpdy)
{
    float4 x = DIG_SAMPLE_GRAD(p.zy * st.xy, dpdx.zy * st.xy, dpdy.zy * st.xy);
    float4 y = DIG_SAMPLE_GRAD(DigLayerUV(p, st), dpdx.xz * st.xy, dpdy.xz * st.xy);
    float4 z = DIG_SAMPLE_GRAD(p.xy * st.xy, dpdx.xy * st.xy, dpdy.xy * st.xy);
    return x * w.x + y * w.y + z * w.z;
}

// Whiteout-blended triplanar normal map with explicit gradients; returns a world-space normal.
float3 DigTriNormalGrad(DIG_TEX_PARAM, float3 p, float4 st, float3 n, float3 w, float scale, float3 dpdx, float3 dpdy)
{
    float3 tx = UnpackScaleNormal(DIG_SAMPLE_GRAD(p.zy * st.xy, dpdx.zy * st.xy, dpdy.zy * st.xy), scale);
    float3 ty = UnpackScaleNormal(DIG_SAMPLE_GRAD(DigLayerUV(p, st), dpdx.xz * st.xy, dpdy.xz * st.xy), scale);
    float3 tz = UnpackScaleNormal(DIG_SAMPLE_GRAD(p.xy * st.xy, dpdx.xy * st.xy, dpdy.xy * st.xy), scale);
    tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
    return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

// Single-plane sample on the dominant normal axis with explicit gradients (cheap triplanar for the Lite shader).
// base/bdx/bdy: the plane coordinates and their gradients; top: whether the plane is the terrain's top plane.
float4 DigDomSampleGrad(DIG_TEX_PARAM, float2 base, float2 bdx, float2 bdy, bool top, float4 st)
{
    float2 uv = base * st.xy + (top ? st.zw : float2(0, 0));
    return DIG_SAMPLE_GRAD(uv, bdx * st.xy, bdy * st.xy);
}

// Surface shaders need tangents to convert a world normal back to tangent space; the voxel mesh has none,
// so build a stable frame from the normal.
void DigBuildFrame(inout appdata_full v, out float3 wN, out float3 wT, out float3 wB)
{
    float3 n = normalize(v.normal.xyz);
    float3 up = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 t = normalize(cross(n, up));
    v.tangent = float4(t, 1.0);
    wN = UnityObjectToWorldNormal(n);
    wT = UnityObjectToWorldDir(t);
    wB = cross(wN, wT) * unity_WorldTransformParams.w;
}

#endif
