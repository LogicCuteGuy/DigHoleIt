// Voxel dig mesh shading that matches Unity's built-in Standard terrain: the terrain's layers (up to 16; Bake sets
// the _DIGLAYERS_* keyword from the terrain's layer count) and splat weights on top, triplanar on walls, fading to a
// "dug soil" material below the original surface. Painted voxels override that automatic choice: vertex colour holds
// the weights of the chunk's 4 paint slots, uv0.x the dug soil weight, uv0.y the slots' terrain layers.
// Mobile GPUs have 16 texture units, so on mobile this shader shades the first 4 layers; use the Lite shader there.
Shader "DigHoleIt/DigTerrain"
{
    Properties
    {
        [Header(Terrain Layers (filled by Bake))]
        _Splat0 ("Layer 0", 2D) = "white" {}
        _Splat1 ("Layer 1", 2D) = "white" {}
        _Splat2 ("Layer 2", 2D) = "white" {}
        _Splat3 ("Layer 3", 2D) = "white" {}
        _Splat4 ("Layer 4", 2D) = "white" {}
        _Splat5 ("Layer 5", 2D) = "white" {}
        _Splat6 ("Layer 6", 2D) = "white" {}
        _Splat7 ("Layer 7", 2D) = "white" {}
        _Splat8 ("Layer 8", 2D) = "white" {}
        _Splat9 ("Layer 9", 2D) = "white" {}
        _Splat10 ("Layer 10", 2D) = "white" {}
        _Splat11 ("Layer 11", 2D) = "white" {}
        _Splat12 ("Layer 12", 2D) = "white" {}
        _Splat13 ("Layer 13", 2D) = "white" {}
        _Splat14 ("Layer 14", 2D) = "white" {}
        _Splat15 ("Layer 15", 2D) = "white" {}
        [NoScaleOffset][Normal] _Normal0 ("Normal 0", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal1 ("Normal 1", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal2 ("Normal 2", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal3 ("Normal 3", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal4 ("Normal 4", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal5 ("Normal 5", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal6 ("Normal 6", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal7 ("Normal 7", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal8 ("Normal 8", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal9 ("Normal 9", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal10 ("Normal 10", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal11 ("Normal 11", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal12 ("Normal 12", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal13 ("Normal 13", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal14 ("Normal 14", 2D) = "bump" {}
        [NoScaleOffset][Normal] _Normal15 ("Normal 15", 2D) = "bump" {}
        _NormalScale0 ("Normal Scale 0", Float) = 1
        _NormalScale1 ("Normal Scale 1", Float) = 1
        _NormalScale2 ("Normal Scale 2", Float) = 1
        _NormalScale3 ("Normal Scale 3", Float) = 1
        _NormalScale4 ("Normal Scale 4", Float) = 1
        _NormalScale5 ("Normal Scale 5", Float) = 1
        _NormalScale6 ("Normal Scale 6", Float) = 1
        _NormalScale7 ("Normal Scale 7", Float) = 1
        _NormalScale8 ("Normal Scale 8", Float) = 1
        _NormalScale9 ("Normal Scale 9", Float) = 1
        _NormalScale10 ("Normal Scale 10", Float) = 1
        _NormalScale11 ("Normal Scale 11", Float) = 1
        _NormalScale12 ("Normal Scale 12", Float) = 1
        _NormalScale13 ("Normal Scale 13", Float) = 1
        _NormalScale14 ("Normal Scale 14", Float) = 1
        _NormalScale15 ("Normal Scale 15", Float) = 1
        [Gamma] _Metallic0 ("Metallic 0", Range(0, 1)) = 0
        [Gamma] _Metallic1 ("Metallic 1", Range(0, 1)) = 0
        [Gamma] _Metallic2 ("Metallic 2", Range(0, 1)) = 0
        [Gamma] _Metallic3 ("Metallic 3", Range(0, 1)) = 0
        [Gamma] _Metallic4 ("Metallic 4", Range(0, 1)) = 0
        [Gamma] _Metallic5 ("Metallic 5", Range(0, 1)) = 0
        [Gamma] _Metallic6 ("Metallic 6", Range(0, 1)) = 0
        [Gamma] _Metallic7 ("Metallic 7", Range(0, 1)) = 0
        [Gamma] _Metallic8 ("Metallic 8", Range(0, 1)) = 0
        [Gamma] _Metallic9 ("Metallic 9", Range(0, 1)) = 0
        [Gamma] _Metallic10 ("Metallic 10", Range(0, 1)) = 0
        [Gamma] _Metallic11 ("Metallic 11", Range(0, 1)) = 0
        [Gamma] _Metallic12 ("Metallic 12", Range(0, 1)) = 0
        [Gamma] _Metallic13 ("Metallic 13", Range(0, 1)) = 0
        [Gamma] _Metallic14 ("Metallic 14", Range(0, 1)) = 0
        [Gamma] _Metallic15 ("Metallic 15", Range(0, 1)) = 0
        _Smoothness0 ("Smoothness 0", Range(0, 1)) = 0
        _Smoothness1 ("Smoothness 1", Range(0, 1)) = 0
        _Smoothness2 ("Smoothness 2", Range(0, 1)) = 0
        _Smoothness3 ("Smoothness 3", Range(0, 1)) = 0
        _Smoothness4 ("Smoothness 4", Range(0, 1)) = 0
        _Smoothness5 ("Smoothness 5", Range(0, 1)) = 0
        _Smoothness6 ("Smoothness 6", Range(0, 1)) = 0
        _Smoothness7 ("Smoothness 7", Range(0, 1)) = 0
        _Smoothness8 ("Smoothness 8", Range(0, 1)) = 0
        _Smoothness9 ("Smoothness 9", Range(0, 1)) = 0
        _Smoothness10 ("Smoothness 10", Range(0, 1)) = 0
        _Smoothness11 ("Smoothness 11", Range(0, 1)) = 0
        _Smoothness12 ("Smoothness 12", Range(0, 1)) = 0
        _Smoothness13 ("Smoothness 13", Range(0, 1)) = 0
        _Smoothness14 ("Smoothness 14", Range(0, 1)) = 0
        _Smoothness15 ("Smoothness 15", Range(0, 1)) = 0
        _Tint0 ("Tint 0", Color) = (1, 1, 1, 1)
        _Tint1 ("Tint 1", Color) = (1, 1, 1, 1)
        _Tint2 ("Tint 2", Color) = (1, 1, 1, 1)
        _Tint3 ("Tint 3", Color) = (1, 1, 1, 1)
        _Tint4 ("Tint 4", Color) = (1, 1, 1, 1)
        _Tint5 ("Tint 5", Color) = (1, 1, 1, 1)
        _Tint6 ("Tint 6", Color) = (1, 1, 1, 1)
        _Tint7 ("Tint 7", Color) = (1, 1, 1, 1)
        _Tint8 ("Tint 8", Color) = (1, 1, 1, 1)
        _Tint9 ("Tint 9", Color) = (1, 1, 1, 1)
        _Tint10 ("Tint 10", Color) = (1, 1, 1, 1)
        _Tint11 ("Tint 11", Color) = (1, 1, 1, 1)
        _Tint12 ("Tint 12", Color) = (1, 1, 1, 1)
        _Tint13 ("Tint 13", Color) = (1, 1, 1, 1)
        _Tint14 ("Tint 14", Color) = (1, 1, 1, 1)
        _Tint15 ("Tint 15", Color) = (1, 1, 1, 1)

        [Header(Dug Soil)]
        _DugTex ("Albedo (tiling = per metre)", 2D) = "white" {}
        [NoScaleOffset][Normal] _DugNormal ("Normal", 2D) = "bump" {}
        _DugNormalScale ("Normal Scale", Float) = 1
        _DugColor ("Tint", Color) = (0.42, 0.31, 0.22, 1)
        _DugSmoothness ("Smoothness", Range(0, 1)) = 0.1
        _DugDepth ("Start Depth (m)", Float) = 0.3
        _DugFade ("Fade Distance (m)", Float) = 0.6
        _DugOcclusion ("Darkening Below Surface", Range(0, 1)) = 0.5
        _DugOcclusionDepth ("Darkening Depth (m)", Float) = 4
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 16)) = 6
        _HoleMargin ("Hole Overlap (m)", Float) = 0.1

        [Header(Baked Zone Data)]
        [NoScaleOffset] _Control ("Control (layers 0-3)", 2D) = "red" {}
        [NoScaleOffset] _Control1 ("Control (layers 4-7)", 2D) = "black" {}
        [NoScaleOffset] _Control2 ("Control (layers 8-11)", 2D) = "black" {}
        [NoScaleOffset] _Control3 ("Control (layers 12-15)", 2D) = "black" {}
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
        #pragma shader_feature_local _ _DIGLAYERS_8 _DIGLAYERS_12 _DIGLAYERS_16

        #if defined(_DIGLAYERS_16)
            #define DIG_LAYERS 16
        #elif defined(_DIGLAYERS_12)
            #define DIG_LAYERS 12
        #elif defined(_DIGLAYERS_8)
            #define DIG_LAYERS 8
        #else
            #define DIG_LAYERS 4
        #endif
        #if defined(SHADER_API_MOBILE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
            #undef DIG_LAYERS
            #define DIG_LAYERS 4
        #endif

        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "DigTerrainCommon.cginc"

        UNITY_DECLARE_TEX2D(_Splat0);
        UNITY_DECLARE_TEX2D(_Normal0);
        float4 _Splat0_ST;
        half _NormalScale0, _Metallic0, _Smoothness0;
        half4 _Tint0;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat1);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal1);
        float4 _Splat1_ST;
        half _NormalScale1, _Metallic1, _Smoothness1;
        half4 _Tint1;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat2);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal2);
        float4 _Splat2_ST;
        half _NormalScale2, _Metallic2, _Smoothness2;
        half4 _Tint2;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat3);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal3);
        float4 _Splat3_ST;
        half _NormalScale3, _Metallic3, _Smoothness3;
        half4 _Tint3;
        #if DIG_LAYERS > 4
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat4);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal4);
        float4 _Splat4_ST;
        half _NormalScale4, _Metallic4, _Smoothness4;
        half4 _Tint4;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat5);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal5);
        float4 _Splat5_ST;
        half _NormalScale5, _Metallic5, _Smoothness5;
        half4 _Tint5;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat6);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal6);
        float4 _Splat6_ST;
        half _NormalScale6, _Metallic6, _Smoothness6;
        half4 _Tint6;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat7);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal7);
        float4 _Splat7_ST;
        half _NormalScale7, _Metallic7, _Smoothness7;
        half4 _Tint7;
        #endif
        #if DIG_LAYERS > 8
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat8);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal8);
        float4 _Splat8_ST;
        half _NormalScale8, _Metallic8, _Smoothness8;
        half4 _Tint8;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat9);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal9);
        float4 _Splat9_ST;
        half _NormalScale9, _Metallic9, _Smoothness9;
        half4 _Tint9;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat10);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal10);
        float4 _Splat10_ST;
        half _NormalScale10, _Metallic10, _Smoothness10;
        half4 _Tint10;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat11);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal11);
        float4 _Splat11_ST;
        half _NormalScale11, _Metallic11, _Smoothness11;
        half4 _Tint11;
        #endif
        #if DIG_LAYERS > 12
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat12);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal12);
        float4 _Splat12_ST;
        half _NormalScale12, _Metallic12, _Smoothness12;
        half4 _Tint12;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat13);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal13);
        float4 _Splat13_ST;
        half _NormalScale13, _Metallic13, _Smoothness13;
        half4 _Tint13;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat14);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal14);
        float4 _Splat14_ST;
        half _NormalScale14, _Metallic14, _Smoothness14;
        half4 _Tint14;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat15);
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Normal15);
        float4 _Splat15_ST;
        half _NormalScale15, _Metallic15, _Smoothness15;
        half4 _Tint15;
        #endif

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
            float4 slots;
            float paintSoil;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            DigBuildFrame(v, o.wN, o.wT, o.wB);
            o.paint = v.color;
            o.paintSoil = v.texcoord.x;
            o.slots = DigSlotLayers(v.texcoord.y);
        }

        // One terrain layer: its weight is the splat weight (scaled by the automatic share) plus its painted weight.
        #define DIG_LAYER(I, SPLAT) \
        { \
            float lw = (SPLAT) * autoSplat + DigPaintWeight(IN.paint, IN.slots, I); \
            UNITY_BRANCH if (lw > 0.002) \
            { \
                float4 a = DigTriSampleGrad(DIG_TEX_ARG(_Splat##I, sampler_Splat0), p, _Splat##I##_ST, w, dpdx, dpdy); \
                total += lw; \
                albedo += lw * a.rgb * _Tint##I.rgb; \
                smooth += lw * a.a * _Smoothness##I; \
                metal += lw * _Metallic##I; \
                nrm += lw * DigTriNormalGrad(DIG_TEX_ARG(_Normal##I, sampler_Normal0), p, _Splat##I##_ST, n, w, _NormalScale##I, dpdx, dpdy); \
            } \
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 p = IN.worldPos;
            DigClipOutsideHole(p);

            float3 n = normalize(IN.wN);
            float3 w = DigTriWeights(n);
            float3 dpdx = ddx(p);
            float3 dpdy = ddy(p);

            float4 c0, c1, c2, c3;
            DigSplatWeights(p, c0, c1, c2, c3);
            float dug = DigAmount(p, n);
            float autoW = saturate(1.0 - dot(IN.paint, 1.0) - IN.paintSoil);
            float autoSplat = (1.0 - dug) * autoW;
            float soil = dug * autoW + IN.paintSoil;

            // Weights of layers this variant lacks (a painted layer 4+ on mobile) are dropped; total rescales the rest.
            float total = 0;
            half3 albedo = 0;
            half smooth = 0;
            half metal = 0;
            float3 nrm = 0;
            DIG_LAYER(0, c0.x)
            DIG_LAYER(1, c0.y)
            DIG_LAYER(2, c0.z)
            DIG_LAYER(3, c0.w)
            #if DIG_LAYERS > 4
            DIG_LAYER(4, c1.x)
            DIG_LAYER(5, c1.y)
            DIG_LAYER(6, c1.z)
            DIG_LAYER(7, c1.w)
            #endif
            #if DIG_LAYERS > 8
            DIG_LAYER(8, c2.x)
            DIG_LAYER(9, c2.y)
            DIG_LAYER(10, c2.z)
            DIG_LAYER(11, c2.w)
            #endif
            #if DIG_LAYERS > 12
            DIG_LAYER(12, c3.x)
            DIG_LAYER(13, c3.y)
            DIG_LAYER(14, c3.z)
            DIG_LAYER(15, c3.w)
            #endif

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

                total += soil;
                albedo += dugA.rgb * _DugColor.rgb * soil;
                smooth += _DugSmoothness * soil;
                nrm += dugN * soil;
            }

            float3 worldN = normalize(nrm + n * 1e-4);
            float rescale = total > 1e-3 ? 1.0 / total : 1.0;
            o.Albedo = albedo * rescale;
            o.Metallic = metal * rescale;
            o.Smoothness = smooth * rescale;
            o.Occlusion = DigOcclusion(p);
            o.Normal = normalize(float3(dot(worldN, normalize(IN.wT)), dot(worldN, normalize(IN.wB)), dot(worldN, n)));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "DigHoleIt/DigTerrain Lite"
}
