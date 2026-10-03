// Camera post effects for the noir look (driven by NoirPostFX.cs):
// bloom (soft-knee threshold, mip pyramid), exposure + filmic tonemap, contrast, split toning (cool shadows / warm
// highlights), saturation that keeps reds (blood, neon, velvet) alive, chromatic fringe, vignette, film grain,
// and the wound effects (desaturation, tunnel vision, blur, red flash).
Shader "Hidden/VinceNoir/PostFX"
{
    Properties { _MainTex ("", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _BloomTex; float4 _BloomTex_TexelSize;
    float4 _Threshold;      // x threshold, y knee, z firefly clamp
    float4 _BloomTint;      // rgb tint, a intensity
    float4 _ShadowTint, _HighlightTint;
    float4 _Grade;          // x exposure, y contrast, z saturation, w red keep
    float4 _Lens;           // x vignette, y grain, z chromatic, w time
    float4 _Wound;          // x desaturate, y tunnel, z blur, w unused
    float4 _Flash;          // rgb colour * a

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    half3 Box4(sampler2D t, float2 uv, float2 texel, float d)
    {
        float4 o = texel.xyxy * float4(-d, -d, d, d);
        return (tex2D(t, uv + o.xy).rgb + tex2D(t, uv + o.zy).rgb + tex2D(t, uv + o.xw).rgb + tex2D(t, uv + o.zw).rgb) * 0.25;
    }
    half3 Tent(sampler2D t, float2 uv, float2 texel)
    {
        float4 d = texel.xyxy * float4(1, 1, -1, 0);
        half3 s = tex2D(t, uv - d.xy).rgb;
        s += tex2D(t, uv - d.wy).rgb * 2;
        s += tex2D(t, uv - d.zy).rgb;
        s += tex2D(t, uv + d.zw).rgb * 2;
        s += tex2D(t, uv).rgb * 4;
        s += tex2D(t, uv + d.xw).rgb * 2;
        s += tex2D(t, uv + d.zy).rgb;
        s += tex2D(t, uv + d.wy).rgb * 2;
        s += tex2D(t, uv + d.xy).rgb;
        return s * (1.0 / 16);
    }
    half3 Prefilter(half3 c)
    {
        c = min(c, _Threshold.zzz);
        half br = max(c.r, max(c.g, c.b));
        half soft = clamp(br - _Threshold.x + _Threshold.y, 0, 2 * _Threshold.y);
        soft = soft * soft / (4 * _Threshold.y + 1e-4);
        return c * (max(soft, br - _Threshold.x) / max(br, 1e-4));
    }
    half3 ACES(half3 x)
    {
        x *= 0.6;
        return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
    }
    float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

    half4 FragPrefilter (v2f i) : SV_Target { return half4(Prefilter(Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, 1)), 1); }
    half4 FragDown (v2f i) : SV_Target { return half4(Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, 1), 1); }
    half4 FragUp (v2f i) : SV_Target { return half4(Tent(_MainTex, i.uv, _MainTex_TexelSize.xy), 1); }

    half4 FragComposite (v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float2 d = uv - 0.5;
        float r2 = dot(d, d);
        // chromatic fringe toward the edges
        float2 ca = d * r2 * _Lens.z * 0.06;
        half3 col;
        col.r = tex2D(_MainTex, uv - ca).r;
        col.g = tex2D(_MainTex, uv).g;
        col.b = tex2D(_MainTex, uv + ca).b;
        // wound blur: the world swims (stronger at the edges)
        if (_Wound.z > 0.001)
        {
            float rad = _Wound.z * (0.004 + 0.02 * r2 * 4);
            half3 acc = col;
            [unroll] for (int k = 0; k < 8; k++)
            {
                float a = k * 0.785398 + _Lens.w * 0.7;
                acc += tex2D(_MainTex, uv + float2(cos(a), sin(a)) * rad * (1 + (k & 1))).rgb;
            }
            col = lerp(col, acc / 9, saturate(_Wound.z * 2));
        }
        half3 bloom = Tent(_BloomTex, uv, _BloomTex_TexelSize.xy);
        col += bloom * _BloomTint.rgb * _BloomTint.a;
        col *= _Grade.x;
        col = ACES(col);
        // grade in a perceptual (gamma) space
        half3 g = pow(max(col, 1e-5), 1.0 / 2.2);
        half lum = dot(g, half3(0.2126, 0.7152, 0.0722));
        g *= lerp(_ShadowTint.rgb, _HighlightTint.rgb, smoothstep(0.08, 0.7, lum));
        half redness = saturate((g.r - max(g.g, g.b)) * 3.0);
        half sat = lerp(_Grade.z, max(_Grade.z, 1.05), redness * _Grade.w);
        sat *= 1 - _Wound.x;
        g = lerp(lum.xxx, g, sat);
        g = saturate((g - 0.5) * _Grade.y + 0.5);
        // vignette + tunnel vision
        half vig = 1 - _Lens.x * smoothstep(0.2, 0.9, length(d * float2(1.25, 1.0)) * 1.25);
        vig *= 1 - _Wound.y * smoothstep(0.08, 0.55, length(d * float2(1.6, 1.0)));
        g *= vig;
        g = lerp(g, _Flash.rgb, _Flash.a * smoothstep(0.1, 0.8, length(d) * 1.4));
        // film grain, a little stronger in the shadows
        half n = Hash(uv * _ScreenParams.xy + frac(_Lens.w * 7.13) * 413.7) - 0.5;
        g += n * _Lens.y * (1.2 - lum);
        col = pow(saturate(g), 2.2);
        return half4(col, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment FragPrefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment FragDown
               ENDCG }
        Pass { Blend One One
               CGPROGRAM
               #pragma vertex vert
               #pragma fragment FragUp
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment FragComposite
               #pragma target 3.0
               ENDCG }
    }
}
