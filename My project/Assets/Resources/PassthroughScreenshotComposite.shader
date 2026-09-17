Shader "Hidden/PassthroughScreenshotComposite"
{
    Properties
    {
        _MainTex ("Virtual Content", 2D) = "black" {}
        _BackgroundTex ("Passthrough Camera", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BackgroundTex);
            SAMPLER(sampler_BackgroundTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 reality = SAMPLE_TEXTURE2D(_BackgroundTex, sampler_BackgroundTex, input.uv);
                half4 virtualContent = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 color = lerp(reality.rgb, virtualContent.rgb, virtualContent.a);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
