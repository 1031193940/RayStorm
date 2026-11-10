#ifndef RAYSTORM_RANDOM_INCLUDE
#define RAYSTORM_RANDOM_INCLUDE

#include "MathInclude.cginc"

///////////////////////////////////////////////////////d/////////////////////////
// Variables | Random function variavbles
////////////////////////////////////////////////////////////////////////////////

uint InitializeRandomState(uint RayIndex, uint DispatchSeed)
{
	uint State = RayIndex ^ DispatchSeed ^ 0x9e3779b9u;
	State ^= State >> 16;
	State *= 0x7feb352du;
	State ^= State >> 15;
	State *= 0x846ca68bu;
	return State ^ (State >> 16);
}

////////////////////////////////////////////////////////////////////////////////
// Hash Functions | Basic hash functions
////////////////////////////////////////////////////////////////////////////////

inline float Random01(inout uint State)
{
	State = State * 747796405u + 2891336453u;
	uint Word = ((State >> ((State >> 28u) + 4u)) ^ State) * 277803737u;
	Word = (Word >> 22u) ^ Word;
	return float(Word >> 8u) * (1.0f / 16777216.0f);
}

inline float RandomRange(inout uint State, float randomMin, float randomMax)
{
	return randomMin + Random01(State) * (randomMax - randomMin);
}

inline int RandomRangeInt(inout uint State, int randomMin, int randomMax)
{
	return randomMin + (int)(Random01(State) * (float)(randomMax - randomMin));
}

inline float Dither(float2 UV, float2 Scale)
{
	float4x4 ditherThresholds =
	{
		1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
		13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
		4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
		16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
	};

	float2 ScreenPosition = UV * Scale;
	return ditherThresholds[ScreenPosition.x % 4][ScreenPosition.y % 4];
}

inline float RadicalInverseVDC(uint bits)
{
	bits = (bits << 16u) | (bits >> 16u);
	bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
	bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
	bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
	bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
	return float(bits) * 2.3283064365386963e-10;
}

inline float2 Hammersley(uint i, uint N)
{
	return float2(float(i) / float(N), RadicalInverseVDC(i));
}

////////////////////////////////////////////////////////////////////////////////
// Spherical Direction | Random spherical direction functions
////////////////////////////////////////////////////////////////////////////////

inline float3 UniformSampleSpherical(inout uint Seed)
{
	float z = Random01(Seed) * 2.0f - 1.0f;
	float r = sqrt(1.0f - z * z);

	float Phi = Random01(Seed) * 2.0f * PI;
	float SinPhi, CosPhi;
	sincos(Phi, SinPhi, CosPhi);

	float x = r * CosPhi;
	float y = r * SinPhi;
	return float3(x, y, z);
}

inline float3 UniformSampleHemisphere(inout uint Seed)
{
	float z = Random01(Seed);
	float r = sqrt(1.0f - z * z);

	float Phi = Random01(Seed) * 2.0f * PI;
	float SinPhi, CosPhi;
	sincos(Phi, SinPhi, CosPhi);

	float x = r * CosPhi;
	float y = r * SinPhi;
	return float3(x, y, z);
}

float3 CosineSampleHemisphere(inout uint State)
{
	float RadiusSquared = Random01(State);
	float Phi = Random01(State) * 2.0f * PI;
	float SinPhi, CosPhi;
	sincos(Phi, SinPhi, CosPhi);
	return float3(sqrt(RadiusSquared) * CosPhi, sqrt(RadiusSquared) * SinPhi,
		sqrt(1.0f - RadiusSquared));
}

float3 ImportanceSampleGGX(inout uint Seed, float Roughness)
{
	float a = Roughness * Roughness;
	float a2 = a * a;

	float Phi = Random01(Seed) * 2.0f * PI;
	float SinPhi, CosPhi;
	sincos(Phi, SinPhi, CosPhi);

	float Theta = Random01(Seed);
	float CosTheta = sqrt((1.0f - Theta) / (1.0f + (a2 - 1.0f) * Theta));
	float SinTheta = sqrt(1.0f - CosTheta * CosTheta);

	float3 H;
	H.x = SinTheta * CosPhi;
	H.y = SinTheta * SinPhi;
	H.z = CosTheta;
	return H;
}

#endif // RAYSTORM_RANDOM_INCLUDE
