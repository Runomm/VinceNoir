// People (Vince and the crew): Standard lighting plus
//  - a cloth pattern (pinstripe, herringbone, twill, check...) on the second UV set, which the Blender export lays out
//    in metres down the torso / legs and along the sleeves, so stripes run the way a tailor cuts them;
//  - cloth sheen: wool and velvet catch the surrounding light at grazing angles (soft bright rim);
//  - skin: a little warm light bleeding past the terminator (cheap subsurface look).
Shader "VinceNoir/Character"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Metallic ("Metallic", Range(0,1)) = 0
        _FabricTex ("Fabric (RGB detail x2, 0.5 grey = neutral; A = smoothness offset)", 2D) = "grey" {}
        _FabricNormal ("Fabric normal", 2D) = "bump" {}
        _FabricAmount ("Fabric amount", Range(0,1)) = 1
        _FabricNormalScale ("Fabric normal strength", Range(0,2)) = 1
        _Sheen ("Cloth sheen", Range(0,2)) = 0.4
        _SheenTint ("Sheen tint", Color) = (1,1,1,1)
        _Wrap ("Skin: warm light past the terminator", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature _GLOSSYREFLECTIONS_OFF
        sampler2D _MainTex, _FabricTex, _FabricNormal;
        fixed4 _Color, _SheenTint;
        half4 _EmissionColor;
        half _Glossiness, _Metallic, _FabricAmount, _FabricNormalScale, _Sheen, _Wrap;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv2_FabricTex;
            float3 viewDir;
            float3 worldNormal; INTERNAL_DATA
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 f = tex2D(_FabricTex, IN.uv2_FabricTex);
            c.rgb *= lerp(fixed3(1,1,1), f.rgb * unity_ColorSpaceDouble.rgb, _FabricAmount);
            o.Normal = UnpackScaleNormal(tex2D(_FabricNormal, IN.uv2_FabricTex), _FabricNormalScale * _FabricAmount);
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = saturate(_Glossiness + (f.a - 0.5) * _FabricAmount);
            float3 wn = WorldNormalVector(IN, o.Normal);
            half nv = saturate(dot(normalize(IN.viewDir), o.Normal));
            half3 amb = max(half3(0,0,0), ShadeSH9(float4(wn, 1)));
            half fres = pow(1 - nv, 3.5);
            half3 sheen = amb * lerp(c.rgb, _SheenTint.rgb, 0.5) * fres * _Sheen * 2.2;
            half3 wrap = amb * c.rgb * half3(1.0, 0.32, 0.22) * _Wrap * 0.45;
            o.Emission = _EmissionColor.rgb + sheen + wrap;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
