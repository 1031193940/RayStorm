using UnityEngine;

public struct RSSourceData
{
    public Vector2 ID;
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Energy;

    public static RSSourceData Default
    {
        get
        {
            RSSourceData DefaultSourceData = default(RSSourceData);
            DefaultSourceData.Direction = Vector3.forward;
            DefaultSourceData.Energy = Vector3.one;
            return DefaultSourceData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(float) * (2 + 3 + 3 + 3);
    }
}
