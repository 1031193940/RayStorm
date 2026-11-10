#ifndef RAYSTORM_MATH_INCLUDE
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
#define RAYSTORM_MATH_INCLUDE

////////////////////////////////////////////////////////////////////////////////
// Definitions | Math constant values
////////////////////////////////////////////////////////////////////////////////

#ifndef PI
#define PI (3.141592653f)
#endif // PI

////////////////////////////////////////////////////////////////////////////////
// Common | General mathematical operation functions
////////////////////////////////////////////////////////////////////////////////

inline float SafeNormalize(float vec)
{
	if (abs(vec) < 0.0001f)
		return 0.0f;

	return vec / length(vec);
}

inline float2 SafeNormalize(float2 vec)
{
	float vecLength = sqrt(dot(vec, vec));
	return vec / max(vecLength, 0.0001f);
}

inline float3 SafeNormalize(float3 vec)
{
	float vecLength = sqrt(dot(vec, vec));
	return vec / max(vecLength, 0.0001f);
}

inline float4 SafeNormalize(float4 vec)
{
	float vecLength = sqrt(dot(vec, vec));
	return vec / max(vecLength, 0.0001f);
}

inline float Max3(float a, float b, float c)
{
	return max(max(a, b), c);
}

inline float Min3(float a, float b, float c)
{
	return min(min(a, b), c);
}

inline float Pow2(float x)
{
	return x * x;
}

inline float Pow3(float x)
{
	return x * x * x;
}

inline float Pow4(float x)
{
	float x2 = x * x;
	return x2 * x2;
}

inline float Pow5(float x)
{
	float x2 = x * x;
	return x2 * x2 * x;
}

inline float TriangularInterpolate(float a, float b, float c, float3 weights)
{
	return dot(weights, float3(a, b, c));
}

inline float2 TriangularInterpolate(float2 a, float2 b, float2 c, float3 weights)
{
	return mul(weights, float3x2(a, b, c));
}

inline float3 TriangularInterpolate(float3 a, float3 b, float3 c, float3 weights)
{
	return mul(weights, float3x3(a, b, c));
}

inline float4 TriangularInterpolate(float4 a, float4 b, float4 c, float3 weights)
{
	return mul(weights, float3x4(a, b, c));
}

////////////////////////////////////////////////////////////////////////////////
// Space Transform | Space transform functions
////////////////////////////////////////////////////////////////////////////////

inline float2 DirectionToSphericalUV(float3 Direction)
{
	float  Theta = acos(Direction.y) / -PI;
	float  Phi = atan2(Direction.x, -Direction.z) / -PI * 0.5f;
	return float2(Phi, Theta);
}

inline float3 TransformPointNDCToWorld(float4 PositionNDC, float4x4 MatrixProjectToWorld)
{
	float3 PositionWS = mul(MatrixProjectToWorld, PositionNDC).xyz;
	return PositionWS;
}

inline float3 TransformPointNDCToWorld(float4 PositionNDC, float4x4 MatrixInverseProject, float4x4 MatrixCameraToWorld)
{
	float4 PositionVS = mul(MatrixInverseProject, PositionNDC);
	float3 PositionWS = mul(MatrixCameraToWorld, PositionVS).xyz;
	return PositionWS;
}

inline float3 TransformDirNDCToWorld(float4 PositionNDC, float4x4 MatrixInverseProject, float4x4 MatrixCameraToWorld)
{
	float4 PositionVS = mul(MatrixInverseProject, PositionNDC);
	float3 PositionWS = mul(MatrixCameraToWorld, float4(PositionVS.xyz, 0.0f)).xyz;
	return PositionWS;
}

////////////////////////////////////////////////////////////////////////////////
// Quaternion | Quaternion build and rotate functions
////////////////////////////////////////////////////////////////////////////////

inline float4 QuaternionBuild(float3 Axis, float Theta)
{
	float SinHalfTheta, CosHalfTheta;
	sincos(Theta * 0.5f, SinHalfTheta, CosHalfTheta);
	return float4(Axis.xyz * SinHalfTheta, CosHalfTheta);
}

inline float3 QuaternionRotate(float4 q, float3 Position)
{
	return Position + cross(q.xyz, cross(q.xyz, Position) + q.w * Position) * 2.0f;
}

float4 QuaternionFromEuler(float x, float y, float z)
{
	float qx = sin(z / 2) * cos(y / 2) * cos(x / 2) - cos(z / 2) * sin(y / 2) * sin(x / 2);
	float qy = cos(z / 2) * sin(y / 2) * cos(x / 2) + sin(z / 2) * cos(y / 2) * sin(x / 2);
	float qz = cos(z / 2) * cos(y / 2) * sin(x / 2) - sin(z / 2) * sin(y / 2) * cos(x / 2);
	float qw = cos(z / 2) * cos(y / 2) * cos(x / 2) + sin(z / 2) * sin(y / 2) * sin(x / 2);
	return float4(qx, qy, qz, qw);
}

