// Quest / mobile variant: Lambert, no normal maps, terrain layers projected on the dominant axis plane,
// walls and dug areas use one triplanar soil texture. About 9 texture samples per pixel.
// Painted voxels (vertex colour = terrain layers 0-3, uv0.x = dug soil) override the automatic choice.
Shader "DigHoleIt/DigTerrain Lite"
{
    Properties
    {
        [Header(Terrain Layers (filled by Bake))]
        _Splat0 ("Layer 0", 2D) = "white" {}
        _Splat1 ("Layer 1", 2D) = "white" {}
        _Splat2 ("Layer 2", 2D) = "white" {}
        _Splat3 ("Layer 3", 2D) = "white" {}
        _Tint0 ("Tint 0", Color) = (1, 1, 1, 1)
        _Tint1 ("Tint 1", Color) = (1, 1, 1, 1)
        _Tint2 ("Tint 2", Color) = (1, 1, 1, 1)
        _Tint3 ("Tint 3", Color) = (1, 1, 1, 1)

        [Header(Dug Soil)]
        _DugTex ("Albedo (tiling = per metre)", 2D) = "white" {}
        _DugColor ("Tint", Color) = (0.42, 0.31, 0.22, 1)
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
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow noforwardadd
        #pragma target 3.0

        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "DigTerrainCommon.cginc"

        sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
        float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
        fixed4 _Tint0, _Tint1, _Tint2, _Tint3;
        sampler2D _DugTex;
        float4 _DugTex_ST;
        fixed4 _DugColor;

        struct Input
        {
            float3 worldPos;
            float3 wN;
            float4 paint;
            float paintSoil;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wN = UnityObjectToWorldNormal(v.normal);
            o.paint = v.color;
            o.paintSoil = v.texcoord.x;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 p = IN.worldPos;
            DigClipOutsideHole(p);

            float3 n = normalize(IN.wN);
            float3 w = DigTriWeights(n);
            float dug = max(DigAmount(p, n), 1.0 - w.y);
            float soilW;
            float4 lw = DigLayerWeights(DigSplatWeights(p), dug, IN.paint, IN.paintSoil, soilW);

            fixed3 top = lw.r * tex2D(_Splat0, DigDomUV(p, _Splat0_ST, n)).rgb * _Tint0.rgb
                       + lw.g * tex2D(_Splat1, DigDomUV(p, _Splat1_ST, n)).rgb * _Tint1.rgb
                       + lw.b * tex2D(_Splat2, DigDomUV(p, _Splat2_ST, n)).rgb * _Tint2.rgb
                       + lw.a * tex2D(_Splat3, DigDomUV(p, _Splat3_ST, n)).rgb * _Tint3.rgb;

            fixed3 soil = (tex2D(_DugTex, p.zy * _DugTex_ST.xy).rgb * w.x
                         + tex2D(_DugTex, p.xz * _DugTex_ST.xy).rgb * w.y
                         + tex2D(_DugTex, p.xy * _DugTex_ST.xy).rgb * w.z) * _DugColor.rgb;

            o.Albedo = top + soil * soilW;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
