#ifndef RAYSTORM_DYNAMIC_OBJECT_INCLUDE
#define RAYSTORM_DYNAMIC_OBJECT_INCLUDE

#include "../Include/SDFInclude.cginc"
#include "../Include/DataInclude.cginc"
#include "../Include/MathInclude.cginc"
#include "../Include/RandomInclude.cginc"
#include "../Include/ConfigInclude.cginc"

StructuredBuffer<RSObjectData> RSSceneObjectBuffer;

#undef OnObjectRayHitProgram

#define OnObjectRayHitProgram OnObjectRayHitProgram0
#include "../Library/Object/Box"
#undef  OnObjectRayHitProgram

#define OnObjectRayHitProgram OnObjectRayHitProgram1
#include "../Library/Object/Sphere"
#undef  OnObjectRayHitProgram

bool DynamicObjectRayHitProgram(in RSObjectData ObjectData, in RSRayData Ray, inout RSRayIntersectData RayIntersectData)
{
	RayIntersectData = (RSRayIntersectData)0;

	switch (ObjectData.programID)
	{
		case 0: return OnObjectRayHitProgram0(ObjectData, Ray, RayIntersectData);
		case 1: return OnObjectRayHitProgram1(ObjectData, Ray, RayIntersectData);
		default: break;
	}

	return false; 
}

bool DynamicObjectRayHit(in RSRayData Ray, inout RSRayIntersectData RayIntersectData)
{
	float ClostestRayHitDistance = RAYTRACE_MAX_DISTANCE;

	int DynamicObjectsCount = RSSceneObjectBuffer.Length;
	for (int ObjectID = 0; ObjectID < DynamicObjectsCount; ++ObjectID)
	{
		RSRayIntersectData ObjectIntersectData;
		if (!DynamicObjectRayHitProgram(RSSceneObjectBuffer[ObjectID], Ray, ObjectIntersectData))
			continue;
		
		float ObjectRayHitDistance = distance(Ray.position, ObjectIntersectData.position);
		if (ObjectRayHitDistance < ClostestRayHitDistance)
		{
			RayIntersectData = ObjectIntersectData;
			ClostestRayHitDistance = ObjectRayHitDistance; 
		}
	}

	return ClostestRayHitDistance < RAYTRACE_MAX_DISTANCE;
}

#endif // RAYSTORM_DYNAMIC_OBJECT_INCLUDE
