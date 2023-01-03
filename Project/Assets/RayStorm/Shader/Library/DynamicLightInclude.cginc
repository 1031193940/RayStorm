#ifndef RAYSTORM_DYNAMIC_LIGHT_INCLUDE
#define RAYSTORM_DYNAMIC_LIGHT_INCLUDE

#include "../Include/DataInclude.cginc"
#include "../Include/MathInclude.cginc"
#include "../Include/BRDFInclude.cginc"
#include "../Include/RandomInclude.cginc"
#include "../Include/ConfigInclude.cginc"
#include "../Include/RaytraceInclude.cginc"

StructuredBuffer<RSLightData> RSSceneLightBuffer;

#undef OnImportanceSampleDirection
#undef OnSampleLightIrradiance

#define OnImportanceSampleDirection OnImportanceSampleDirection0
#define OnSampleLightIrradiance OnSampleLightIrradiance0
#include "../Library/Light/Directional"
#undef OnImportanceSampleDirection
#undef OnSampleLightIrradiance


uint GetDynamicLightCount()
{
    return RSSceneLightBuffer.Length;
}

float3 DynamicLightImportanceSampleDirection(uint LightID, float3 Position)
{
    switch (LightID)
    {
        case 0: return OnImportanceSampleDirection0(RSSceneLightBuffer[LightID], Position);
        default: break;
    }

    return 0.0f;
}

float3 DynamicLightSampleIrradiance(uint LightID, float3 Position)
{
    switch (LightID)
    {
        case 0: return OnSampleLightIrradiance0(RSSceneLightBuffer[LightID], Position);
        default: break;
    }

    return 0.0f;
}

#endif // RAYSTORM_DYNAMIC_LIGHT_INCLUDE
