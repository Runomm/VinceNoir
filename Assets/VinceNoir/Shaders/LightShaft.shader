// Fake volumetric light: an open cone under a lamp (or a beam through a window), additive, bright in the middle of the
// beam and soft at its edges (view-angle fade), fading out toward the floor. Smoky rooms read as smoky.
Shader "VinceNoir/LightShaft"
{
    Properties
    {
        [HDR] _Color ("Color (a = density)", Color) = (1, 0.75, 0.45, 0.08)
        _EdgePower ("Edge softness", Range(0.5, 8)) = 2.5
        _Bottom ("Fade toward the base", Range(0, 1)) = 0.85
        _Drift ("Dust drift", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            half4 _Color; half _EdgePower, _Bottom, _Drift;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; float3 wp : TEXCOORD3; UNITY_FOG_COORDS(4) };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = _WorldSpaceCameraPos - o.wp;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag (v2f i) : SV_Target
            {
                half facing = abs(dot(normalize(i.n), normalize(i.v)));
                half edge = pow(facing, _EdgePower);
                half along = i.uv.y;                                   // 0 at the lamp, 1 at the base
                half fade = smoothstep(0.0, 0.08, along) * (1 - _Bottom * along * along);
                half dust = 1 + _Drift * (sin(i.wp.x * 3.1 + _Time.y * 0.4) * sin(i.wp.z * 2.7 - _Time.y * 0.3) * 0.5 + sin(i.wp.y * 5.0 + _Time.y * 0.6) * 0.25);
                half near = saturate(length(i.v) / 1.5);               // don't flood the screen when the camera is inside the beam
                half3 c = _Color.rgb * _Color.a * edge * fade * dust * near;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                return half4(c, 1);
            }
            ENDCG
        }
    }
}
