using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RSSceneData
{
    public Texture SkyboxTexture = null;
    
    public RSMaterialData[] MaterialList = null;
    public RSVertexData[] VertexList = null;
    public RSGeometryData[] GeometryList = null;
    public RSCustomizeObject[] CustomizeObjectList = null;
    public RSOctreeData[] OctreeList = null;
    public int[] OctreeGeometryIDList = null;

    public static RSSceneData Default
    {
        get
        {
            RSSceneData NewSceneData = new RSSceneData();
            NewSceneData.SkyboxTexture = Texture2D.whiteTexture;
            return NewSceneData;
        }
    }
}
