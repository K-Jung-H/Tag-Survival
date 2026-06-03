Shader "Unlit/BaseDistortionMixColorEnvironment"
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
        [Header(DYNAMIC)]
        [Space(5)]
        _VectorEnvironmentMove("Vector Move of Environment",vector)=(0,0,0,0)
        [Header(SHAPE)]
        [Space(5)]
        _RadiusFadeAlpha("Radius Fade Alpha",Float)=1
        _Alpha("Alpha",Float)=1
        [Header(COLOR)]
        [Space(5)]
        _ColorMixEnvironment("Color Mix with Environment",Color)=(0,0,0,0)
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
            float4 _VectorEnvironmentMove;
            float4 _ColorMixEnvironment;
            float _Brightness;

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
                //first step: distortion by offset environment text by distortion text
                // only take alpha of distortion texture to simply control distortion
                float2 distortion = tex2D(_DistortionTexture, i.uv).a;
                float2 newUV=i.uv-distortion*_DistortionScale-_Time.y*_VectorEnvironmentMove.xy;
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
