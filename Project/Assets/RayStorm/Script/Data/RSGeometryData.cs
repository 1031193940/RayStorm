using UnityEngine;

public struct RSGeometryData
{
    public int MaterialID;
    public int Indice0;
    public int Indice1;
    public int Indice2;

    public static RSGeometryData Default
    {
        get
        {
            RSGeometryData DefaultGeometryData = default(RSGeometryData);
            return DefaultGeometryData;
        }
    }

    public static int GetDataSize()
    {
        return sizeof(int) * 4;
    }
}
