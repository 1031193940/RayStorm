using UnityEngine;

public struct RSCustomizeObject
{
    public int ProgramID;
    public int MaterialID;
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale;
    public Vector4 Other;

    public static RSCustomizeObject Default
    {
        get
        {
            RSCustomizeObject DefaultObjectData = default(RSCustomizeObject);
            DefaultObjectData.ProgramID = -1;
            DefaultObjectData.Scale = Vector3.one;
            return DefaultObjectData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(int) * (1 + 1) + sizeof(float) * (3 + 3 + 3 + 4);
    }
}