float3 QuaternionToEuler(float4 Quaternion)
{
	float x = Quaternion.x;
	float y = Quaternion.y;
	float z = Quaternion.z;
	float w = Quaternion.w;
	
	float t0 = 2.0f * (w * x + y * z);
	float t1 = 1.0f - (x * x + y * y) * 2.0f;
	float roll = atan2(t0, t1);

	float t2 = (w * y - z * x) * 2.0f;
	t2 = t2 > 1.0f ? 1.0f : t2;
	t2 = t2 < 1.0f ? 1.0f : t2;
	float pitch = asin(t2);

	float t3 = 2.0f * (w * z + x * y);
	float t4 = 1.0f - (y * y + z * z) * 2.0f;
	float yaw = atan2(t3, t4);
	return float3(yaw, pitch, roll);
}

float4 QuaternionFromTo(float3 VectorFrom, float3 VectorTo)
{
	float CosTheta = dot(VectorFrom, VectorTo);
	if (CosTheta < -0.999f) // Parallax and different direction
	{
		float3 SourceAxis = float3(1.0f, 0.0f, 0.0f);
		float3 VerticalAxis = float3(0.0f, 1.0f, 0.0f);
		float  CosPhi = dot(SourceAxis, VectorFrom);
		if (abs(CosPhi) < 0.999f)
		{
			float3 AdapterAxis = normalize(cross(SourceAxis, VectorFrom));
			float4 AdapterQuaternion = QuaternionBuild(AdapterAxis, acos(CosPhi));
			float3 axis = QuaternionRotate(AdapterQuaternion, VerticalAxis);
			return QuaternionBuild(axis, PI);
		}
		else
			return QuaternionBuild(VerticalAxis, PI);
	}
	else if (CosTheta < 0.999f) // Not parallax
	{
		float  angle = acos(CosTheta);
		float3 axis = normalize(cross(VectorFrom, VectorTo));
		return QuaternionBuild(axis, angle);
	}
	else // Parallax and same direction
	{
		return QuaternionBuild(VectorFrom, 0);
	}

	return 0.0f;
}

////////////////////////////////////////////////////////////////////////////////
// Spherical Harmonics | Spherical basis and operation functions
////////////////////////////////////////////////////////////////////////////////

float Y0(float3 v)
{
	return (1.0f / 2.0f) * sqrt(1.0f / PI);
}

float Y1(float3 v)
{
	return sqrt(3.0f / (4.0f * PI)) * v.z;
}

float Y2(float3 v)
{
	return sqrt(3.0f / (4.0f * PI)) * v.y;
}

float Y3(float3 v)
{
	return sqrt(3.0f / (4.0f * PI)) * v.x;
}

float Y4(float3 v)
{
	return 1.0f / 2.0f * sqrt(15.0f / PI) * v.x * v.z;
}

float Y5(float3 v)
{
	return 1.0f / 2.0f * sqrt(15.0f / PI) * v.z * v.y;
}

float Y6(float3 v)
{
	return 1.0f / 4.0f * sqrt(5.0f / PI) * (-v.x * v.x - v.z * v.z + 2.0f * v.y * v.y);
}

float Y7(float3 v)
{
	return 1.0f / 2.0f * sqrt(15.0f / PI) * v.y * v.x;
}

float Y8(float3 v)
{
	return 1.0f / 4.0f * sqrt(15.0f / PI) * (v.x * v.x - v.z * v.z);
}

float4 SphericalHarmonicsCofficientsTransform(float4 sourceCofficients, float3x3 transformMatrix)
{
	float3 N1 = float3(1.0f, 0.0f, 0.0f);
	float3 N2 = float3(0.0f, 1.0f, 0.0f);
	float3 N3 = float3(0.0f, 0.0f, 1.0f);

	float c = sqrt(4.0f * PI / 3.0f);
	float3x3 matrixInverseA = float3x3
		(
			float3(N1.z, N1.y, N1.x) * c,
			float3(N2.z, N2.y, N2.x) * c,
			float3(N3.z, N3.y, N3.x) * c
			);

	float  ct = sqrt(3.0f / (4.0f * PI));
	float3 Nt1 = normalize(mul(transformMatrix, N1));
	float3 Nt2 = normalize(mul(transformMatrix, N2));
	float3 Nt3 = normalize(mul(transformMatrix, N3));
	float3x3 matrixP = float3x3
		(
			float3(Nt1.z, Nt2.z, Nt3.z) * ct,
			float3(Nt1.y, Nt2.y, Nt3.y) * ct,
			float3(Nt1.x, Nt2.x, Nt3.x) * ct
			);

	float3 SH1Cofficients = sourceCofficients.yzw;
	SH1Cofficients = mul(matrixP, mul(matrixInverseA, SH1Cofficients));

	float4 transformedCofficients = float4(sourceCofficients.x, SH1Cofficients);
	return transformedCofficients;
}

