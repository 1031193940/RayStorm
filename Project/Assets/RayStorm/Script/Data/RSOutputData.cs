using UnityEngine;

namespace RayStorm
{
    public struct RSOutputData
    {
        public Vector4 Data;

        public static RSOutputData Default
        {
            get
            {
                RSOutputData DefaultOutputData = default(RSOutputData);
                return DefaultOutputData;
            }
        }

        public static int GetDataSize()
        {
            return sizeof(float) * (4);
        }
    }
}