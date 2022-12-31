#ifndef SHADE_INCLUDE
#define SHADE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "BRDFInclude.cginc"
#include "ConfigInclude.cginc"
#include "RaytraceInclude.cginc"

StructuredBuffer<RSMaterialData> RSSceneMaterialBuffer;
TextureCube<float4> RSSkyboxTexture; SamplerState sampler_RSSkyboxTexture;

////////////////////////////////////////////////////////////////////////////////////////////////////
// Ray Shade Helper Function |
////////////////////////////////////////////////////////////////////////////////////////////////////

float3 RayHitMetalSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 V = -Ray.direction;
	float3 N = RayIntersectData.normal;

	if (MaterialData.roughness > 0.001f)
		N = RoughnessNormal(Ray.id, N, MaterialData.roughness);

	Ray.energy *= MaterialData.albedo;
	Ray.direction = normalize(reflect(-V, N));
	Ray.position = RayIntersectData.position + N * RAYTRACE_EPSILON;
	return MaterialData.emission;
}

float3 RayHitDiffuseSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 V = -Ray.direction;
	float3 N = RayIntersectData.normal;

	float3 LocalBounceDirection = UniformSampleHemisphere(Ray.id);
	float4 QuaternionLocalToWorld = QuaternionFromTo(float3(0, 0, 1), N);
	float3 WorldBounceDirection = QuaternionRotate(QuaternionLocalToWorld, LocalBounceDirection);

	Ray.energy *= MaterialData.albedo;
	Ray.direction = normalize(WorldBounceDirection);
	Ray.position = RayIntersectData.position + N * RAYTRACE_EPSILON;
	return MaterialData.emission;
}

float3 RayHitDielectricSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 V = -Ray.direction;
	float3 N = RayIntersectData.normal;
	float3 Absorption = MaterialData.albedo;
	float  IOR = MaterialData.IOR;
	if (dot(-Ray.direction, RayIntersectData.normal) > 0.0)
	{
		// In
		if (MaterialData.roughness > 0.001f)
			N = RoughnessNormal(Ray.id, N, MaterialData.roughness);

		Absorption = 1.0;
	}
	else
	{
		// Out
		N = -N;
		if (MaterialData.roughness > 0.001f)
			N = RoughnessNormal(Ray.id, N, MaterialData.roughness);

		Absorption = exp(-distance(Ray.position, RayIntersectData.position) * (1.0 - MaterialData.albedo));

		IOR = rcp(IOR);
	}

	float  ProbabilityOfReflection = FresnelSchlickIOR(dot(N, V), IOR);
	float3 RefractDirection;
	if (Random01(Ray.id) > ProbabilityOfReflection)
		RefractDirection = RefractIOR(V, N, IOR);
	else
		RefractDirection = reflect(-V, N);

	float  RefractNormalSign = dot(N, RefractDirection);
	float3 RefractNormalDirection = normalize(N * RefractNormalSign);

	Ray.energy *= Absorption;
	Ray.direction = normalize(RefractDirection);
	Ray.position = RayIntersectData.position + RefractNormalDirection * RAYTRACE_EPSILON;
	return MaterialData.emission;
}

////////////////////////////////////////////////////////////////////////////////////////////////////
// Ray Shade Function |
////////////////////////////////////////////////////////////////////////////////////////////////////

float3 RayHitSkybox(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	Ray.energy = 0.0f;
	return RSSkyboxTexture.SampleLevel(sampler_RSSkyboxTexture, Ray.direction, 0.0f).xyz;
}

float3 RayHitSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	int MaterialID = RayIntersectData.materialID;
	RSMaterialData MaterialData = RSSceneMaterialBuffer[MaterialID];

	bool IsMetal = MaterialData.metallic > 0.01f;
	bool IsFullRough = MaterialData.roughness > 0.99f;
	if (!IsMetal)
	{
		if (!IsFullRough)
			return RayHitDielectricSurface(Ray, RayIntersectData, MaterialData);
		else
			return RayHitDiffuseSurface(Ray, RayIntersectData, MaterialData);
	}
	else
		return RayHitMetalSurface(Ray, RayIntersectData, MaterialData);

	return 0;
}

#endif // SHADE_INCLUDE