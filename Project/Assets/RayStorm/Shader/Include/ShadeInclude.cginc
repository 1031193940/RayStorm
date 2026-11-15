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
int RSEnvironmentIsGamma;

void GetFacingNormals(float3 ViewDirection, in RSRayIntersectData RayIntersectData, out float3 GeometricNormal, out float3 ShadingNormal)
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

bool SampleReflection(inout RSRayData Ray, float3 ViewDirection, float3 GeometricNormal, float3 ShadingNormal, float Roughness, out float3 Direction, out float ViewHalfCosine, out float SamplingWeight)
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
		? GGXSampleWeight(NormalViewCosine, NormalLightCosine, dot(ShadingNormal, HalfVector), ViewHalfCosine, Roughness)
		: 1.0f;
	return true;
}

bool SampleDiffuse(inout RSRayData Ray, float3 GeometricNormal, float3 ShadingNormal, out float3 Direction)
{
	float3 LocalDirection = CosineSampleHemisphere(Ray.randomState);
	float4 LocalToWorld = QuaternionFromTo(float3(0, 0, 1), ShadingNormal);
	Direction = normalize(QuaternionRotate(LocalToWorld, LocalDirection));
	return dot(GeometricNormal, Direction) > 0.0f;
}

RSMaterialData GetSurfaceMaterial(RSRayIntersectData RayIntersectData)
{
	RSMaterialData MaterialData = RSSceneMaterialBuffer[RayIntersectData.materialID];
	MaterialData.albedo = saturate(MaterialData.albedo);
	MaterialData.roughness = saturate(MaterialData.roughness);
	MaterialData.IOR = max(MaterialData.IOR, 0.001f);
	return MaterialData;
}

float3 GetSurfaceAbsorption(RSRayData Ray, RSRayIntersectData RayIntersectData, RSMaterialData MaterialData)
{
	if (MaterialData.metallic > 0.01f || MaterialData.roughness > 0.99f || MaterialData.transparency <= 0.01f || dot(-Ray.direction, RayIntersectData.geometricNormal) > 0.0f)
		return 1.0f;
	return exp(-distance(Ray.position, RayIntersectData.position) * (1.0f - MaterialData.albedo) * 10.0f);
}

bool HasNonDeltaBSDF(RSMaterialData MaterialData)
{
	if (MaterialData.metallic > 0.01f)
		return MaterialData.roughness > 0.001f;
	if (MaterialData.roughness > 0.99f || MaterialData.transparency <= 0.01f)
		return true;
	return MaterialData.roughness > 0.001f && MaterialData.IOR != 1.0f;
}

float GetGGXHalfPdf(float NormalHalfCosine, float Roughness)
{
	float Alpha = Roughness * Roughness;
	float AlphaSquared = Alpha * Alpha;
	float HalfCosineSquared = NormalHalfCosine * NormalHalfCosine;
	float Denominator = max(0.0f, 1.0f - HalfCosineSquared) + AlphaSquared * HalfCosineSquared;
	return AlphaSquared * NormalHalfCosine / (PI * Denominator * Denominator);
}

float3 EvaluateBSDF(float3 ViewDirection, float3 Direction, RSRayIntersectData RayIntersectData, RSMaterialData MaterialData, out float Pdf)
{
	Pdf = 0.0f;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, RayIntersectData, GeometricNormal, ShadingNormal);
	float NormalViewCosine = dot(ShadingNormal, ViewDirection);
	float NormalLightCosine = dot(ShadingNormal, Direction);
	float GeometricLightCosine = dot(GeometricNormal, Direction);
	bool IsMetal = MaterialData.metallic > 0.01f;
	bool IsDiffuse = !IsMetal && MaterialData.roughness > 0.99f;
	bool IsTransparent = !IsMetal && !IsDiffuse && MaterialData.transparency > 0.01f;
	bool IsReflection = NormalLightCosine > 0.0f && GeometricLightCosine > 0.0f;
	bool IsTransmission = NormalLightCosine < 0.0f && GeometricLightCosine < 0.0f;
	if (NormalViewCosine <= 0.0f || (!IsReflection && !(IsTransparent && IsTransmission)))
		return 0.0f;
	if (IsDiffuse)
	{
		Pdf = NormalLightCosine / PI;
		return MaterialData.albedo * Pdf;
	}

	float ReflectionProbability = FresnelDielectricIOR(NormalViewCosine, MaterialData.IOR);
	float3 Value = 0.0f;
	if (!IsMetal && !IsTransparent)
	{
		Pdf = (1.0f - ReflectionProbability) * NormalLightCosine / PI;
		Value = MaterialData.albedo * Pdf;
	}
	if (MaterialData.roughness <= 0.001f || (IsTransparent && MaterialData.IOR == 1.0f))
		return Value;

	float IOR = dot(ViewDirection, RayIntersectData.geometricNormal) > 0.0f ? MaterialData.IOR : rcp(MaterialData.IOR);
	float3 HalfVector = normalize(ViewDirection + Direction * (IsTransmission ? IOR : 1.0f));
	if (dot(ShadingNormal, HalfVector) < 0.0f)
		HalfVector = -HalfVector;
	float NormalHalfCosine = saturate(dot(ShadingNormal, HalfVector));
	float ViewHalfCosine = dot(ViewDirection, HalfVector);
	float LightHalfCosine = dot(Direction, HalfVector);
	if (NormalHalfCosine <= 0.0f || ViewHalfCosine <= 0.0f || (IsTransmission && LightHalfCosine >= 0.0f))
		return Value;
	float HalfPdf = GetGGXHalfPdf(NormalHalfCosine, MaterialData.roughness);
	float SamplingWeight = GGXSampleWeight(NormalViewCosine, NormalLightCosine, NormalHalfCosine, ViewHalfCosine, MaterialData.roughness);
	if (IsTransparent)
	{
		ReflectionProbability = FresnelDielectricIOR(ViewHalfCosine, IOR);
		if (IsReflection)
			Pdf = ReflectionProbability * HalfPdf / (4.0f * ViewHalfCosine);
		else
		{
			float Denominator = LightHalfCosine + ViewHalfCosine / IOR;
			Pdf = (1.0f - ReflectionProbability) * HalfPdf * abs(LightHalfCosine) / (Denominator * Denominator);
			SamplingWeight /= IOR * IOR;
		}
		return Pdf * SamplingWeight;
	}

	float ReflectionPdf = HalfPdf / (4.0f * ViewHalfCosine);
	if (IsMetal)
	{
		Pdf = ReflectionPdf;
		return BRDF_F_Schlick(MaterialData.albedo, ViewHalfCosine) * SamplingWeight * Pdf;
	}
	Pdf += ReflectionProbability * ReflectionPdf;
	return Value + FresnelDielectricIOR(ViewHalfCosine, MaterialData.IOR) * SamplingWeight * ReflectionPdf;
}