////////////////////////////////////////////////////////////////////////////////
// Intersect Functions | Geometric intersection test functions
////////////////////////////////////////////////////////////////////////////////

bool TriangleIntersectWithAABBBox(float3 AABBCenterPos, float3 AABBSize, float3 v0, float3 v1, float3 v2)
{
	v0 -= AABBCenterPos;
	v1 -= AABBCenterPos;
	v2 -= AABBCenterPos;

	float3 e0 = normalize(v1 - v0);
	float3 e1 = normalize(v2 - v0);
	float3 e2 = normalize(v2 - v1);

	float3 n0 = float3(1.0f, 0.0f, 0.0f);
	float3 n1 = float3(0.0f, 1.0f, 0.0f);
	float3 n2 = float3(0.0f, 0.0f, 1.0f);

	const int kAxisCount = 13;
	float3 AxisList[13] =
	{
		n0, n1, n2,
		cross(e0, e1),
		cross(n0, e0), cross(n0, e1), cross(n0, e2),
		cross(n1, e0), cross(n1, e1), cross(n1, e2),
		cross(n2, e0), cross(n2, e1), cross(n2, e2),
	};

	for (int AxisID = 0; AxisID < kAxisCount; ++AxisID)
	{
		float3 axis = normalize(AxisList[AxisID]);

		float p0 = dot(v0, axis);
		float p1 = dot(v1, axis);
		float p2 = dot(v2, axis);

		float r = AABBSize.x * abs(dot(axis, n0)) * 0.5f +
			AABBSize.y * abs(dot(axis, n1)) * 0.5f +
			AABBSize.z * abs(dot(axis, n2)) * 0.5f;

		if (max(-Max3(p0, p1, p2), Min3(p0, p1, p2)) > r)
			return false;
	}

	return true;
}

bool RayIntersectWithAABBBox(float3 RaySource, float3 RayDirection, float3 AABBBoxCenter, float3 AABBBoxSize, out float DistanceToIntersectPos, out float DistanceInsideBox)
{
	float3 BoundBoxMin = AABBBoxCenter - AABBBoxSize * 0.5f;
	float3 BoundBoxMax = AABBBoxCenter + AABBBoxSize * 0.5f;

	DistanceToIntersectPos = 0.0f;
	DistanceInsideBox = 0.0f;
	float exitDistance = 3.402823466e+38f;
	[unroll]
	for (int axis = 0; axis < 3; ++axis)
	{
		// Avoid 0/0 for rays lying on a slab boundary, including axis-aligned rays.
		if (abs(RayDirection[axis]) < 1.0e-20f)
		{
			if (RaySource[axis] < BoundBoxMin[axis] || RaySource[axis] > BoundBoxMax[axis])
				return false;
		}
		else
		{
			float firstDistance = (BoundBoxMin[axis] - RaySource[axis]) / RayDirection[axis];
			float secondDistance = (BoundBoxMax[axis] - RaySource[axis]) / RayDirection[axis];
			DistanceToIntersectPos = max(DistanceToIntersectPos, min(firstDistance, secondDistance));
			exitDistance = min(exitDistance, max(firstDistance, secondDistance));
			if (exitDistance < DistanceToIntersectPos)
				return false;
		}
	}
	DistanceInsideBox = exitDistance - DistanceToIntersectPos;
	return true;
}

bool RayIntersectWithTriangle(float3 RaySource, float3 RayDirection, float3 p0, float3 p1, float3 p2, out float3 weights, out float hitDistance)
{
	const float kEpsilon = 0.000001f;

	weights = 0.0f;
	hitDistance = 0.0f;

	// Triangle edges from p1 to p2 and p3
	float3 e1 = p1 - p0;
	float3 e2 = p2 - p0;

	// Calculate determinant, Ray lines in the triangle plane if determinant is near 0
	float3 p = cross(RayDirection, e2);
	float  det = dot(e1, p);
	if (abs(det) < kEpsilon)
		return false;

	float3 t = RaySource - p0;
	float invDet = rcp(det);

	// Calculate u parameter, ray not hit triangle if u is not in range[0, 1]
	float u = dot(t, p) * invDet;
	if (u < 0 || u > 1)
		return false;

	// Calculate v parameter, ray not hit triangle if v is not in range[0, 1]
	float3 q = cross(t, e1);
	float  v = dot(RayDirection, q) * invDet;
	if (v < 0 || u + v > 1)
		return false;

	hitDistance = dot(e2, q) * invDet;
	if (hitDistance > kEpsilon)
	{
		weights = float3(1.0f - u - v, u, v);
		return true;
	}

	return false;
}

bool RayIntersectWithTriangle(float3 RaySource, float3 RayDirection, float3 p0, float3 p1, float3 p2, out float3 weights)
{
	float hitDistance;
	return RayIntersectWithTriangle(RaySource, RayDirection, p0, p1, p2, weights, hitDistance);
}

#endif // RAYSTORM_MATH_INCLUDE
