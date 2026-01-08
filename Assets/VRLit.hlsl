#ifdef __INTELLISENSE__
    //#define STEREO_INSTANCING_ON
    #define STEREO_MULTIVIEW_ON
#endif

struct VertexInput
{
    float3 position : POSITION;
	float3 normal : NORMAL;
    
    #ifdef STEREO_INSTANCING_ON
        uint instanceId : SV_InstanceID;
    #endif
};

struct FragmentInput
{
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

matrix unity_ObjectToWorld, unity_MatrixVP;

#if defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
	matrix WorldToClip[2];
#endif

#ifdef STEREO_MULTIVIEW_ON
	cbuffer OVR_multiview
	{
		uint gl_ViewID;
		uint numViews_2;
	};
#endif

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = mul(unity_ObjectToWorld, float4(input.position, 1.0)).xyz;

	FragmentInput output;
	output.normal = input.normal;
	
	#ifdef STEREO_INSTANCING_ON
		matrix worldToClip = WorldToClip[input.instanceId];
		output.viewIndex = input.instanceId;
	#elif defined(STEREO_MULTIVIEW_ON)
		matrix worldToClip = WorldToClip[gl_ViewID];
    #else
		matrix worldToClip = unity_MatrixVP;
    #endif

	output.position = mul(worldToClip, float4(worldPosition, 1.0));
    return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	return float4(input.normal * 0.5 + 0.5, 1.0);
	float3 L = normalize(float3(0.8, -0.6, 0.2));
	float3 albedo = 0.75;
	return float4(saturate(dot(input.normal, L) * albedo), 1.0);
}