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
	float2 uv : TEXCOORD;
	
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

Texture2DArray<float3> CameraTarget;
SamplerState LinearClampSampler;
//float Flip;
float IsSceneView;
Texture2DArray<float3> _UnityFBInput0;

FragmentInput Vertex(VertexInput input)
{
	uint localId = input.vertexId % 3u;
	float2 uv = (localId << uint2(1, 0)) & 2;

	FragmentInput output;
	output.position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	
	if (IsSceneView /*Flip*/)
		uv.y = 1 - uv.y;
		
	output.uv = uv;
	
	uint index = input.vertexId;

	#ifdef STEREO_MULTIVIEW_ON
		index += 3u * gl_ViewID;
	#endif
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = input.vertexId / 3u;
    #endif

    return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	#ifdef STEREO_MULTIVIEW_ON
		uint slice = gl_ViewID;
	#elif defined(STEREO_INSTANCING_ON)
		uint slice = input.viewIndex;
	#else
		uint slice = 0;
	#endif
	
	return float4(_UnityFBInput0[uint3(input.position.xy, slice)], 1.0);
	return float4(CameraTarget.Sample(LinearClampSampler, float3(input.uv, slice)), 1.0);
}