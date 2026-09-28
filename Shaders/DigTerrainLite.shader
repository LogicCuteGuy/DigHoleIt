// Quest / mobile variant: Lambert, no normal maps, terrain layers (up to 8) projected on the dominant axis plane,
// walls and dug areas use one triplanar soil texture. Painted voxels override the automatic choice (vertex colour
// = the chunk's 4 paint slot weights, uv0.x = dug soil, uv0.y = the slots' terrain layers). Terrains with more than
// 8 layers shade the first 8 here; mobile GPUs have 16 texture units.
Shader "DigHoleIt/DigTerrain Lite"
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
        _Tint0 ("Tint 0", Color) = (1, 1, 1, 1)
        _Tint1 ("Tint 1", Color) = (1, 1, 1, 1)
        _Tint2 ("Tint 2", Color) = (1, 1, 1, 1)
        _Tint3 ("Tint 3", Color) = (1, 1, 1, 1)
        _Tint4 ("Tint 4", Color) = (1, 1, 1, 1)
        _Tint5 ("Tint 5", Color) = (1, 1, 1, 1)
        _Tint6 ("Tint 6", Color) = (1, 1, 1, 1)
        _Tint7 ("Tint 7", Color) = (1, 1, 1, 1)

        [Header(Dug Soil)]
        _DugTex ("Albedo (tiling = per metre)", 2D) = "white" {}
        _DugColor ("Tint", Color) = (0.42, 0.31, 0.22, 1)
        _DugDepth ("Start Depth (m)", Float) = 0.3
        _DugFade ("Fade Distance (m)", Float) = 0.6
        _DugOcclusion ("Darkening Below Surface", Range(0, 1)) = 0.5
        _DugOcclusionDepth ("Darkening Depth (m)", Float) = 4
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 16)) = 6
        _HoleMargin ("Hole Overlap (m)", Float) = 0.1

        [Header(Baked Zone Data)]
        [NoScaleOffset] _Control ("Control (layers 0-3)", 2D) = "red" {}
        [NoScaleOffset] _Control1 ("Control (layers 4-7)", 2D) = "black" {}
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
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow noforwardadd
        #pragma target 3.5 // GLES3 / Vulkan: room for the paint slot interpolator
        #pragma shader_feature_local _ _DIGLAYERS_8 _DIGLAYERS_12 _DIGLAYERS_16

        #if defined(_DIGLAYERS_8) || defined(_DIGLAYERS_12) || defined(_DIGLAYERS_16)
            #define DIG_LAYERS 8
        #else
            #define DIG_LAYERS 4
        #endif

        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "DigTerrainCommon.cginc"

        UNITY_DECLARE_TEX2D(_Splat0);
        float4 _Splat0_ST;
        fixed4 _Tint0;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat1);
        float4 _Splat1_ST;
        fixed4 _Tint1;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat2);
        float4 _Splat2_ST;
        fixed4 _Tint2;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat3);
        float4 _Splat3_ST;
        fixed4 _Tint3;
        #if DIG_LAYERS > 4
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat4);
        float4 _Splat4_ST;
        fixed4 _Tint4;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat5);
        float4 _Splat5_ST;
        fixed4 _Tint5;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat6);
        float4 _Splat6_ST;
        fixed4 _Tint6;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_Splat7);
        float4 _Splat7_ST;
        fixed4 _Tint7;
        #endif
        sampler2D _DugTex;
        float4 _DugTex_ST;
        fixed4 _DugColor;

        struct Input
        {
            float3 worldPos;
            float3 wN;
            float4 paint;
            float4 slots;
            float paintSoil;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wN = UnityObjectToWorldNormal(v.normal);
            o.paint = v.color;
            o.paintSoil = v.texcoord.x;
            o.slots = DigSlotLayers(v.texcoord.y);
        }

        #define DIG_LITE_LAYER(I, SPLAT) \
        { \
            float lw = (SPLAT) * autoSplat + DigPaintWeight(IN.paint, slots, I); \
            total += lw; \
            UNITY_BRANCH if (lw > 0.002) \
                top += lw * DigDomSampleGrad(DIG_TEX_ARG(_Splat##I, sampler_Splat0), base, bdx, bdy, isTop, _Splat##I##_ST).rgb * _Tint##I.rgb; \
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 p = IN.worldPos;
            DigClipOutsideHole(p);

            float3 n = normalize(IN.wN);
            float3 w = DigTriWeights(n);
            float4 slots = IN.slots;
            float paintSoil = IN.paintSoil;
            float4 c0, c1, c2, c3;
            DigSplatWeights(p, c0, c1, c2, c3);
            float dug = max(DigAmount(p, n), 1.0 - w.y);
            float autoW = saturate(1.0 - dot(IN.paint, 1.0) - paintSoil);
            float autoSplat = (1.0 - dug) * autoW;
            float soilW = dug * autoW + paintSoil;

            // Dominant-axis plane, and its gradients taken from the world position so they stay smooth at plane changes.
            float3 an = abs(n);
            bool isTop = an.y >= an.x && an.y >= an.z;
            bool isX = an.x >= an.z;
            float3 dpdx = ddx(p);
            float3 dpdy = ddy(p);
            float2 base = isTop ? p.xz - _TerrainPos.xz : (isX ? p.zy : p.xy);
            float2 bdx = isTop ? dpdx.xz : (isX ? dpdx.zy : dpdx.xy);
            float2 bdy = isTop ? dpdy.xz : (isX ? dpdy.zy : dpdy.xy);

            // Weights of layers this variant lacks (a painted layer 8+) are dropped; total rescales the rest.
            float total = soilW;
            fixed3 top = 0;
            DIG_LITE_LAYER(0, c0.x)
            DIG_LITE_LAYER(1, c0.y)
            DIG_LITE_LAYER(2, c0.z)
            DIG_LITE_LAYER(3, c0.w)
            #if DIG_LAYERS > 4
            DIG_LITE_LAYER(4, c1.x)
            DIG_LITE_LAYER(5, c1.y)
            DIG_LITE_LAYER(6, c1.z)
            DIG_LITE_LAYER(7, c1.w)
            #endif

            fixed3 soil = (tex2D(_DugTex, p.zy * _DugTex_ST.xy).rgb * w.x
                         + tex2D(_DugTex, p.xz * _DugTex_ST.xy).rgb * w.y
                         + tex2D(_DugTex, p.xy * _DugTex_ST.xy).rgb * w.z) * _DugColor.rgb;

            o.Albedo = (top + soil * soilW) / max(total, 1e-3) * DigOcclusion(p);
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
