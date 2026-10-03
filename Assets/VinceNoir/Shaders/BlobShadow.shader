// Soft contact shadow under a person (multiplies the floor darker). The room's lamps are baked, so people get this
// instead of an expensive realtime shadow from every lamp.
Shader "VinceNoir/BlobShadow"
{
    Properties
    {
        _MainTex ("Shape (alpha)", 2D) = "white" {}
        _Strength ("Strength", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent-60" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend DstColor Zero
        ZWrite Off
        Offset -2, -2
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; half _Strength;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            half4 frag (v2f i) : SV_Target
            {
                half a = tex2D(_MainTex, i.uv).a * _Strength;
                return half4(1 - a, 1 - a, 1 - a, 1);
            }
            ENDCG
        }
    }
}
