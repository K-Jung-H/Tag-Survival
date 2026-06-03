Shader "Unlit/TwirlDistortionMixColorEnvironment"
{
    // This distortion will make dynamic environment twirl around distortion texture
    Properties
    {
        [Header(TEX)]
        [Space(5)]
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionTexture ("Distortion Texture", 2D) = "white" {}
	    _EnvironmentTexture ("Environment Texture", 2D) = "white" {}
        [Header(DISTORTION)]
        [Space(5)]
        _DistortionScale("Distortion Scale",Float)=1
        [Header(SHAPE AND ALPHA)]
        [Space(5)]
        _RadiusFadeAlpha("Radius Fade Alpha",Float)=1
        _Alpha("Alpha",Float)=1
        [Header(DYNAMIC)]
        [Space(5)]
        _SpeedRotate("Speed of Rotate",Float)=-1
        _Twirl("Twirl",vector)=(0,0,0,0)  // x,y are speed. z is strength
        [Header(COLOR)]
        [Space(5)]
        _ColorMixEnvironment("Color Mix with Environment",Color)=(1,1,1,1)
        _Brightness("Brightness of Color",Float)=1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off


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
            sampler2D _DistortionTexture;
            sampler2D _EnvironmentTexture;
            float _DistortionScale;
            float _Radius;
            float _Alpha;
            float _RadiusFadeAlpha;
            // float4 _VectorEnvironmentMove;
            float _SpeedRotate;
            float4 _Twirl;
            float4 _ColorMixEnvironment;
            float _Brightness;

            void Unity_Rotate_Radians_float(float2 UV, float2 Center, float Rotation, out float2 Out)
            {
                UV -= Center;
                float s = sin(Rotation);
                float c = cos(Rotation);
                float2x2 rMatrix = float2x2(c, -s, s, c);
                rMatrix *= 0.5;
                rMatrix += 0.5;
                rMatrix = rMatrix * 2 - 1;
                UV.xy = mul(UV.xy, rMatrix);
                UV += Center;
                Out = UV;
            }
            void Unity_Twirl_float(float2 UV, float2 Center, float Strength, float2 Offset, out float2 Out)
            {
                float2 delta = UV - Center;
                float angle = Strength * length(delta);
                float x = cos(angle) * delta.x - sin(angle) * delta.y;
                float y = sin(angle) * delta.x + cos(angle) * delta.y;
                Out = float2(x + Center.x + Offset.x, y + Center.y + Offset.y);
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
                // make rotate uv
                float2 rotateUV;
                Unity_Rotate_Radians_float(i.uv,float2(0.5,0.5),_Time.y*_SpeedRotate,rotateUV);
                // twirl to increase shape
                float2 twirlOut;
                Unity_Twirl_float(rotateUV, float2(0.5, 0.5), _Twirl.z, _Twirl.xy*_Time.y, twirlOut);
                // distortion
                float2 distortion = tex2D(_DistortionTexture, twirlOut).a;
                // new uv distortion rotate by time
                float2 newUV=i.uv-distortion*_DistortionScale;

                // col environment from uv result
                fixed4 colEnvironment = tex2D(_EnvironmentTexture, newUV)*_ColorMixEnvironment*_Brightness;
                // col mask tex
                float4 mask = tex2D(_MainTex, i.uv);
                //fade mask alpha from center
                float dist = distance(i.uv, float2(0.5,0.5));
                // control fade from 0-1
                float fade = saturate(1.0 - dist / _RadiusFadeAlpha);
                // calculate final alpha
                float alphaFinal=mask.a*fade*_Alpha;

                // result by mask
                fixed4 colResuklt = fixed4(colEnvironment.rgb,alphaFinal);
                return colResuklt;
            }
            ENDCG
        }
    }
}
