// Voxel dig mesh shading that matches Unity's built-in Standard terrain: the same 4 terrain layers and splat
// weights on top, triplanar on walls, fading to a "dug soil" material below the original surface.
// Painted voxels (vertex colour = terrain layers 0-3, uv0.x = dug soil) override that automatic choice.
Shader "DigHoleIt/DigTerrain"
{
    Properties
    {
        [Header(Terrain Layers (filled by Bake))]
        _Splat0 ("Layer 0", 2D) = "white" {}
        _Splat1 ("Layer 1", 2D) = "white" {}
        _Splat2 ("Layer 2", 2D) = "white" {}
        _Splat3 ("Layer 3", 2D) = "white" {}
        [NoScaleOffset][Normal] _Normal0 ("Normal 0", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal1 ("Normal 1", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal2 ("Normal 2", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal3 ("Normal 3", 2D) = "bump" {}
        _NormalScale0 ("Normal Scale 0", Float) = 1
        _NormalScale1 ("Normal Scale 1", Float) = 1
        _NormalScale2 ("Normal Scale 2", Float) = 1
        _NormalScale3 ("Normal Scale 3", Float) = 1
        [Gamma] _Metallic0 ("Metallic 0", Range(0, 1)) = 0
        [Gamma] _Metallic1 ("Metallic 1", Range(0, 1)) = 0
        [Gamma] _Metallic2 ("Metallic 2", Range(0, 1)) = 0
        [Gamma] _Metallic3 ("Metallic 3", Range(0, 1)) = 0
        _Smoothness0 ("Smoothness 0", Range(0, 1)) = 0
        _Smoothness1 ("Smoothness 1", Range(0, 1)) = 0
        _Smoothness2 ("Smoothness 2", Range(0, 1)) = 0
        _Smoothness3 ("Smoothness 3", Range(0, 1)) = 0
        _Tint0 ("Tint 0", Color) = (1, 1, 1, 1)
        _Tint1 ("Tint 1", Color) = (1, 1, 1, 1)
        _Tint2 ("Tint 2", Color) = (1, 1, 1, 1)
        _Tint3 ("Tint 3", Color) = (1, 1, 1, 1)

        [Header(Dug Soil)]
        _DugTex ("Albedo (tiling = per metre)", 2D) = "white" {}
        [NoScaleOffset][Normal] _DugNormal ("Normal", 2D) = "bump" {}
        _DugNormalScale ("Normal Scale", Float) = 1
        _DugColor ("Tint", Color) = (0.42, 0.31, 0.22, 1)
        _DugSmoothness ("Smoothness", Range(0, 1)) = 0.1
        _DugDepth ("Start Depth (m)", Float) = 0.3
        _DugFade ("Fade Distance (m)", Float) = 0.6
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 16)) = 6
        _HoleMargin ("Hole Overlap (m)", Float) = 0.03

        [Header(Baked Zone Data)]
        [NoScaleOffset] _Control ("Control", 2D) = "red" {}
        [NoScaleOffset] _HeightTex ("Original Height", 2D) = "black" {}
        _ControlST ("Control ST", Vector) = (0, 0, 0, 0)
        _HeightST ("Height ST", Vector) = (0, 0, 0, 0)
        _HeightRange ("Height Range", Vector) = (0, 0, 0, 0)
        _HoleRect ("Hole Rect", Vector) = (-100000, -100000, 100000, 100000)
        _TerrainPos ("Terrain Position", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.5

        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "DigTerrainCommon.cginc"

        sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
        sampler2D _Normal0, _Normal1, _Normal2, _Normal3;
        float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
        half _NormalScale0, _NormalScale1, _NormalScale2, _NormalScale3;
        half _Metallic0, _Metallic1, _Metallic2, _Metallic3;
        half _Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3;
        half4 _Tint0, _Tint1, _Tint2, _Tint3;

        sampler2D _DugTex, _DugNormal;
        float4 _DugTex_ST;
        half _DugNormalScale;
        half4 _DugColor;
        half _DugSmoothness;

        struct Input
        {
            float3 worldPos;
            float3 wN;
            float3 wT;
            float3 wB;
            float4 paint;
            float paintSoil;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            DigBuildFrame(v, o.wN, o.wT, o.wB);
            o.paint = v.color;
            o.paintSoil = v.texcoord.x;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 p = IN.worldPos;
            DigClipOutsideHole(p);

            float3 n = normalize(IN.wN);
            float3 w = DigTriWeights(n);
            float soil;
            float4 lw = DigLayerWeights(DigSplatWeights(p), DigAmount(p, n), IN.paint, IN.paintSoil, soil);

            float4 a0 = DigTriSample(_Splat0, p, _Splat0_ST, w);
            float4 a1 = DigTriSample(_Splat1, p, _Splat1_ST, w);
            float4 a2 = DigTriSample(_Splat2, p, _Splat2_ST, w);
            float4 a3 = DigTriSample(_Splat3, p, _Splat3_ST, w);
            half3 albedo = lw.r * a0.rgb * _Tint0.rgb + lw.g * a1.rgb * _Tint1.rgb
                         + lw.b * a2.rgb * _Tint2.rgb + lw.a * a3.rgb * _Tint3.rgb;
            half smooth = dot(lw, half4(a0.a * _Smoothness0, a1.a * _Smoothness1, a2.a * _Smoothness2, a3.a * _Smoothness3));
            half metal = dot(lw, half4(_Metallic0, _Metallic1, _Metallic2, _Metallic3));

            float3 nrm = lw.r * DigTriNormal(_Normal0, p, _Splat0_ST, n, w, _NormalScale0)
                       + lw.g * DigTriNormal(_Normal1, p, _Splat1_ST, n, w, _NormalScale1)
                       + lw.b * DigTriNormal(_Normal2, p, _Splat2_ST, n, w, _NormalScale2)
                       + lw.a * DigTriNormal(_Normal3, p, _Splat3_ST, n, w, _NormalScale3);

            if (soil > 0.001)
            {
                float4 dugST = float4(_DugTex_ST.xy, 0, 0);
                float4 dugA = float4(tex2D(_DugTex, p.zy * dugST.xy).rgb * w.x
                                   + tex2D(_DugTex, p.xz * dugST.xy).rgb * w.y
                                   + tex2D(_DugTex, p.xy * dugST.xy).rgb * w.z, 1);
                float3 tx = UnpackScaleNormal(tex2D(_DugNormal, p.zy * dugST.xy), _DugNormalScale);
                float3 ty = UnpackScaleNormal(tex2D(_DugNormal, p.xz * dugST.xy), _DugNormalScale);
                float3 tz = UnpackScaleNormal(tex2D(_DugNormal, p.xy * dugST.xy), _DugNormalScale);
                tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
                float3 dugN = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);

                albedo += dugA.rgb * _DugColor.rgb * soil;
                smooth += _DugSmoothness * soil;
                nrm += dugN * soil;
            }

            float3 worldN = normalize(nrm);
            o.Albedo = albedo;
            o.Metallic = metal;
            o.Smoothness = smooth;
            o.Normal = normalize(float3(dot(worldN, normalize(IN.wT)), dot(worldN, normalize(IN.wB)), dot(worldN, n)));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "DigHoleIt/DigTerrain Lite"
}
