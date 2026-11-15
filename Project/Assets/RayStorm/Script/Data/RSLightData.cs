using UnityEngine;

namespace RayStorm
{
    public struct RSLightData
    {
        public int Type;
        public int GeometryID;
        public float Area;
        public Vector3 Position;
        public Vector3 Direction;
        public Vector3 Radiance;
        public float InnerCosine;
        public float OuterCosine;

        public static int GetDataSize()
        {
            return sizeof(int) * 2 + sizeof(float) * 12;
        }
    }

    public struct RSAliasData
    {
        public float Probability;
        public float Threshold;
        public int Alias;

        public static int GetDataSize()
        {
            return sizeof(float) * 2 + sizeof(int);
        }
    }
}
