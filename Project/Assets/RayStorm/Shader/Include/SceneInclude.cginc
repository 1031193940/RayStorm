#ifndef RAYSTORM_SCENE_INCLUDE
#define RAYSTORM_SCENE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "ConfigInclude.cginc"

#include "../Library/DynamicLightInclude.cginc"
#include "../Library/DynamicMaterialInclude.cginc"

StructuredBuffer<RSVertexData>   RSSceneVertexBuffer;
StructuredBuffer<RSGeometryData> RSSceneGeometryBuffer;
StructuredBuffer<RSOctreeData>   RSSceneOctreeBuffer;
StructuredBuffer<int> RSSceneOctreeGeometryIDBuffer;
TextureCube<float4> RSSkyboxTexture; SamplerState sampler_RSSkyboxTexture;

////////////////////////////////////////////////////////////////////////////////////////////////////
// Material |
////////////////////////////////////////////////////////////////////////////////////////////////////

float3 RayShadeMaterial(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	return DynamicMaterialRayIntersect(Ray, RayIntersectData);
}

float3 RayShadeLight(in RSLightData LightData, in RSRayData RayData, in RSRayIntersectData RayIntersectData, float3 L, float Shadow)
{
	return DynamicMaterialLightShade(LightData, RayData, RayIntersectData, L, Shadow);
}

float3 RayShadeSkybox(inout RSRayData Ray, in RSRayIntersectData RayIntersectData)
{
	Ray.energy = 0.0f;
	return RSSkyboxTexture.SampleLevel(sampler_RSSkyboxTexture, Ray.direction, 0.0f).xyz;
}

////////////////////////////////////////////////////////////////////////////////////////////////////
// Scene |
////////////////////////////////////////////////////////////////////////////////////////////////////

int FindClosestOctreeLeaf(float3 RaySource, float3 RayDirection)
{
	// Is intersect with octree root
	RSOctreeData OctreeRoot = RSSceneOctreeBuffer[0];
	float OctreeRootDistance, OctreeRootTransition;
	if (!RayIntersectWithAABBBox(RaySource, RayDirection, OctreeRoot.center, OctreeRoot.size, OctreeRootDistance, OctreeRootTransition))
		return -1;

	int OctreeID = 0;
	while (OctreeID >= 0)
	{
		RSOctreeData OctreeUnit = RSSceneOctreeBuffer[OctreeID];
		if (OctreeUnit.geometryCount > 0)
			break;

		int ClosestSubtreeID = -1;
		float ClosestSubtreeDistance = RAYTRACE_MAX_DISTANCE;
		for (int SubtreeID = OctreeUnit.subtreeStartID; SubtreeID < OctreeUnit.subtreeStartID + OctreeUnit.subtreeCount; ++SubtreeID)
		{
			RSOctreeData SubtreeData = RSSceneOctreeBuffer[SubtreeID];
			float SubtreeDistance, SubtreeTransition;
			if (!RayIntersectWithAABBBox(RaySource, RayDirection, SubtreeData.center, SubtreeData.size, SubtreeDistance, SubtreeTransition))
				continue;

			if (SubtreeDistance < ClosestSubtreeDistance)
			{
				ClosestSubtreeID = SubtreeID;
				ClosestSubtreeDistance = SubtreeDistance;
			}
		}

		if (ClosestSubtreeID == -1)
		{
			float OctreeDistance, OctreeTransition;
			RayIntersectWithAABBBox(RaySource, RayDirection, OctreeUnit.center, OctreeUnit.size, OctreeDistance, OctreeTransition);
			RaySource += RayDirection * (OctreeDistance + OctreeTransition + RAYTRACE_EPSILON);

			OctreeID = OctreeUnit.parentTreeID;
		}
		else
			OctreeID = ClosestSubtreeID;
	}

	return OctreeID;
}

bool FindClosestRayHit(RSRayData Ray, out RSRayIntersectData RayIntersectData)
{
	int ClosestGeometryID = -1;
	float3 ClosestGeometryWeights = 0.0f;
	float  ClosestGeometryDistance = RAYTRACE_MAX_DISTANCE;

	float3 RaySource = Ray.position;
	float3 RayDirection = Ray.direction;
	while (true)
	{
		int OctreeID = FindClosestOctreeLeaf(RaySource, RayDirection);
		if (OctreeID == -1)
			break;

		RSOctreeData OctreeData = RSSceneOctreeBuffer[OctreeID];
		for (int GeometryIndiceID = OctreeData.geometryStartID; GeometryIndiceID < OctreeData.geometryStartID + OctreeData.geometryCount; ++GeometryIndiceID)
		{
			int GeometryID = RSSceneOctreeGeometryIDBuffer[GeometryIndiceID];
			RSGeometryData GeometryData = RSSceneGeometryBuffer[GeometryID];
			float3 p0 = RSSceneVertexBuffer[GeometryData.indice0].position;
			float3 p1 = RSSceneVertexBuffer[GeometryData.indice1].position;
			float3 p2 = RSSceneVertexBuffer[GeometryData.indice2].position;

			float3 weights = 0.0f;
			if (RayIntersectWithTriangle(Ray.position, Ray.direction, p0, p1, p2, weights))
			{
				float3 RayHitPosition = TriangularInterpolate(p0, p1, p2, weights);
				float  RayHitDistance = distance(Ray.position, RayHitPosition);
				if (RayHitDistance < ClosestGeometryDistance)
				{
					ClosestGeometryID = GeometryID;
					ClosestGeometryDistance = RayHitDistance;
					ClosestGeometryWeights = weights;
				}
			}
		}

		float OctreeDistance, OctreeTransition;
		RayIntersectWithAABBBox(RaySource, RayDirection, OctreeData.center, OctreeData.size, OctreeDistance, OctreeTransition);
		RaySource += RayDirection * (OctreeDistance + OctreeTransition + RAYTRACE_EPSILON);
	}

	if (ClosestGeometryID != -1)
	{
		RSGeometryData GeometryData = RSSceneGeometryBuffer[ClosestGeometryID];
		RSVertexData v0 = RSSceneVertexBuffer[GeometryData.indice0];
		RSVertexData v1 = RSSceneVertexBuffer[GeometryData.indice1];
		RSVertexData v2 = RSSceneVertexBuffer[GeometryData.indice2];

		RayIntersectData.materialID = GeometryData.materialID;
		RayIntersectData.position = TriangularInterpolate(v0.position, v1.position, v2.position, ClosestGeometryWeights);
		RayIntersectData.uv0 = TriangularInterpolate(v0.uv0, v1.uv0, v2.uv0, ClosestGeometryWeights);
		RayIntersectData.uv1 = TriangularInterpolate(v0.uv1, v1.uv1, v2.uv1, ClosestGeometryWeights);
		RayIntersectData.normal = normalize(TriangularInterpolate(v0.normal, v1.normal, v2.normal, ClosestGeometryWeights));
		return true;
	}


	RayIntersectData = (RSRayIntersectData)0;
	return false;
}

#endif // RAYSTORM_SCENE_INCLUDE