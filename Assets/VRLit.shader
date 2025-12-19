Shader "VR Lit"
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "VRLit.hlsl"
            ENDHLSL
        }
    }
}