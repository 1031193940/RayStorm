#ifndef RAYSTORM_DATA_INCLUDE
#define RAYSTORM_DATA_INCLUDE

struct RSSourceData
{
	float2 id;
	float3 position;
	float3 direction;
	float3 energy;
};

struct RSVertexData
{
	float3 position;
	float2 uv0;
	float2 uv1;
	float3 normal;
	float4 tangent;
};

struct RSGeometryData
{
	int materialID;
	int indice0;
	int indice1;
	int indice2;
};

struct RSMaterialData
{
	float3 albedo;
	float3 emission;
	float  metallic;
	float  roughness;
	float  transparency;
	float  IOR;
};

struct RSBVHData
{
	float3 minimum;
	int startID;
	float3 maximum;
	int geometryCount;
};

struct RSOutputData
{
	float4 data;
};

struct RSRayData
{
	float2 id;
	float3 position;
	float3 direction;
	float3 energy;
	uint randomState;
};

struct RSRayIntersectData
{
	int materialID;
	int geometryID;
	float3 position;
	float2 uv0;
	float2 uv1;
	float3 normal;
	float3 geometricNormal;
};

#endif // RAYSTORM_DATA_INCLUDE
