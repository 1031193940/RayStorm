using UnityEngine;

namespace RayStorm
{
    public struct RSBVHData
    {
        public Vector3 Minimum;
        public int StartID;
        public Vector3 Maximum;
        public int GeometryCount;

        public static int GetDataSize()
        {
            return sizeof(float) * 6 + sizeof(int) * 2;
        }
    }
}
