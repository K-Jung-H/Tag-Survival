Shader "Custom/TaggerBlindMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _OverlayColor ("Overlay Color", Color) = (0,0,0,0.92)
        _Center ("Center", Vector) = (0.5,0.5,0,0)
        _Radius ("Radius", Float) = 1.5
        _Feather ("Feather", Float) = 0.08
        _NoiseScale ("Noise Scale", Float) = 38
        _NoiseStrength ("Noise Strength", Float) = 0.035
        _NoiseSpeed ("Noise Speed", Float) = 1.4
        _DitherStrength ("Dither Strength", Range(0,1)) = 0.35
        _TimeValue ("Time Value", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TaggerBlindMask"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed4 _OverlayColor;
            float4 _Center;
            float _Radius;
            float _Feather;
            float _NoiseScale;
            float _NoiseStrength;
            float _NoiseSpeed;
            float _DitherStrength;
            float _TimeValue;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Bayer4(float2 pixel)
            {
                int x = (int)fmod(pixel.x, 4);
                int y = (int)fmod(pixel.y, 4);
                int index = x + y * 4;

                float values[16] = {
                    0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                    12.0/16.0, 4.0/16.0, 14.0/16.0,  6.0/16.0,
                    3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                    15.0/16.0, 7.0/16.0, 13.0/16.0,  5.0/16.0
                };

                return values[index];
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 spriteColor = tex2D(_MainTex, uv);

                float2 delta = uv - _Center.xy;
                delta.x *= _ScreenParams.x / max(1.0, _ScreenParams.y);

                float noise = ValueNoise(uv * _NoiseScale + _TimeValue * _NoiseSpeed);
                float distanceValue = length(delta) + (noise - 0.5) * _NoiseStrength;

                float feather = max(0.0001, _Feather);
                float alpha = smoothstep(_Radius, _Radius + feather, distanceValue);

                float edgeBand = 1.0 - saturate(abs(distanceValue - _Radius) / feather);
                float2 screenUv = i.screenPos.xy / max(0.0001, i.screenPos.w);
                float dither = Bayer4(screenUv * _ScreenParams.xy);
                float ditheredAlpha = step(dither, alpha);
                alpha = lerp(alpha, ditheredAlpha, _DitherStrength * edgeBand);

                fixed4 color = _OverlayColor * i.color;
                color.a *= spriteColor.a;
                color.a *= alpha;
                return color;
            }
            ENDCG
        }
    }
}