Shader "Unlit/TestDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
	    _GrayScaleTex ("Grayscale Texture", 2D) = "white" {}
	    _FinalTexShape ("Final Texture", 2D) = "white" {}
        _DistortionScale("Distortion Scale",Float)=1
        _Speed("Speed Scroll", vector)=(0,0,0,0)
        _TwirlStrength("Twirl Strength",Float)=1
        _Radius("Radius",Float)=1
        _Alpha("Alpha",Float)=1
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque"
            "RenderType"="Transparent" 
        }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        // Cull Off
        // ZWrite Off



        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _GrayScaleTex;
            sampler2D _FinalTexShape;
            float _DistortionScale;
            float4 _Speed;
            float _TwirlStrength;
            float _Radius;
            float _Alpha;

            void Unity_Twirl_float(float2 UV, float2 Center, float Strength, float2 Offset, out float2 Out)
            {
                float2 delta = UV - Center;
                float angle = Strength * length(delta);
                float x = cos(angle) * delta.x - sin(angle) * delta.y;
                float y = sin(angle) * delta.x + cos(angle) * delta.y;
                Out = float2(x + Center.x + Offset.x, y + Center.y + Offset.y);
            }
            float circle (float2 p, float r, float s, float2 center)
            {
                float c = length(p - center);
                return smoothstep(c - s, c + s, r);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_FinalTexShape, i.uv);
                float2 distortion = tex2D(_GrayScaleTex, i.uv).a;
                float2 twirlOut;
                Unity_Twirl_float(i.uv, float2(0.5, 0.5), _TwirlStrength, _Speed.xy*_Time.y, twirlOut);
                float2 newUV=twirlOut-distortion*_DistortionScale;
                fixed4 FinalTexDistortion=tex2D(_FinalTexShape, newUV);
                fixed4 colShape=tex2D(_MainTex, i.uv);
                fixed4 colResuklt = fixed4(FinalTexDistortion.rgb,colShape.a);
                float c = circle(i.uv, 0.35,_Radius,0.5);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, colResuklt);
                return fixed4(colResuklt.rgb,c*_Alpha);
            }
            ENDCG
        }
    }
}
