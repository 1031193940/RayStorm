using UnityEngine;

public struct RSLightData
{
    public int ProgramID;
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Color;
    public Vector4 Data;

    public static RSLightData Default
    {
        get
        {
            RSLightData DefaultLightData = default(RSLightData);
            DefaultLightData.ProgramID = -1;
            DefaultLightData.Direction = Vector3.forward;
            DefaultLightData.Color = Vector3.one;
            return DefaultLightData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(int) + sizeof(float) * (3 + 3 + 3 + 4);
    }
}
