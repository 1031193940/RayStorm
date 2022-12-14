#ifndef RAYSTORM_BRDF_INCLUDE
#define RAYSTORM_BRDF_INCLUDE

inline float FresnelSchlickIOR(float NdotV, float IOR)
{
    float F0 = (1.0f - IOR) / (1.0f + IOR);
    F0 *= F0;
    return F0 + (1.0f - F0) * pow(1.0f - NdotV, 5.0f);
}

inline float3 RefractIOR(float3 V, float3 N, float IOR)
{
    float  IORRcp = rcp(IOR);
    float  CosV = dot(N, V);
    float  CosR2 = 1.0f - IORRcp * IORRcp * (1.0f - CosV * CosV);
    float3 RefractDirection = 0.0f;
    if (CosR2 > 0.0f)
    {
        float CosR = sqrt(CosR2);
        RefractDirection = (-N * CosR) + (N * CosV - V) * IORRcp;
    }
    else
        RefractDirection = reflect(-V, N); // Total-Reflection

    return RefractDirection;
}

inline float Lambertine(float3 N, float3 L)
{
    return saturate(dot(N, L));
}

inline float BRDF_D_GGX(float NdotH, float Roughness)
{
	float a = Roughness * Roughness;
	float a2 = a * a;
	float d = (NdotH * a2 - NdotH) * NdotH + 1.0;
	return a2 / (PI * d * d);
}

inline float3 BRDF_F_Schlick(float3 F0, float VdotH)
{
	float f = Pow5(1.0 - VdotH);
	return f + F0 * (1.0 - f);
}

inline float BRDF_V_Smith(float NdotL, float NdotV, float Roughness)
{
	float a2 = Roughness * Roughness;
	float VisSmithV = NdotV + sqrt(NdotV * (NdotV - NdotV * a2) + a2);
	float VisSmithL = NdotL + sqrt(NdotL * (NdotL - NdotL * a2) + a2);
	return rcp(VisSmithV * VisSmithL);
}

inline float3 BRDF(float3 F0, float NdotL, float NdotV, float NdotH, float VdotH, float Roughness)
{
	float  D = BRDF_D_GGX(NdotH, Roughness);
	float  V = BRDF_V_Smith(NdotL, NdotV, Roughness);
	float3 F = BRDF_F_Schlick(F0, VdotH);
	return (D * V) * NdotL * F;
}

#endif // RAYSTORM_BRDF_INCLUDE