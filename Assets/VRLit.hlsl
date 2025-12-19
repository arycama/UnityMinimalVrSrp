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
	cbuffer UnityStereoGlobals
	{
		matrix unity_StereoMatrixP[2];
		matrix unity_StereoMatrixV[2];
		matrix unity_StereoMatrixInvV[2];
		matrix unity_StereoMatrixVP[2];

		matrix unity_StereoCameraProjection[2];
		matrix unity_StereoCameraInvProjection[2];
		matrix unity_StereoWorldToCamera[2];
		matrix unity_StereoCameraToWorld[2];

		float3 unity_StereoWorldSpaceCameraPos[2];
		float4 unity_StereoScaleOffset[2];
	};
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
		matrix worldToClip = unity_StereoMatrixVP[input.instanceId];
		output.viewIndex = input.instanceId;
	#elif defined(STEREO_MULTIVIEW_ON)
		matrix worldToClip = unity_StereoMatrixVP[gl_ViewID];
    #else
		matrix worldToClip = unity_MatrixVP;
    #endif

	output.position = mul(worldToClip, float4(worldPosition, 1.0));
    return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	float3 L = normalize(float3(0.8, 0.6, -0.2));
	float3 albedo = 0.75;
	return float4(saturate(dot(input.normal, L) * albedo), 1.0);
}