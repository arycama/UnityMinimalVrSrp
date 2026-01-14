Shader "VR Lit"
{
    SubShader
    {
        Pass
        {
            Stencil
            {
                Ref 1
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "VRLit.hlsl"
            ENDHLSL
        }
    }
}