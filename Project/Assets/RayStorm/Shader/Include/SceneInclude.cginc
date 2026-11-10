#ifndef RAYSTORM_SCENE_INCLUDE
#define RAYSTORM_SCENE_INCLUDE

#include "DataInclude.cginc"
#include "MathInclude.cginc"
#include "ConfigInclude.cginc"

StructuredBuffer<RSVertexData> RSSceneVertexBuffer;
StructuredBuffer<RSGeometryData> RSSceneGeometryBuffer;
StructuredBuffer<RSOctreeData> RSSceneOctreeBuffer;
StructuredBuffer<int> RSSceneOctreeGeometryIDBuffer;
uint SceneOctreeCount;
uint SceneGeometryCount;

void TestSceneGeometry(RSRayData ray, int geometryID, inout int closestGeometryID,
    inout float closestDistance, inout float3 closestWeights)
{
    RSGeometryData geometry = RSSceneGeometryBuffer[geometryID];
    float3 firstPosition = RSSceneVertexBuffer[geometry.indice0].position;
    float3 secondPosition = RSSceneVertexBuffer[geometry.indice1].position;
    float3 thirdPosition = RSSceneVertexBuffer[geometry.indice2].position;
    float3 weights;
    float hitDistance;
    if (RayIntersectWithTriangle(ray.position, ray.direction, firstPosition,
        secondPosition, thirdPosition, weights, hitDistance) && hitDistance < closestDistance)
    {
        closestGeometryID = geometryID;
        closestDistance = hitDistance;
        closestWeights = weights;
    }
}

bool FindClosestRayHit(RSRayData ray, out RSRayIntersectData hit)
{
    hit = (RSRayIntersectData)0;
    int closestGeometryID = -1;
    float closestDistance = RAYTRACE_MAX_DISTANCE;
    float3 closestWeights = 0.0f;

    if (SceneOctreeCount == 0 || SceneGeometryCount == 0)
        return false;

    // Depth-first traversal visits each node at most once; no epsilon ray marching.
    // Depth 7 requires at most 1 + 7 * 7 = 50 pending nodes. Overflow safely falls
    // back to a bounded geometry scan instead of dropping geometry or hanging.
    const int stackCapacity = 64;
    int pendingNodes[64];
    pendingNodes[0] = 0;
    int pendingCount = 1;
    bool requireGeometryFallback = false;
    uint visitedCount = 0;
    [loop]
    while (pendingCount > 0 && visitedCount < SceneOctreeCount)
    {
        int nodeID = pendingNodes[--pendingCount];
        ++visitedCount;
        RSOctreeData node = RSSceneOctreeBuffer[nodeID];
        float entryDistance, insideDistance;
        if (!RayIntersectWithAABBBox(ray.position, ray.direction, node.center,
            node.size, entryDistance, insideDistance) || entryDistance > closestDistance)
            continue;

        for (int geometryOffset = 0; geometryOffset < node.geometryCount; ++geometryOffset)
        {
            int geometryID = RSSceneOctreeGeometryIDBuffer[node.geometryStartID + geometryOffset];
            TestSceneGeometry(ray, geometryID, closestGeometryID, closestDistance, closestWeights);
        }

        if (pendingCount + node.subtreeCount > stackCapacity)
        {
            requireGeometryFallback = true;
            break;
        }
        for (int childOffset = 0; childOffset < node.subtreeCount; ++childOffset)
            pendingNodes[pendingCount++] = node.subtreeStartID + childOffset;
    }

    requireGeometryFallback = requireGeometryFallback || pendingCount > 0;
    if (requireGeometryFallback)
    {
        for (uint geometryID = 0; geometryID < SceneGeometryCount; ++geometryID)
            TestSceneGeometry(ray, geometryID, closestGeometryID, closestDistance, closestWeights);
    }

    if (closestGeometryID == -1)
        return false;

    RSGeometryData closestGeometry = RSSceneGeometryBuffer[closestGeometryID];
    RSVertexData firstVertex = RSSceneVertexBuffer[closestGeometry.indice0];
    RSVertexData secondVertex = RSSceneVertexBuffer[closestGeometry.indice1];
    RSVertexData thirdVertex = RSSceneVertexBuffer[closestGeometry.indice2];
    hit.materialID = closestGeometry.materialID;
    hit.position = TriangularInterpolate(firstVertex.position, secondVertex.position, thirdVertex.position, closestWeights);
    hit.uv0 = TriangularInterpolate(firstVertex.uv0, secondVertex.uv0, thirdVertex.uv0, closestWeights);
    hit.uv1 = TriangularInterpolate(firstVertex.uv1, secondVertex.uv1, thirdVertex.uv1, closestWeights);
    hit.geometricNormal = normalize(cross(secondVertex.position - firstVertex.position,
        thirdVertex.position - firstVertex.position));
    float3 shadingNormal = TriangularInterpolate(firstVertex.normal, secondVertex.normal, thirdVertex.normal, closestWeights);
    hit.normal = dot(shadingNormal, shadingNormal) > 1.0e-20f ? normalize(shadingNormal) : hit.geometricNormal;
    return true;
}

#endif // RAYSTORM_SCENE_INCLUDE
