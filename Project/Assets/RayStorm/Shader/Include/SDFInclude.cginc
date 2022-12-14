#ifndef RAYSTORM_SDF_INCLUDE
#define RAYSTORM_SDF_INCLUDE

#include "MathInclude.cginc"

////////////////////////////////////////////////////////////////////////////////
/// Helper Functions | Signed distance field calculate helper functions
////////////////////////////////////////////////////////////////////////////////

inline float Dot2(float2 x)
{
    return dot(x, x);
}

inline float Dot2(float3 x)
{
    return dot(x, x);
}

////////////////////////////////////////////////////////////////////////////////
/// Process Functions | Signed distance field process functions
////////////////////////////////////////////////////////////////////////////////

// [IQ SDF 3D] https://iquilezles.org/www/articles/distfunctions/distfunctions.htm

inline float SDFUnion(float a, float b)
{
    return min(a, b);
}

inline float SDFSmoothUnion(float a, float b, float meta)
{
    float h = saturate(0.5f + (b - a) / meta * 0.5f);
    return lerp(b, a, h) - meta * h * (1.0f - h);
}

inline float SDFSubtraction(float a, float b)
{
    return max(a, -b);
}

inline float SDFSmoothSubtraction(float a, float b, float meta)
{
    float h = saturate(0.5f - (b + a) / meta * 0.5f);
    return lerp(a, -b, h) + meta * h * (1.0f - h);
}

inline float SDFIntersection(float a, float b)
{
    return max(a, b);
}

inline float SDFSmoothIntersection(float a, float b, float meta)
{
    float h = saturate(0.5f - (a - b) / meta * 0.5f);
    return lerp(a, b, h) + meta * h * (1.0f - h);
}

inline float SDFRound(float a, float rad)
{
    return a - rad;
}

////////////////////////////////////////////////////////////////////////////////
/// 2D SDF Functions | Signed distance field calculate functions
////////////////////////////////////////////////////////////////////////////////

inline float SampleSDFCircle(float2 uv, float2 Center, float Radius)
{
    return distance(uv, Center) - Radius;
}

inline float SampleSDFRing(float2 uv, float2 Center, float Radius, float Width)
{
    float DistanceToCenter = distance(uv, Center);
    return distance(DistanceToCenter, Radius) - Width;
}

inline float SampleSDFLine(float2 uv, float2 p0, float2 p1, float Width)
{
    float2 StartToPixel = uv - p0;
    float2 StartToPoint = p1 - p0;
    float  h = saturate(dot(StartToPixel, StartToPoint) / dot(StartToPoint, StartToPoint));
    return distance(StartToPixel, StartToPoint * h) - Width;
}

////////////////////////////////////////////////////////////////////////////////
/// 3D SDF Functions | Signed distance field calculate functions
////////////////////////////////////////////////////////////////////////////////

inline float SampleSDFSphere(float3 position, float3 spherePosition, float sphereRadius)
{
    return distance(position, spherePosition) - sphereRadius;
}

inline float SampleSDFPlane(float3 position, float3 planePosition, float3 planeNormal)
{
    return position.y;
    return dot(position - planePosition, planeNormal);
}

inline float SampleSDFBox(float3 position, float3 boxPosition, float3 boxSize)
{
    float3 p = position - boxPosition;
    float3 q = abs(p) - boxSize;
    return length(max(q, 0.0f)) + min(max(q.x, max(q.y, q.z)), 0.0f);
}

inline float SampleSDFTriangle(float3 position, float3 v1, float3 v2, float3 v3)
{
    float3 p = position;
    float3 a = v1, b = v2, c = v3;
    float3 ba = b - a;
    float3 pa = p - a;
    float3 cb = c - b;
    float3 pb = p - b;
    float3 ac = a - c;
    float3 pc = p - c;
    float3 normal = cross(ba, ac);

    float d2 = 0.0f;
    if (sign(dot(cross(ba, normal), pa)) + sign(dot(cross(cb, normal), pb)) + sign(dot(cross(ac, normal), pc)) < 2.0f)
        d2 = min(min(Dot2(ba * saturate(dot(ba, pa) / max(Dot2(ba), 0.0001f)) - pa), Dot2(cb * saturate(dot(cb, pb) / max(Dot2(cb), 0.0001f)) - pb)), Dot2(ac * saturate(dot(ac, pc) / max(Dot2(ac), 0.0001f)) - pc));
    else
        d2 = dot(normal, pa) * dot(normal, pa) / max(Dot2(normal), 0.0001f);

    return sqrt(d2);
}


#endif // RAYSTORM_SDF_INCLUDE