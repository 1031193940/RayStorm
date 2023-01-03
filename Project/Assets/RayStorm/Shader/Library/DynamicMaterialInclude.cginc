#ifndef RAYSTORM_DYNAMIC_MATERIAL_INCLUDE
#define RAYSTORM_DYNAMIC_MATERIAL_INCLUDE

#include "../Include/DataInclude.cginc"
#include "../Include/MathInclude.cginc"
#include "../Include/BRDFInclude.cginc"
#include "../Include/RandomInclude.cginc"
#include "../Include/ConfigInclude.cginc"
#include "../Include/RaytraceInclude.cginc"

StructuredBuffer<RSMaterialData> RSSceneMaterialBuffer;

#undef OnMaterialRayIntersectProgram
#undef OnMaterialLightShadeProgram

#define OnMaterialRayIntersectProgram OnMaterialRayIntersectProgram0
#define OnMaterialLightShadeProgram   OnMaterialLightShadeProgram0
#include "../Library/Material/Diffuse" 
#undef  OnMaterialRayIntersectProgram
#undef  OnMaterialLightShadeProgram

#define OnMaterialRayIntersectProgram OnMaterialRayIntersectProgram1
#define OnMaterialLightShadeProgram   OnMaterialLightShadeProgram1
#include "../Library/Material/Mirror" 
#undef  OnMaterialRayIntersectProgram
#undef  OnMaterialLightShadeProgram

#define OnMaterialRayIntersectProgram OnMaterialRayIntersectProgram2
#define OnMaterialLightShadeProgram   OnMaterialLightShadeProgram2
#include "../Library/Material/Normal" 
#undef  OnMaterialRayIntersectProgram
#undef  OnMaterialLightShadeProgram

#define OnMaterialRayIntersectProgram OnMaterialRayIntersectProgram3
#define OnMaterialLightShadeProgram   OnMaterialLightShadeProgram3
#include "../Library/Material/Transparency" 
#undef  OnMaterialRayIntersectProgram
#undef  OnMaterialLightShadeProgram


float3 DynamicMaterialRayIntersect(inout RSRayData Ray, in RSRayIntersectData RayIntersectData) 
{
	RSMaterialData MaterialData = RSSceneMaterialBuffer[RayIntersectData.materialID];

	switch (MaterialData.programID)
	{
		case 0: return OnMaterialRayIntersectProgram0(MaterialData, Ray, RayIntersectData);
		case 1: return OnMaterialRayIntersectProgram1(MaterialData, Ray, RayIntersectData);
		case 2: return OnMaterialRayIntersectProgram2(MaterialData, Ray, RayIntersectData);
		case 3: return OnMaterialRayIntersectProgram3(MaterialData, Ray, RayIntersectData);
		default: break;
	}

	Ray.energy = 0.0f;
	return float3(1, 0, 1);
}

float3 DynamicMaterialLightShade(in RSLightData LightData, in RSRayData RayData, in RSRayIntersectData RayIntersectData, float3 L, float Shadow)
{
	RSMaterialData MaterialData = RSSceneMaterialBuffer[RayIntersectData.materialID];

	switch (MaterialData.programID)
	{
		case 0: return OnMaterialLightShadeProgram0(MaterialData, LightData, RayIntersectData.normal, L, -RayData.direction, Shadow);
		case 1: return OnMaterialLightShadeProgram1(MaterialData, LightData, RayIntersectData.normal, L, -RayData.direction, Shadow);
		case 2: return OnMaterialLightShadeProgram2(MaterialData, LightData, RayIntersectData.normal, L, -RayData.direction, Shadow);
		case 3: return OnMaterialLightShadeProgram3(MaterialData, LightData, RayIntersectData.normal, L, -RayData.direction, Shadow);
		default: break;
	}

	return float3(0, 0, 0);
}

#endif // RAYSTORM_DYNAMIC_MATERIAL_INCLUDE
