#ifndef RAYSTORM_SCENE_INCLUDE
#define RAYSTORM_SCENE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "ConfigInclude.cginc"

StructuredBuffer<RSVertexData>   RSSceneVertexBuffer;
StructuredBuffer<RSGeometryData> RSSceneGeometryBuffer;
StructuredBuffer<RSBVHData>      RSSceneBVHBuffer;
StructuredBuffer<int> RSSceneBVHGeometryIDBuffer;
uint SceneBVHCount;

void TestSceneGeometry(RSRayData Ray, int GeometryID, inout int ClosestGeometryID, inout float ClosestGeometryDistance, inout float3 ClosestGeometryWeights)
{
	RSGeometryData GeometryData = RSSceneGeometryBuffer[GeometryID];
	float3 FirstPosition = RSSceneVertexBuffer[GeometryData.indice0].position;
	float3 SecondPosition = RSSceneVertexBuffer[GeometryData.indice1].position;
	float3 ThirdPosition = RSSceneVertexBuffer[GeometryData.indice2].position;
	float3 Weights;
	float HitDistance;
	if (RayIntersectWithTriangle(Ray.position, Ray.direction, FirstPosition, SecondPosition, ThirdPosition, Weights, HitDistance) && (HitDistance < ClosestGeometryDistance || (HitDistance == ClosestGeometryDistance && GeometryID < ClosestGeometryID)))
	{
		ClosestGeometryID = GeometryID;
		ClosestGeometryDistance = HitDistance;
		ClosestGeometryWeights = Weights;
	}
}

bool TestShadowGeometry(RSRayData Ray, int GeometryID, float MaximumDistance)
{
	RSGeometryData Geometry = RSSceneGeometryBuffer[GeometryID];
	float3 Weights;
	float Distance;
	return RayIntersectWithTriangle(Ray.position, Ray.direction, RSSceneVertexBuffer[Geometry.indice0].position, RSSceneVertexBuffer[Geometry.indice1].position, RSSceneVertexBuffer[Geometry.indice2].position, Weights, Distance) && Distance < MaximumDistance;
}

bool TestSceneBVHBounds(RSRayData Ray, RSBVHData Node, float MaximumDistance, bool AnyHit, out float EntryDistance)
{
	EntryDistance = 0.0f;
	float ExitDistance = MaximumDistance;
	[unroll]
	for (int Axis = 0; Axis < 3; ++Axis)
	{
		if (abs(Ray.direction[Axis]) < 1.0e-20f)
		{
			if (Ray.position[Axis] < Node.minimum[Axis] || Ray.position[Axis] > Node.maximum[Axis])
				return false;
		}
		else
		{
			float FirstDistance = (Node.minimum[Axis] - Ray.position[Axis]) / Ray.direction[Axis];
			float SecondDistance = (Node.maximum[Axis] - Ray.position[Axis]) / Ray.direction[Axis];
			EntryDistance = max(EntryDistance, min(FirstDistance, SecondDistance));
			ExitDistance = min(ExitDistance, max(FirstDistance, SecondDistance));
			if (ExitDistance < EntryDistance)
				return false;
		}
	}
	return AnyHit ? EntryDistance < MaximumDistance : EntryDistance <= MaximumDistance;
}

