using UnityEngine;

public struct RSOctreeData
{
    public int ParentTreeID;
    public int SubtreeStartID;
    public int SubtreeCount;
    public int GeometryStartID;
    public int GeometryCount;
    public Vector3 Center;
    public Vector3 Size;

    public static RSOctreeData Default
    {
        get
        {
            RSOctreeData NewOctreeData = default(RSOctreeData);
            return NewOctreeData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(int) * 5 + sizeof(float) * (3 + 3);
    }
}