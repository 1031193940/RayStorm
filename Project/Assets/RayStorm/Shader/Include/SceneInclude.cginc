#ifndef RAYSTORM_SCENE_INCLUDE
#define RAYSTORM_SCENE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "ConfigInclude.cginc"

StructuredBuffer<RSVertexData>   RSSceneVertexBuffer;
StructuredBuffer<RSGeometryData> RSSceneGeometryBuffer;
StructuredBuffer<RSOctreeData>   RSSceneOctreeBuffer;
StructuredBuffer<int> RSSceneOctreeGeometryIDBuffer;
uint SceneOctreeCount;
uint SceneGeometryCount;

void TestSceneGeometry(RSRayData Ray, int GeometryID, inout int ClosestGeometryID,
	inout float ClosestGeometryDistance, inout float3 ClosestGeometryWeights)
{
	RSGeometryData GeometryData = RSSceneGeometryBuffer[GeometryID];
	float3 FirstPosition = RSSceneVertexBuffer[GeometryData.indice0].position;
	float3 SecondPosition = RSSceneVertexBuffer[GeometryData.indice1].position;
	float3 ThirdPosition = RSSceneVertexBuffer[GeometryData.indice2].position;
	float3 Weights;
	float HitDistance;
	if (RayIntersectWithTriangle(Ray.position, Ray.direction, FirstPosition,
		SecondPosition, ThirdPosition, Weights, HitDistance) && HitDistance < ClosestGeometryDistance)
	{
		ClosestGeometryID = GeometryID;
		ClosestGeometryDistance = HitDistance;
		ClosestGeometryWeights = Weights;
	}
}

bool FindClosestRayHit(RSRayData Ray, out RSRayIntersectData RayIntersectData)
{
	RayIntersectData = (RSRayIntersectData)0;
	int ClosestGeometryID = -1;
	float ClosestGeometryDistance = RAYTRACE_MAX_DISTANCE;
	float3 ClosestGeometryWeights = 0.0f;

	if (SceneOctreeCount == 0 || SceneGeometryCount == 0)
		return false;

	const int kStackCapacity = 64;
	int PendingNodes[kStackCapacity];
	PendingNodes[0] = 0;
	int PendingNodeCount = 1;
	bool RequireGeometryFallback = false;
	uint VisitedNodeCount = 0;
	[loop]
	while (PendingNodeCount > 0 && VisitedNodeCount < SceneOctreeCount)
	{
		int OctreeID = PendingNodes[--PendingNodeCount];
		++VisitedNodeCount;
		RSOctreeData OctreeData = RSSceneOctreeBuffer[OctreeID];
		float EntryDistance, InsideDistance;
		if (!RayIntersectWithAABBBox(Ray.position, Ray.direction, OctreeData.center,
			OctreeData.size, EntryDistance, InsideDistance) || EntryDistance > ClosestGeometryDistance)
			continue;

		for (int GeometryOffset = 0; GeometryOffset < OctreeData.geometryCount; ++GeometryOffset)
		{
			int GeometryID = RSSceneOctreeGeometryIDBuffer[OctreeData.geometryStartID + GeometryOffset];
			TestSceneGeometry(Ray, GeometryID, ClosestGeometryID, ClosestGeometryDistance, ClosestGeometryWeights);
		}

		if (PendingNodeCount + OctreeData.subtreeCount > kStackCapacity)
		{
			RequireGeometryFallback = true;
			break;
		}
		for (int ChildOffset = 0; ChildOffset < OctreeData.subtreeCount; ++ChildOffset)
			PendingNodes[PendingNodeCount++] = OctreeData.subtreeStartID + ChildOffset;
	}

	// Fall back to a triangle scan if traversal cannot finish.
	if (RequireGeometryFallback || PendingNodeCount > 0)
	{
		for (uint GeometryID = 0; GeometryID < SceneGeometryCount; ++GeometryID)
			TestSceneGeometry(Ray, GeometryID, ClosestGeometryID, ClosestGeometryDistance, ClosestGeometryWeights);
	}

	if (ClosestGeometryID == -1)
		return false;

	RSGeometryData ClosestGeometry = RSSceneGeometryBuffer[ClosestGeometryID];
	RSVertexData FirstVertex = RSSceneVertexBuffer[ClosestGeometry.indice0];
	RSVertexData SecondVertex = RSSceneVertexBuffer[ClosestGeometry.indice1];
	RSVertexData ThirdVertex = RSSceneVertexBuffer[ClosestGeometry.indice2];
	RayIntersectData.materialID = ClosestGeometry.materialID;
	RayIntersectData.position = TriangularInterpolate(FirstVertex.position, SecondVertex.position, ThirdVertex.position, ClosestGeometryWeights);
	RayIntersectData.uv0 = TriangularInterpolate(FirstVertex.uv0, SecondVertex.uv0, ThirdVertex.uv0, ClosestGeometryWeights);
	RayIntersectData.uv1 = TriangularInterpolate(FirstVertex.uv1, SecondVertex.uv1, ThirdVertex.uv1, ClosestGeometryWeights);
	RayIntersectData.geometricNormal = normalize(cross(SecondVertex.position - FirstVertex.position,
		ThirdVertex.position - FirstVertex.position));
	float3 ShadingNormal = TriangularInterpolate(FirstVertex.normal, SecondVertex.normal, ThirdVertex.normal, ClosestGeometryWeights);
	RayIntersectData.normal = dot(ShadingNormal, ShadingNormal) > 1.0e-20f ? normalize(ShadingNormal) : RayIntersectData.geometricNormal;
	return true;
}

#endif // RAYSTORM_SCENE_INCLUDE
