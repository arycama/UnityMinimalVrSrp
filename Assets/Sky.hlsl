#ifdef __INTELLISENSE__
    //#define STEREO_INSTANCING_ON
    #define STEREO_MULTIVIEW_ON
#endif

struct VertexInput
{
	uint vertexId : SV_VertexID;
    
    #ifdef STEREO_INSTANCING_ON
        uint instanceId : SV_InstanceID;
    #endif
};

struct FragmentInput
{
    float4 position : SV_POSITION;
	float3 worldDirection : TEXCOORD1;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

#ifdef STEREO_MULTIVIEW_ON
	cbuffer OVR_multiview
	{
		uint gl_ViewID;
		uint numViews_2;
	};
#endif

TextureCube<float3> _Tex;
SamplerState LinearClampSampler;
float4 FrustumCorners[6];

FragmentInput Vertex(VertexInput input)
{
	uint localId = input.vertexId % 3u;
	float2 uv = (localId << uint2(1, 0)) & 2;

	FragmentInput output;
	output.position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	
	uint index = input.vertexId;

	#ifdef STEREO_MULTIVIEW_ON
		index += 3u * gl_ViewID;
	#endif
	
	output.worldDirection = FrustumCorners[input.vertexId].xyz;
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = input.vertexId / 3u;
    #endif

    return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	return float4(_Tex.Sample(LinearClampSampler, input.worldDirection), 1.0);
}