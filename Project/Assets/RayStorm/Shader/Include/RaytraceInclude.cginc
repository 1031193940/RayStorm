#ifndef RAYSTORM_RAYTRACE_INCLUDE
#define RAYSTORM_RAYTRACE_INCLUDE

#include "MathInclude.cginc"
#include "RandomInclude.cginc"

// Make sure the Geometry-Normal is on the same side with View-Direction
inline float3 CorrectGeometryNormal(float3 View, float3 Normal)
{
	if (dot(Normal, View) < 0.0f)
		Normal = -Normal;
	return Normal;
}

// Select a random normal direction from roughness
inline float3 RoughnessNormal(float2 Seed, float3 Normal, float Roughness)
{
	float3 TangentSpaceNormal = ImportanceSampleGGX(Seed, Roughness);
	float4 QuaternionLocalToWorld = QuaternionFromTo(float3(0, 0, 1), Normal);
	return normalize(QuaternionRotate(QuaternionLocalToWorld, TangentSpaceNormal));
}

#endif // RAYSTORM_RAYTRACE_INCLUDE