Shader "Hidden/PassthroughLayerCompositor/AlphaComposite"
{
    Properties
    {
        _MainTex ("Passthrough", 2D) = "black" {}
        _OverlayTex ("Overlay", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 passthrough = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 overlay = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, input.uv);
                return lerp(passthrough, overlay, saturate(overlay.a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
