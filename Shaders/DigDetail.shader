// Terrain details (grass, flowers, detail meshes) that a Dig Zone draws over its terrain hole, where the terrain
// hides its own. The zone merges them into one mesh per chunk column. Every vertex knows its instance's root
// (uv3) and the foliage mask texel of the grid column the instance stands on (uv2.xy): when digging or burying
// removes the ground there the mask turns black and the whole instance collapses to its root, so it is not drawn.
// Instances further than the terrain's Detail Distance collapse the same way. uv2.z is the wind sway weight
// (0 at the root, 1 at the top); the wind follows the terrain's Wind Settings for Grass.
Shader "DigHoleIt/DigDetail"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _WaveAndDistance ("Wind Speed, Size, Bending, Detail Distance", Vector) = (0.5, 0.5, 0.5, 80)
        _WavingTint ("Wind Tint", Color) = (0.7, 0.6, 0.5, 0)
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
        sampler2D _DigFoliageMask;

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

            float sway = v.texcoord2.z;
            float phase = _Time.y * (0.5 + _WaveAndDistance.x * 3.0) + dot(rootWorld.xz, float2(0.35, 0.27)) * (0.5 + _WaveAndDistance.y * 2.0);
            float wave = sin(phase) + 0.5 * sin(phase * 2.3 + 1.7);
            v.vertex.xz += float2(wave, wave * 0.6) * (_WaveAndDistance.z * 0.2 * sway);
            v.color.rgb = lerp(v.color.rgb, v.color.rgb * _WavingTint.rgb * 1.5, saturate(wave * 0.25 + 0.25) * sway * _WaveAndDistance.z);
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
