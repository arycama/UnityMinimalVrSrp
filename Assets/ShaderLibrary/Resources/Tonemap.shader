Shader "Hidden/SimpleTonemap"
{
    SubShader
    {
        Cull Off
		ZClip Off
		ZTest Off
		ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ USE_TEXTURE_ARRAY
            #include "Tonemap.hlsl"
            ENDHLSL
        }
    }
}