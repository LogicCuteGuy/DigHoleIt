#ifndef DIGHOLEIT_TERRAIN_COMMON
#define DIGHOLEIT_TERRAIN_COMMON

// Baked per-zone data (written by DigZoneBaker.ApplyMaterial).
sampler2D _Control;
float4 _ControlST;      // world xz -> control uv
sampler2D _HeightTex;
float4 _HeightST;       // world xz -> height uv
float4 _HeightRange;    // original terrain height min, max
float4 _HoleRect;       // world terrain hole: minX, minZ, maxX, maxZ
float4 _TerrainPos;
float _HoleMargin;

float _DugDepth;
float _DugFade;
float _TriplanarSharpness;

// Hides the part of the voxel mesh that lies under intact terrain (outside the hole).
void DigClipOutsideHole(float3 worldPos)
{
    float2 p = worldPos.xz;
    float inside = min(min(p.x - _HoleRect.x, _HoleRect.z - p.x), min(p.y - _HoleRect.y, _HoleRect.w - p.y));
    clip(inside + _HoleMargin);
}

float4 DigSplatWeights(float3 worldPos)
{
    float4 c = tex2D(_Control, worldPos.xz * _ControlST.xy + _ControlST.zw);
    float sum = dot(c, 1.0);
    return sum > 1e-3 ? c / sum : float4(1, 0, 0, 0);
}

// 0 on the original surface, 1 once the point is deeper than _DugDepth + _DugFade below it, or faces downward.
float DigAmount(float3 worldPos, float3 worldNormal)
{
    float h01 = tex2D(_HeightTex, worldPos.xz * _HeightST.xy + _HeightST.zw).r;
    float original = lerp(_HeightRange.x, _HeightRange.y, h01);
    float depth = original - worldPos.y;
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

// Single-plane projection picked by the dominant normal axis (cheap triplanar for the Lite shader).
float2 DigDomUV(float3 worldPos, float4 st, float3 n)
{
    float3 a = abs(n);
    if (a.y >= a.x && a.y >= a.z) return DigLayerUV(worldPos, st);
    return (a.x >= a.z ? worldPos.zy : worldPos.xy) * st.xy;
}

// Final layer weights. paint = painted weights of terrain layers 0-3 (vertex colour), paintSoil = painted dug
// soil weight (uv0.x); whatever is left is "auto": the baked splat above the original surface, dug soil below it.
// Returns the four layer weights; soil receives the dug soil weight. They sum to 1.
float4 DigLayerWeights(float4 splat, float dug, float4 paint, float paintSoil, out float soil)
{
    float autoW = saturate(1.0 - dot(paint, 1.0) - paintSoil);
    soil = dug * autoW + paintSoil;
    return splat * ((1.0 - dug) * autoW) + paint;
}

float4 DigTriSample(sampler2D tex, float3 worldPos, float4 st, float3 w)
{
    float4 x = tex2D(tex, worldPos.zy * st.xy);
    float4 y = tex2D(tex, DigLayerUV(worldPos, st));
    float4 z = tex2D(tex, worldPos.xy * st.xy);
    return x * w.x + y * w.y + z * w.z;
}

// Whiteout-blended triplanar normal map; returns a world-space normal.
float3 DigTriNormal(sampler2D tex, float3 worldPos, float4 st, float3 n, float3 w, float scale)
{
    float3 tx = UnpackScaleNormal(tex2D(tex, worldPos.zy * st.xy), scale);
    float3 ty = UnpackScaleNormal(tex2D(tex, DigLayerUV(worldPos, st)), scale);
    float3 tz = UnpackScaleNormal(tex2D(tex, worldPos.xy * st.xy), scale);
    tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
    return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
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
