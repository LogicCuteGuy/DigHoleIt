// Terrain details (grass, flowers, detail meshes) that a Dig Zone draws over its terrain hole, where the terrain
// hides its own. The zone merges them into one mesh per chunk column. Every vertex knows its instance's root
// (uv3) and the foliage mask texel of the grid column the instance stands on (uv2.xy): when digging or burying
// removes the ground there the mask turns black and the whole instance collapses to its root, so it is not drawn.
// Instances further than the terrain's Detail Distance collapse the same way. uv2.z is the wind sway weight
// (0 at the root, 1 at the top). Wind and its colour follow the terrain's grass (TerrainEngine.cginc,
// TerrainWaveGrass) on positions in terrain space, so the grass in the hole matches the terrain's around it.
Shader "DigHoleIt/DigDetail"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _WaveAndDistance ("Wind Speed, Size, Bending, Detail Distance", Vector) = (0.5, 0.5, 0.5, 80)
        _WavingTint ("Wind Tint", Color) = (0.7, 0.6, 0.5, 0)
        _TerrainPos ("Terrain Position", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _DigFoliageMask ("Foliage Mask (set by DigHoleIt)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert alphatest:_Cutoff addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float4 _WaveAndDistance;
        fixed4 _WavingTint;
        float4 _TerrainPos;
        sampler2D _DigFoliageMask;

        // What the terrain passes its grass shader, measured against its own grass: wave size = Wind Strength * 0.4,
        // and a wave phase of Wind Speed * 0.05 * the time since the scene loaded (the shaders' _Time.y), so the
        // zone's grass waves in step with the terrain's.
        #define DIG_WAVE_SIZE 0.4
        #define DIG_WAVE_RATE 0.05

        // TerrainEngine.cginc FastSinCos: val in 0..1.
        void DigFastSinCos(float4 val, out float4 s, out float4 c)
        {
            val = val * 6.408849 - 3.1415927;
            float4 r5 = val * val;
            float4 r6 = r5 * r5;
            float4 r7 = r6 * r5;
            float4 r8 = r6 * r5;
            float4 r1 = r5 * val;
            float4 r2 = r1 * r5;
            float4 r3 = r2 * r5;
            float4 sin7 = { 1, -0.16161616, 0.0083333, -0.00019841 };
            float4 cos8 = { -0.5, 0.041666666, -0.0013888889, 0.000024801587 };
            s = val + r1 * sin7.y + r2 * sin7.z + r3 * sin7.w;
            c = 1 + r5 * cos8.x + r6 * cos8.y + r7 * cos8.z + r8 * cos8.w;
        }

        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
        };

        void vert(inout appdata_full v)
        {
            float3 root = v.texcoord3.xyz;
            float3 rootWorld = mul(unity_ObjectToWorld, float4(root, 1.0)).xyz;
            float standing = tex2Dlod(_DigFoliageMask, float4(v.texcoord2.xy, 0.0, 0.0)).r;
            float3 toCamera = rootWorld - _WorldSpaceCameraPos;
            if (standing < 0.5 || dot(toCamera, toCamera) > _WaveAndDistance.w * _WaveAndDistance.w)
            {
                v.vertex.xyz = root;
                return;
            }

            // TerrainWaveGrass, with the vertex in terrain space.
            float3 p = mul(unity_ObjectToWorld, v.vertex).xyz - _TerrainPos.xyz;
            float size = _WaveAndDistance.y * DIG_WAVE_SIZE;
            float4 waves = p.x * float4(0.012, 0.02, 0.06, 0.024) * size + p.z * float4(0.006, 0.02, 0.02, 0.05) * size +
                           _Time.y * _WaveAndDistance.x * DIG_WAVE_RATE * float4(1.2, 2.0, 1.6, 4.8);
            float4 s, c;
            DigFastSinCos(frac(waves), s, c);
            s = s * s;
            s = s * s;
            float lighting = dot(s, normalize(float4(1, 1, 0.4, 0.2))) * 0.7;
            s *= v.texcoord2.z * _WaveAndDistance.z;
            float2 move = float2(dot(s, float4(0.024, 0.04, -0.12, 0.096)), dot(s, float4(0.006, 0.02, -0.02, 0.1)));
            v.vertex.xyz -= mul((float3x3)unity_WorldToObject, float3(move.x, 0.0, move.y)) * _WaveAndDistance.z;
            v.color.rgb *= 2.0 * lerp(float3(0.5, 0.5, 0.5), _WavingTint.rgb, lighting);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb * IN.color.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
