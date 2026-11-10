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

void GetFacingNormals(float3 ViewDirection, RSRayIntersectData Hit,
	out float3 GeometricNormal, out float3 ShadingNormal)
{
	GeometricNormal = CorrectGeometryNormal(ViewDirection, Hit.geometricNormal);
	ShadingNormal = dot(Hit.normal, GeometricNormal) < 0.0f ? -Hit.normal : Hit.normal;
	// A smoothed normal must still face the incident ray.
	if (dot(ShadingNormal, ViewDirection) <= 0.0f)
		ShadingNormal = GeometricNormal;
}

void ContinueRay(inout RSRayData Ray, RSRayIntersectData Hit, float3 Direction)
{
	Ray.direction = normalize(Direction);
	Ray.position = OffsetRayOrigin(Hit.position, Hit.geometricNormal, Ray.direction);
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
	if (ViewHalfCosine <= 0.0f || NormalViewCosine <= 0.0f
		|| NormalLightCosine <= 0.0f || dot(GeometricNormal, Direction) <= 0.0f)
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

float3 RayHitMetalSurface(inout RSRayData Ray, RSRayIntersectData Hit, RSMaterialData Material)
{
	float3 ViewDirection = -Ray.direction;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, Hit, GeometricNormal, ShadingNormal);
	float3 Direction;
	float ViewHalfCosine, SamplingWeight;
	if (!SampleReflection(Ray, ViewDirection, GeometricNormal, ShadingNormal,
		Material.roughness, Direction, ViewHalfCosine, SamplingWeight))
	{
		Ray.energy = 0.0f;
		return Material.emission;
	}

	Ray.energy *= BRDF_F_Schlick(Material.albedo, ViewHalfCosine) * SamplingWeight;
	ContinueRay(Ray, Hit, Direction);
	return Material.emission;
}

float3 RayHitDiffuseSurface(inout RSRayData Ray, RSRayIntersectData Hit, RSMaterialData Material)
{
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(-Ray.direction, Hit, GeometricNormal, ShadingNormal);
	float3 Direction;
	if (!SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction))
	{
		Ray.energy = 0.0f;
		return Material.emission;
	}

	// Lambert f=albedo/pi and cosine PDF=cos(theta)/pi cancel.
	Ray.energy *= Material.albedo;
	ContinueRay(Ray, Hit, Direction);
	return Material.emission;
}

float3 RayHitOpaqueDielectricSurface(inout RSRayData Ray, RSRayIntersectData Hit, RSMaterialData Material)
{
	float3 ViewDirection = -Ray.direction;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, Hit, GeometricNormal, ShadingNormal);
	float ReflectionProbability = FresnelDielectricIOR(dot(ShadingNormal, ViewDirection), Material.IOR);
	float3 Direction;
	if (Random01(Ray.randomState) < ReflectionProbability)
	{
		float ViewHalfCosine, SamplingWeight;
		if (!SampleReflection(Ray, ViewDirection, GeometricNormal, ShadingNormal,
			Material.roughness, Direction, ViewHalfCosine, SamplingWeight))
		{
			Ray.energy = 0.0f;
			return Material.emission;
		}
		float Fresnel = FresnelDielectricIOR(ViewHalfCosine, Material.IOR);
		Ray.energy *= Fresnel * SamplingWeight / ReflectionProbability;
	}
	else
	{
		if (!SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction))
		{
			Ray.energy = 0.0f;
			return Material.emission;
		}
		// The diffuse lobe is weighted by (1 - ReflectionProbability), which
		// cancels its selection probability. This retains the simple coated-diffuse model.
		Ray.energy *= Material.albedo;
	}

	ContinueRay(Ray, Hit, Direction);
	return Material.emission;
}

float3 RayHitTransparencyDielectricSurface(inout RSRayData Ray, RSRayIntersectData Hit, RSMaterialData Material)
{
	float3 ViewDirection = -Ray.direction;
	bool IsEntering = dot(ViewDirection, Hit.geometricNormal) > 0.0f;
	float IOR = IsEntering ? Material.IOR : rcp(Material.IOR);
	float3 Absorption = IsEntering ? 1.0f
		: exp(-distance(Ray.position, Hit.position) * (1.0f - Material.albedo) * 10.0f);
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, Hit, GeometricNormal, ShadingNormal);
	float3 HalfVector = ShadingNormal;
	if (Material.roughness > 0.001f)
		HalfVector = RoughnessNormal(Ray.randomState, ShadingNormal, Material.roughness);
	float ViewHalfCosine = dot(ViewDirection, HalfVector);
	if (ViewHalfCosine <= 0.0f)
	{
		Ray.energy = 0.0f;
		return Absorption * Material.emission;
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
		return Absorption * Material.emission;
	}

	float SamplingWeight = Material.roughness > 0.001f
		? GGXSampleWeight(dot(ShadingNormal, ViewDirection), NormalLightCosine,
			dot(ShadingNormal, HalfVector), ViewHalfCosine, Material.roughness)
		: 1.0f;
	// Fresnel cancels reflection/transmission selection. Radiance transport
	// across refraction additionally scales by eta_incident^2 / eta_transmitted^2.
	if (!IsReflection)
		SamplingWeight /= IOR * IOR;
	Ray.energy *= Absorption * SamplingWeight;
	ContinueRay(Ray, Hit, Direction);
	return Absorption * Material.emission;
}

float3 RayHitSkybox(inout RSRayData Ray, RSRayIntersectData Hit)
{
	Ray.energy = 0.0f;
	return RSSkyboxTexture.SampleLevel(sampler_RSSkyboxTexture, Ray.direction, 0.0f).rgb;
}

float3 RayHitSurface(inout RSRayData Ray, RSRayIntersectData Hit)
{
	RSMaterialData Material = RSSceneMaterialBuffer[Hit.materialID];
	Material.albedo = saturate(Material.albedo);
	Material.roughness = saturate(Material.roughness);
	Material.IOR = max(Material.IOR, 0.001f);

	if (Material.metallic > 0.01f)
		return RayHitMetalSurface(Ray, Hit, Material);
	if (Material.roughness > 0.99f)
		return RayHitDiffuseSurface(Ray, Hit, Material);
	if (Material.transparency > 0.01f)
		return RayHitTransparencyDielectricSurface(Ray, Hit, Material);
	return RayHitOpaqueDielectricSurface(Ray, Hit, Material);
}

#endif // SHADE_INCLUDE