bool SampleBSDF(inout RSRayData Ray, RSRayIntersectData RayIntersectData, RSMaterialData MaterialData, out float Pdf, out bool IsDelta)
{
	Pdf = 0.0f;
	IsDelta = false;
	float3 ViewDirection = -Ray.direction;
	float3 GeometricNormal, ShadingNormal;
	GetFacingNormals(ViewDirection, RayIntersectData, GeometricNormal, ShadingNormal);
	bool IsMetal = MaterialData.metallic > 0.01f;
	bool IsDiffuse = !IsMetal && MaterialData.roughness > 0.99f;
	bool IsTransparent = !IsMetal && !IsDiffuse && MaterialData.transparency > 0.01f;
	float3 Direction = 0.0f;
	float3 DeltaWeight = 1.0f;
	bool ValidDirection = false;
	if (IsTransparent)
	{
		if (MaterialData.IOR == 1.0f)
		{
			Pdf = 1.0f;
			IsDelta = true;
			ContinueRay(Ray, RayIntersectData, Ray.direction);
			return true;
		}
		float IOR = dot(ViewDirection, RayIntersectData.geometricNormal) > 0.0f ? MaterialData.IOR : rcp(MaterialData.IOR);
		float3 HalfVector = MaterialData.roughness > 0.001f ? RoughnessNormal(Ray.randomState, ShadingNormal, MaterialData.roughness) : ShadingNormal;
		float ViewHalfCosine = dot(ViewDirection, HalfVector);
		float ReflectionProbability = FresnelDielectricIOR(ViewHalfCosine, IOR);
		bool IsReflection = Random01(Ray.randomState) < ReflectionProbability;
		Direction = IsReflection ? reflect(-ViewDirection, HalfVector) : RefractIOR(ViewDirection, HalfVector, IOR);
		float NormalLightCosine = dot(ShadingNormal, Direction);
		float GeometricLightCosine = dot(GeometricNormal, Direction);
		ValidDirection = ViewHalfCosine > 0.0f && (IsReflection ? (NormalLightCosine > 0.0f && GeometricLightCosine > 0.0f) : (NormalLightCosine < 0.0f && GeometricLightCosine < 0.0f));
		IsDelta = MaterialData.roughness <= 0.001f;
		Pdf = IsReflection ? ReflectionProbability : 1.0f - ReflectionProbability;
		DeltaWeight = IsReflection ? 1.0f : rcp(IOR * IOR);
	}
	else if (IsDiffuse)
		ValidDirection = SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction);
	else
	{
		float ReflectionProbability = IsMetal ? 1.0f : FresnelDielectricIOR(dot(ShadingNormal, ViewDirection), MaterialData.IOR);
		if (IsMetal || Random01(Ray.randomState) < ReflectionProbability)
		{
			float ViewHalfCosine, SamplingWeight;
			ValidDirection = SampleReflection(Ray, ViewDirection, GeometricNormal, ShadingNormal, MaterialData.roughness, Direction, ViewHalfCosine, SamplingWeight);
			IsDelta = MaterialData.roughness <= 0.001f;
			Pdf = ReflectionProbability;
			DeltaWeight = IsMetal ? BRDF_F_Schlick(MaterialData.albedo, ViewHalfCosine) : 1.0f;
		}
		else
			ValidDirection = SampleDiffuse(Ray, GeometricNormal, ShadingNormal, Direction);
	}
	if (!ValidDirection)
	{
		Ray.energy = 0.0f;
		Pdf = 0.0f;
		return false;
	}
	if (IsDelta)
		Ray.energy *= DeltaWeight;
	else
	{
		float3 Value = EvaluateBSDF(ViewDirection, Direction, RayIntersectData, MaterialData, Pdf);
		if (Pdf <= 0.0f)
		{
			Ray.energy = 0.0f;
			return false;
		}
		Ray.energy *= Value / Pdf;
	}
	ContinueRay(Ray, RayIntersectData, Direction);
	return true;
}

float EnvironmentGammaToLinear(float Value)
{
	Value = max(Value, 0.0f);
	if (Value <= 0.04045f)
		return Value / 12.92f;
	if (Value < 1.0f)
		return pow((Value + 0.055f) / 1.055f, 2.4f);
	return pow(Value, 2.2f);
}

#endif // SHADE_INCLUDE
