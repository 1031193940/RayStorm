using UnityEngine;

public struct RSVertexData
{
    public Vector3 Position;
    public Vector2 UV0;
    public Vector2 UV1;
    public Vector3 Normal;
    public Vector4 Tangent;

    public static RSVertexData Default
    {
        get
        {
            RSVertexData DefaultVertexData = default(RSVertexData);
            DefaultVertexData.Normal = Vector3.up;
            DefaultVertexData.Tangent = Vector3.right;
            return DefaultVertexData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(float) * (3 + 2 + 2 + 3 + 4);
    }
}
