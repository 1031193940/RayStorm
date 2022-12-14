using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class RSMathUtility
{
	public static float TriangularInterpolate(float a, float b, float c, Vector3 Weights)
    {
		return a * Weights.x + b * Weights.y + c * Weights.z;
    }

	public static Vector2 TriangularInterpolate(Vector2 a, Vector2 b, Vector2 c, Vector3 Weights)
	{
		return a * Weights.x + b * Weights.y + c * Weights.z;
	}

	public static Vector3 TriangularInterpolate(Vector3 a, Vector3 b, Vector3 c, Vector3 Weights)
	{
		return a * Weights.x + b * Weights.y + c * Weights.z;
	}

	public static bool IsTriangleIntersectWithAABBBox(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 AABBCenter, Vector3 AABBSize)
    {
        v0 -= AABBCenter;
        v1 -= AABBCenter;
        v2 -= AABBCenter;

        Vector3 e0 = (v1 - v0).normalized;
        Vector3 e1 = (v2 - v0).normalized;
        Vector3 e2 = (v2 - v1).normalized;

        Vector3 n0 = new Vector3(1.0f, 0.0f, 0.0f);
        Vector3 n1 = new Vector3(0.0f, 1.0f, 0.0f);
        Vector3 n2 = new Vector3(0.0f, 0.0f, 1.0f);

        Vector3[] AxisList =
        {
            n0, n1, n2,
            Vector3.Cross(e0, e1).normalized,
            Vector3.Cross(n0, e0).normalized, Vector3.Cross(n0, e1).normalized, Vector3.Cross(n0, e2).normalized,
            Vector3.Cross(n1, e0).normalized, Vector3.Cross(n1, e1).normalized, Vector3.Cross(n1, e2).normalized,
            Vector3.Cross(n2, e0).normalized, Vector3.Cross(n2, e1).normalized, Vector3.Cross(n2, e2).normalized,
        };

        foreach (var Axis in AxisList)
        {
            float p0 = Vector3.Dot(v0, Axis);
            float p1 = Vector3.Dot(v1, Axis);
            float p2 = Vector3.Dot(v2, Axis);

            float r = AABBSize.x * Mathf.Abs(Vector3.Dot(Axis, n0)) * 0.5f +
                      AABBSize.y * Mathf.Abs(Vector3.Dot(Axis, n1)) * 0.5f +
                      AABBSize.z * Mathf.Abs(Vector3.Dot(Axis, n2)) * 0.5f;

            if (Mathf.Max(-Mathf.Max(Mathf.Max(p0, p1), p2), Mathf.Min(Mathf.Min(p0, p1), p2)) > r)
                return false;
        }

        return true;
    }

	public static bool RayIntersectWithAABBBox(Vector3 RaySource, Vector3 RayDirection, Vector3 AABBBoxCenter, Vector3 AABBBoxSize, out float DistanceToIntersectPos, out float DistanceInsideBox)
	{
		Vector3 BoundBoxMin = AABBBoxCenter - AABBBoxSize * 0.5f;
		Vector3 BoundBoxMax = AABBBoxCenter + AABBBoxSize * 0.5f;

		Vector3 InverseRayDirection = new Vector3(1.0f / RayDirection.x, 1.0f / RayDirection.y, 1.0f / RayDirection.z);
		Vector3 BoundBoxDistanceMin = Vector3.Scale((BoundBoxMin - RaySource), InverseRayDirection);
		Vector3 BoundBoxDistanceMax = Vector3.Scale((BoundBoxMax - RaySource), InverseRayDirection);
		Vector3 IntersectDistanceMin = Vector3.Min(BoundBoxDistanceMin, BoundBoxDistanceMax);
		Vector3 IntersectDistanceMax = Vector3.Max(BoundBoxDistanceMin, BoundBoxDistanceMax);

		float DistanceMin = Mathf.Min(IntersectDistanceMax.x, Mathf.Min(IntersectDistanceMax.y, IntersectDistanceMax.z));
		float DistanceMax = Mathf.Max(IntersectDistanceMin.x, Mathf.Max(IntersectDistanceMin.y, IntersectDistanceMin.z));

		DistanceToIntersectPos = Mathf.Max(0, DistanceMax);
		DistanceInsideBox = Mathf.Max(0, DistanceMin - DistanceToIntersectPos);

		return DistanceInsideBox > 0.0f;
	}

	public static bool RayIntersectWithTriangle(Vector3 RaySource, Vector3 RayDirection, Vector3 p0, Vector3 p1, Vector3 p2, out Vector3 weights)
	{
		const float kEpsilon = 0.000001f;

		weights = Vector3.zero;

		// Triangle edges from p1 to p2 and p3
		Vector3 e1 = p1 - p0;
		Vector3 e2 = p2 - p0;

		// Calculate determinant, Ray lines in the triangle plane if determinant is near 0
		Vector3 p = Vector3.Cross(RayDirection, e2);
		float det = Vector3.Dot(e1, p);
		if (Mathf.Abs(det) < kEpsilon)
			return false;

		Vector3 t = RaySource - p0;
		float invDet = 1.0f / det;

		// Calculate u parameter, ray not hit triangle if u is not in range[0, 1]
		float u = Vector3.Dot(t, p) * invDet;
		if (u < 0 || u > 1)
			return false;

		// Calculate v parameter, ray not hit triangle if v is not in range[0, 1]
		Vector3 q = Vector3.Cross(t, e1);
		float v = Vector3.Dot(RayDirection, q) * invDet;
		if (v < 0 || u + v > 1)
			return false;

		if (Vector3.Dot(e2, q) * invDet > kEpsilon)
		{
			weights = new Vector3(1.0f - u - v, u, v);
			return true;
		}

		return false;
	}
}
