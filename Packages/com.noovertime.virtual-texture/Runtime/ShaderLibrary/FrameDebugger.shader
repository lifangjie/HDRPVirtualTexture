Shader "HDRP/FrameDebugger"
{
    Properties {}
    SubShader
    {
        // This tags allow to use the shader replacement features
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            HLSLINCLUDE
            #pragma target 5.0
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varings
            {
                float4 positionCS : SV_POSITION;
            };

            Varings vert(Attributes attributes)
            {
                Varings varings;
                varings.positionCS = attributes.positionOS;
                return varings;
            }

            float4 frag(Varings varings):SV_Target
            {
                discard;
            }
            ENDHLSL
        }
    }

}