int FindSceneBVHHit(RSRayData Ray, float MaximumDistance, bool AnyHit, out float3 ClosestGeometryWeights)
{
	ClosestGeometryWeights = 0.0f;
	int ClosestGeometryID = -1;
	if (SceneBVHCount == 0)
		return -1;

	const int kStackCapacity = 32; // RSBVHUtility limits node depth to 31.
	int PendingNodes[kStackCapacity];
	PendingNodes[0] = 0;
	int PendingNodeCount = 1;
	[loop]
	while (PendingNodeCount > 0)
	{
		RSBVHData Node = RSSceneBVHBuffer[PendingNodes[--PendingNodeCount]];
		float EntryDistance;
		if (!TestSceneBVHBounds(Ray, Node, MaximumDistance, AnyHit, EntryDistance))
			continue;

		for (int GeometryOffset = 0; GeometryOffset < Node.geometryCount; ++GeometryOffset)
		{
			int GeometryID = RSSceneBVHGeometryIDBuffer[Node.startID + GeometryOffset];
			if (AnyHit)
			{
				if (TestShadowGeometry(Ray, GeometryID, MaximumDistance))
					return GeometryID;
			}
			else
				TestSceneGeometry(Ray, GeometryID, ClosestGeometryID, MaximumDistance, ClosestGeometryWeights);
		}

		if (Node.geometryCount == 0)
		{
			int Left = Node.startID;
			int Right = Left + 1;
			float LeftDistance, RightDistance;
			bool HitLeft = TestSceneBVHBounds(Ray, RSSceneBVHBuffer[Left], MaximumDistance, AnyHit, LeftDistance);
			bool HitRight = TestSceneBVHBounds(Ray, RSSceneBVHBuffer[Right], MaximumDistance, AnyHit, RightDistance);
			if (HitLeft && HitRight)
			{
				bool LeftFirst = LeftDistance <= RightDistance;
				PendingNodes[PendingNodeCount++] = LeftFirst ? Right : Left;
				PendingNodes[PendingNodeCount++] = LeftFirst ? Left : Right;
			}
			else if (HitLeft || HitRight)
				PendingNodes[PendingNodeCount++] = HitLeft ? Left : Right;
		}
	}
	return ClosestGeometryID;
}

bool FindClosestRayHit(RSRayData Ray, out RSRayIntersectData RayIntersectData)
{
	RayIntersectData = (RSRayIntersectData)0;
	float3 ClosestGeometryWeights;
	int ClosestGeometryID = FindSceneBVHHit(Ray, RAYTRACE_MAX_DISTANCE, false, ClosestGeometryWeights);
	if (ClosestGeometryID == -1)
		return false;

	RSGeometryData ClosestGeometry = RSSceneGeometryBuffer[ClosestGeometryID];
	RSVertexData FirstVertex = RSSceneVertexBuffer[ClosestGeometry.indice0];
	RSVertexData SecondVertex = RSSceneVertexBuffer[ClosestGeometry.indice1];
	RSVertexData ThirdVertex = RSSceneVertexBuffer[ClosestGeometry.indice2];
	RayIntersectData.materialID = ClosestGeometry.materialID;
	RayIntersectData.geometryID = ClosestGeometryID;
	RayIntersectData.position = TriangularInterpolate(FirstVertex.position, SecondVertex.position, ThirdVertex.position, ClosestGeometryWeights);
	RayIntersectData.uv0 = TriangularInterpolate(FirstVertex.uv0, SecondVertex.uv0, ThirdVertex.uv0, ClosestGeometryWeights);
	RayIntersectData.uv1 = TriangularInterpolate(FirstVertex.uv1, SecondVertex.uv1, ThirdVertex.uv1, ClosestGeometryWeights);
	RayIntersectData.geometricNormal = normalize(cross(SecondVertex.position - FirstVertex.position, ThirdVertex.position - FirstVertex.position));
	float3 ShadingNormal = TriangularInterpolate(FirstVertex.normal, SecondVertex.normal, ThirdVertex.normal, ClosestGeometryWeights);
	RayIntersectData.normal = dot(ShadingNormal, ShadingNormal) > 1.0e-20f ? normalize(ShadingNormal) : RayIntersectData.geometricNormal;
	return true;
}

bool FindShadowRayHit(RSRayData Ray, float MaximumDistance)
{
	float3 Weights;
	return FindSceneBVHHit(Ray, MaximumDistance, true, Weights) >= 0;
}

#endif // RAYSTORM_SCENE_INCLUDE
