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

struct RSLightData
{
	int programID;
	float3 popsition;
	float3 direction;
	float3 color;
	float4 data;
};

struct RSMaterialData
{
	int programID;
	float3 albedo;
	float3 emission;
	float  metallic;
	float  roughness;
	float  IOR;
};

struct RSOctreeData
{
	int parentTreeID;
	int subtreeStartID;
	int subtreeCount;
	int geometryStartID;
	int geometryCount;
	float3 center;
	float3 size;
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
};

struct RSRayIntersectData
{
	int materialID;
	float3 position;
	float2 uv0;
	float2 uv1;
	float3 normal;
};

#endif // RAYSTORM_DATA_INCLUDE