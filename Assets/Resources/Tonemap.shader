Shader "Hidden/Tonemap"
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
            #include "Tonemap.hlsl"
            ENDHLSL
        }
    }
}