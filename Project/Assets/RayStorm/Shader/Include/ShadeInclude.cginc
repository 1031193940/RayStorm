#ifndef SHADE_INCLUDE
#define SHADE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "BRDFInclude.cginc"
#include "ConfigInclude.cginc"
#include "RaytraceInclude.cginc"

StructuredBuffer<RSMaterialData> RSSceneMaterialBuffer;
TextureCube<float4> RSSkyboxTexture;
SamplerState sampler_RSSkyboxTexture;

void GetFacingNormals(float3 ViewDirection, in RSRayIntersectData RayIntersectData,
	out float3 GeometricNormal, out float3 ShadingNormal)
{
	GeometricNormal = CorrectGeometryNormal(ViewDirection, RayIntersectData.geometricNormal);
	ShadingNormal = dot(RayIntersectData.normal, GeometricNormal) < 0.0f ? -RayIntersectData.normal : RayIntersectData.normal;
	if (dot(ShadingNormal, ViewDirection) <= 0.0f)
		ShadingNormal = GeometricNormal;
}

void ContinueRay(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, float3 Direction)
{
	Ray.direction = normalize(Direction);
	Ray.position = OffsetRayOrigin(RayIntersectData.position, RayIntersectData.geometricNormal, Ray.direction);
}

bool SampleReflection(inout RSRayData Ray, float3 ViewDirection, float3 GeometricNormal,
	float3 ShadingNormal, float Roughness, out float3 Direction, out float ViewHalfCosine,
	out float SamplingWeight)
{
	float3 HalfVector = ShadingNormal;
	if (Roughness > 0.001f)
		HalfVector = RoughnessNormal(Ray.randomState, ShadingNormal, Roughness);

	ViewHalfCosine = dot(ViewDirection, HalfVector);
	Direction = reflect(-ViewDirection, HalfVector);
	SamplingWeight = 0.0f;
	float NormalViewCosine = dot(ShadingNormal, ViewDirection);
	float NormalLightCosine = dot(ShadingNormal, Direction);
	if (ViewHalfCosine <= 0.0f || NormalLightCosine <= 0.0f || dot(GeometricNormal, Direction) <= 0.0f)
		return false;

	SamplingWeight = Roughness > 0.001f
		? GGXSampleWeight(NormalViewCosine, NormalLightCosine,
			dot(ShadingNormal, HalfVector), ViewHalfCosine, Roughness)
		: 1.0f;
	return true;
}

bool SampleDiffuse(inout RSRayData Ray, float3 GeometricNormal, float3 ShadingNormal,
	out float3 Direction)
{
	float3 LocalDirection = CosineSampleHemisphere(Ray.randomState);
	float4 LocalToWorld = QuaternionFromTo(float3(0, 0, 1), ShadingNormal);
	Direction = normalize(QuaternionRotate(LocalToWorld, LocalDirection));
	return dot(GeometricNormal, Direction) > 0.0f;
}

float3 RayHitMetalSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 ViewDirection = -Ray.direction;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, RayIntersectData, GeometricNormal, ShadingNormal);
	float3 Direction;
	float ViewHalfCosine, SamplingWeight;
	if (!SampleReflection(Ray, ViewDirection, GeometricNormal, ShadingNormal,
		MaterialData.roughness, Direction, ViewHalfCosine, SamplingWeight))
	{
		Ray.energy = 0.0f;
		return MaterialData.emission;
	}

	Ray.energy *= BRDF_F_Schlick(MaterialData.albedo, ViewHalfCosine) * SamplingWeight;
	ContinueRay(Ray, RayIntersectData, Direction);
	return MaterialData.emission;
}

float3 RayHitDiffuseSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(-Ray.direction, RayIntersectData, GeometricNormal, ShadingNormal);
	float3 Direction;
	if (!SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction))
	{
		Ray.energy = 0.0f;
		return MaterialData.emission;
	}

	Ray.energy *= MaterialData.albedo;
	ContinueRay(Ray, RayIntersectData, Direction);
	return MaterialData.emission;
}

float3 RayHitOpaqueDielectricSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 ViewDirection = -Ray.direction;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, RayIntersectData, GeometricNormal, ShadingNormal);
	float ReflectionProbability = FresnelDielectricIOR(dot(ShadingNormal, ViewDirection), MaterialData.IOR);
	float3 Direction;
	if (Random01(Ray.randomState) < ReflectionProbability)
	{
		float ViewHalfCosine, SamplingWeight;
		if (!SampleReflection(Ray, ViewDirection, GeometricNormal, ShadingNormal,
			MaterialData.roughness, Direction, ViewHalfCosine, SamplingWeight))
		{
			Ray.energy = 0.0f;
			return MaterialData.emission;
		}
		float Fresnel = FresnelDielectricIOR(ViewHalfCosine, MaterialData.IOR);
		Ray.energy *= Fresnel * SamplingWeight / ReflectionProbability;
	}
	else
	{
		if (!SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction))
		{
			Ray.energy = 0.0f;
			return MaterialData.emission;
		}
		Ray.energy *= MaterialData.albedo;
	}

	ContinueRay(Ray, RayIntersectData, Direction);
	return MaterialData.emission;
}

float3 RayHitTransparencyDielectricSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData, in RSMaterialData MaterialData)
{
	float3 ViewDirection = -Ray.direction;
	bool IsEntering = dot(ViewDirection, RayIntersectData.geometricNormal) > 0.0f;
	float IOR = IsEntering ? MaterialData.IOR : rcp(MaterialData.IOR);
	float3 Absorption = IsEntering ? 1.0f
		: exp(-distance(Ray.position, RayIntersectData.position) * (1.0f - MaterialData.albedo) * 10.0f);
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, RayIntersectData, GeometricNormal, ShadingNormal);
	float3 HalfVector = ShadingNormal;
	if (MaterialData.roughness > 0.001f)
		HalfVector = RoughnessNormal(Ray.randomState, ShadingNormal, MaterialData.roughness);
	float ViewHalfCosine = dot(ViewDirection, HalfVector);
	if (ViewHalfCosine <= 0.0f)
	{
		Ray.energy = 0.0f;
		return Absorption * MaterialData.emission;
	}

	float ReflectionProbability = FresnelDielectricIOR(ViewHalfCosine, IOR);
	bool IsReflection = Random01(Ray.randomState) < ReflectionProbability;
	float3 Direction = IsReflection ? reflect(-ViewDirection, HalfVector)
		: RefractIOR(ViewDirection, HalfVector, IOR);
	float NormalLightCosine = dot(ShadingNormal, Direction);
	float GeometricLightCosine = dot(GeometricNormal, Direction);
	bool ValidHemisphere = IsReflection
		? (NormalLightCosine > 0.0f && GeometricLightCosine > 0.0f)
		: (NormalLightCosine < 0.0f && GeometricLightCosine < 0.0f);
	if (!ValidHemisphere)
	{
		Ray.energy = 0.0f;
		return Absorption * MaterialData.emission;
	}

	float SamplingWeight = MaterialData.roughness > 0.001f
		? GGXSampleWeight(dot(ShadingNormal, ViewDirection), NormalLightCosine,
			dot(ShadingNormal, HalfVector), ViewHalfCosine, MaterialData.roughness)
		: 1.0f;
	// Radiance transport: (eta_i / eta_t)^2.
	if (!IsReflection)
		SamplingWeight /= IOR * IOR;
	Ray.energy *= Absorption * SamplingWeight;
	ContinueRay(Ray, RayIntersectData, Direction);
	return Absorption * MaterialData.emission;
}

float3 RayHitSkybox(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	Ray.energy = 0.0f;
	return RSSkyboxTexture.SampleLevel(sampler_RSSkyboxTexture, Ray.direction, 0.0f).rgb;
}

float3 RayHitSurface(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	RSMaterialData MaterialData = RSSceneMaterialBuffer[RayIntersectData.materialID];
	MaterialData.albedo = saturate(MaterialData.albedo);
	MaterialData.roughness = saturate(MaterialData.roughness);
	MaterialData.IOR = max(MaterialData.IOR, 0.001f);

	if (MaterialData.metallic > 0.01f)
		return RayHitMetalSurface(Ray, RayIntersectData, MaterialData);
	if (MaterialData.roughness > 0.99f)
		return RayHitDiffuseSurface(Ray, RayIntersectData, MaterialData);
	if (MaterialData.transparency > 0.01f)
		return RayHitTransparencyDielectricSurface(Ray, RayIntersectData, MaterialData);
	return RayHitOpaqueDielectricSurface(Ray, RayIntersectData, MaterialData);
}

#endif // SHADE_INCLUDE
