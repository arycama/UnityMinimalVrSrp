Shader "Sky"
{
    Properties
    {
        _Tex("Texture", Cube) = "" {}
    }

    SubShader
    {
        Cull Off
		ZClip Off
		ZTest Off
		ZWrite Off

        Stencil
        {
            Ref 0
            Comp Equal
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Sky.hlsl"
            ENDHLSL
        }
    }
}