// 3D text for neon signs. Like the built-in GUI/Text Shader (font alpha * vertex colour) but additive and with a
// normal depth test, so a sign on the street isn't drawn through the walls when you are inside (and vice versa).
Shader "VinceNoir/NeonText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
        _Intensity ("Glow (HDR, feeds the bloom)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; half _Intensity;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex); return o;
            }
            half4 frag (v2f i) : SV_Target { half4 c = i.color; c.a *= tex2D(_MainTex, i.texcoord).a; c.rgb *= _Intensity; return c; }
            ENDCG
        }
    }
}
