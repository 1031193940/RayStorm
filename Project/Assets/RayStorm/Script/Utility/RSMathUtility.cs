using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
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

		public static bool RayIntersectWithAABBBox(Vector3 RaySource, Vector3 RayDirection, Vector3 AABBBoxCenter, Vector3 AABBBoxSize, out float DistanceToIntersectPos, out float DistanceInsideBox)
		{
			Vector3 BoundBoxMin = AABBBoxCenter - AABBBoxSize * 0.5f;
			Vector3 BoundBoxMax = AABBBoxCenter + AABBBoxSize * 0.5f;

			DistanceToIntersectPos = 0.0f;
			DistanceInsideBox = 0.0f;
			float ExitDistance = float.MaxValue;
			for (int AxisID = 0; AxisID < 3; ++AxisID)
			{
				if (Mathf.Abs(RayDirection[AxisID]) < 1.0e-20f)
				{
					if (RaySource[AxisID] < BoundBoxMin[AxisID] || RaySource[AxisID] > BoundBoxMax[AxisID])
						return false;
					continue;
				}
				float FirstDistance = (BoundBoxMin[AxisID] - RaySource[AxisID]) / RayDirection[AxisID];
				float SecondDistance = (BoundBoxMax[AxisID] - RaySource[AxisID]) / RayDirection[AxisID];
				DistanceToIntersectPos = Mathf.Max(DistanceToIntersectPos, Mathf.Min(FirstDistance, SecondDistance));
				ExitDistance = Mathf.Min(ExitDistance, Mathf.Max(FirstDistance, SecondDistance));
				if (ExitDistance < DistanceToIntersectPos)
					return false;
			}
			DistanceInsideBox = ExitDistance - DistanceToIntersectPos;
			return true;
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
}
