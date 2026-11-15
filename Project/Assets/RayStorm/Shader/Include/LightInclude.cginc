#ifndef RAYSTORM_LIGHT_INCLUDE
#define RAYSTORM_LIGHT_INCLUDE

#include "SceneInclude.cginc"
#include "ShadeInclude.cginc"

struct RSLightData
{
	int type;
	int geometryID;
	float area;
	float3 position;
	float3 direction;
	float3 radiance;
	float innerCosine;
	float outerCosine;
};

struct RSAliasData
{
	float probability;
	float threshold;
	int alias;
};

struct RSLightSample
{
	float3 direction;
	float3 radiance;
	float distance;
	float pdf;
	bool isDelta;
};

StructuredBuffer<RSLightData> RSSceneLightBuffer;
StructuredBuffer<RSAliasData> RSSceneLightAliasBuffer;
StructuredBuffer<RSAliasData> RSEnvironmentAliasBuffer;
StructuredBuffer<int> RSSceneGeometryLightIDBuffer;
uint SceneLightCount;
int EnvironmentLightID;
uint EnvironmentWidth;
uint EnvironmentHeight;

float3 EvaluateEnvironment(float3 Direction)
{
	float3 Radiance = RSSkyboxTexture.SampleLevel(sampler_RSSkyboxTexture, Direction, 0.0f).rgb;
	if (RSEnvironmentIsGamma != 0)
		Radiance = float3(EnvironmentGammaToLinear(Radiance.r), EnvironmentGammaToLinear(Radiance.g), EnvironmentGammaToLinear(Radiance.b));
	return Radiance;
}

float3 EnvironmentDirection(float2 UV)
{
	float CosTheta = 1.0f - 2.0f * UV.y;
	float SinTheta = sqrt(max(0.0f, 1.0f - CosTheta * CosTheta));
	float SinPhi, CosPhi;
	sincos(2.0f * PI * UV.x, SinPhi, CosPhi);
	return float3(SinTheta * CosPhi, CosTheta, SinTheta * SinPhi);
}

float GetLightProbability(int LightID)
{
	return RSSceneLightAliasBuffer[LightID].probability;
}

float EvaluateEnvironmentPdf(float3 Direction)
{
	if (EnvironmentLightID < 0)
		return 0.0f;
	float Probability = GetLightProbability(EnvironmentLightID);
	float U = atan2(Direction.z, Direction.x) / (2.0f * PI);
	U = U < 0.0f ? U + 1.0f : U;
	float V = saturate(0.5f * (1.0f - Direction.y));
	uint Column = min((uint)(U * EnvironmentWidth), EnvironmentWidth - 1);
	uint Row = min((uint)(V * EnvironmentHeight), EnvironmentHeight - 1);
	return Probability * RSEnvironmentAliasBuffer[Row * EnvironmentWidth + Column].probability * (EnvironmentWidth * EnvironmentHeight) / (4.0f * PI);
}

float EvaluateLightPdf(float3 PreviousPosition, RSRayIntersectData Hit)
{
	int LightID = RSSceneGeometryLightIDBuffer[Hit.geometryID];
	if (LightID < 0)
		return 0.0f;
	RSLightData Light = RSSceneLightBuffer[LightID];
	float3 Delta = Hit.position - PreviousPosition;
	float DistanceSquared = dot(Delta, Delta);
	float Cosine = abs(dot(Hit.geometricNormal, -normalize(Delta)));
	return Cosine > 0.0f ? GetLightProbability(LightID) * DistanceSquared / (Light.area * Cosine) : 0.0f;
}

bool SampleLight(float3 Position, inout uint State, out RSLightSample Sample)
{
	Sample = (RSLightSample)0;
	if (SceneLightCount == 0)
		return false;
	float Choice = Random01(State) * SceneLightCount;
	int LightID = min((uint)Choice, SceneLightCount - 1);
	RSAliasData Alias = RSSceneLightAliasBuffer[LightID];
	if (frac(Choice) >= Alias.threshold)
		LightID = Alias.alias;
	RSLightData Light = RSSceneLightBuffer[LightID];
	Sample.pdf = GetLightProbability(LightID);
	Sample.radiance = Light.radiance;
	Sample.distance = RAYTRACE_MAX_DISTANCE;
	Sample.isDelta = Light.type < 3;
	if (Light.type == 0)
	{
		Sample.direction = -Light.direction;
		return true;
	}
	if (Light.type == 4)
	{
		uint CellCount = EnvironmentWidth * EnvironmentHeight;
		float CellChoice = Random01(State) * CellCount;
		uint CellID = min((uint)CellChoice, CellCount - 1);
		RSAliasData Cell = RSEnvironmentAliasBuffer[CellID];
		if (frac(CellChoice) >= Cell.threshold)
			CellID = Cell.alias;
		float2 UV = (float2(CellID % EnvironmentWidth, CellID / EnvironmentWidth) + float2(Random01(State), Random01(State))) / float2(EnvironmentWidth, EnvironmentHeight);
		Sample.direction = EnvironmentDirection(UV);
		Sample.radiance = EvaluateEnvironment(Sample.direction);
		Sample.pdf = EvaluateEnvironmentPdf(Sample.direction);
		return Sample.pdf > 0.0f;
	}
	float3 LightPosition = Light.position;
	float3 LightNormal = Light.direction;
	if (Light.type == 3)
	{
		RSGeometryData Geometry = RSSceneGeometryBuffer[Light.geometryID];
		float3 First = RSSceneVertexBuffer[Geometry.indice0].position;
		float3 Second = RSSceneVertexBuffer[Geometry.indice1].position;
		float3 Third = RSSceneVertexBuffer[Geometry.indice2].position;
		float Root = sqrt(Random01(State));
		float Offset = Random01(State);
		LightPosition = (1.0f - Root) * First + Root * (1.0f - Offset) * Second + Root * Offset * Third;
	}
	float3 Delta = LightPosition - Position;
	float DistanceSquared = dot(Delta, Delta);
	if (DistanceSquared <= 0.0f)
		return false;
	Sample.distance = sqrt(DistanceSquared);
	Sample.direction = Delta / Sample.distance;
	if (Light.type == 3)
	{
		float Cosine = abs(dot(LightNormal, -Sample.direction));
		if (Cosine <= 0.0f)
			return false;
		Sample.pdf *= DistanceSquared / (Light.area * Cosine);
		RSMaterialData Material = RSSceneMaterialBuffer[RSSceneGeometryBuffer[Light.geometryID].materialID];
		if (Material.metallic <= 0.01f && Material.roughness <= 0.99f && Material.transparency > 0.01f && dot(-Sample.direction, LightNormal) <= 0.0f)
			Sample.radiance *= exp(-Sample.distance * (1.0f - saturate(Material.albedo)) * 10.0f);
	}
	else
	{
		Sample.radiance /= DistanceSquared;
		if (Light.type == 2)
		{
			float Cosine = dot(Light.direction, -Sample.direction);
			float Falloff = Light.innerCosine > Light.outerCosine ? smoothstep(Light.outerCosine, Light.innerCosine, Cosine) : (Cosine >= Light.outerCosine ? 1.0f : 0.0f);
			Sample.radiance *= Falloff;
		}
	}
	return Sample.pdf > 0.0f;
}

#endif // RAYSTORM_LIGHT_INCLUDE
