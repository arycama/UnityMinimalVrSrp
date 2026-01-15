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

SamplerState LinearClampSampler;
float IsSceneView;
float2 Resolution;

//#ifdef SHADER_API_VULKAN
//	#ifdef UNITY_COMPILER_DXC
		
//		 #ifdef SHADER_STAGE_FRAGMENT
//            [[vk::input_attachment_index(0)]] SubpassInput<float4> hlslcc_fbinput_0
//        #else
//            //declaring dummy resources here so that non-fragment shader stage automatic bindings wouldn't diverge from the fragment shader (important for vulkan)
//            Texture2D dxc_dummy_fbinput_resource0; static float DXC_DummySubpassVariable0 = float(0).xxxx;
//        #endif
//	#else
//		cbuffer hlslcc_SubpassInput_f_0 
//		{
//			float4 hlslcc_fbinput_0;
//		}
//	#endif
//#else
	#ifdef USE_TEXTURE_ARRAY
		Texture2DArray<float3> _UnityFBInput0;
	#else
		Texture2D<float3> _UnityFBInput0;
	#endif
//#endif

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
	
	float2 position = input.position.xy;
	if (!IsSceneView)
		position.y = Resolution - input.position.y;
	
	//#ifdef SHADER_API_VULKAN
	//	float3 color = hlslcc_fbinput_0.rgb;
	//#else
		#ifdef USE_TEXTURE_ARRAY
			float3 color = _UnityFBInput0[uint3(position.xy, slice)];
		#else
			float3 color = _UnityFBInput0[position.xy];
		#endif
	//#endif
	
	// Simple reinhard tonemap
	float luminance = dot(color, float3(0.2126729, 0.7151522, 0.0721750));
	color *= rcp(1.0 + luminance);
	return float4(color, 1.0);

}