Texture2DArray Input;
float RenderMode;
SamplerState LinearClampSampler;

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

FragmentInput Vertex(uint id : SV_VertexID)
{
	FragmentInput output;

	uint localId = id % 3;
	float2 uv = (localId << uint2(1, 0)) & 2;
	
	output.position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	output.uv = uv;
	
	// If using stereo instancing, every 3 vertices makes a triangle for a seperate layer
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = id / 3;
	#endif
	
	return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	float2 uv = input.uv;
	
	float viewIndex = 0;
	if (RenderMode == 2)
		viewIndex = 1;
		
	if (RenderMode == 3)
	{
		if (uv.x < 0.5)
		{
			viewIndex = 0;
			uv.x *= 2;
		}
		else
		{
			viewIndex = 1;
			uv.x = uv.x * 2 - 1;
		}
	}

	return Input.Sample(LinearClampSampler, float3(uv, viewIndex));
}
