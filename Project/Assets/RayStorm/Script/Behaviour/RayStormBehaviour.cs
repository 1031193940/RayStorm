using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class RayStormBehaviour : ScriptableObject
{
    public int FrameCount = 0; // 0 means no frame count limit

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Virtual Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    
    public virtual void OnLoad(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnStartup(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnFinished(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnUnload(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnFrameStartup(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneData OutputSceneData)
    {
        DefaultSceneCollectFunction(Context, out OutputSceneData);
    }

    public virtual RSRenderTask[] OnFrameCollectRenderTask(RSRenderContext Context)
    {
        int TextureWidth = Context.MainCamera.pixelWidth;
        int TextureHeight = Context.MainCamera.pixelHeight;

        int RenderWindowSize;
        int SubpixelCountPerPixel;
        switch (Context.FrameID)
        {
            case 0:
                RenderWindowSize = Mathf.Max(TextureWidth, TextureHeight);
                SubpixelCountPerPixel = 1;
                break;
            case 1:
                RenderWindowSize = 128;
                SubpixelCountPerPixel = 4;
                break;
            default:
                RenderWindowSize = 32;
                SubpixelCountPerPixel = 64;
                break;
        }

        List<RSRenderTask> RenderTaskList = new List<RSRenderTask>();
        int RenderWindowCountRow = Mathf.CeilToInt(TextureHeight / (float)RenderWindowSize);
        int RenderWindowCountCol = Mathf.CeilToInt(TextureWidth / (float)RenderWindowSize);
        for (int RowID = 0; RowID < RenderWindowCountRow; ++RowID)
        {
            for (int ColID = 0; ColID < RenderWindowCountCol; ++ColID)
            {
                RSRenderTask NewRenderTask = RSRenderTask.Default;
                NewRenderTask.FrameID = Context.FrameID;
                NewRenderTask.WindowPos = new Vector2Int(ColID, RowID) * RenderWindowSize;
                NewRenderTask.WindowSize = Vector2Int.one * RenderWindowSize;
                NewRenderTask.SubpixelCountPerPixel = SubpixelCountPerPixel;
                RenderTaskList.Add(NewRenderTask);
            }
        }

        return RenderTaskList.ToArray();
    }

    public virtual void OnFrameUpdate(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnFramePostProcess(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnFrameFinished(RSRenderContext Context)
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnInspectorGUI() 
    {
        // !!!TODO : Override in child class
    }

    public virtual void OnDrawGizmos() 
    {
        // !!!TODO : Override in child class
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Default Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    private void DefaultSceneCollectFunction(RSRenderContext Context, out RSSceneData OutputSceneData)
    {
        var SceneObjectList = RSSceneUtility.CollectSceneObject();

        // Find Dynamic Programs
        var ToolkitLibraryFolder = RSDynamicUtility.GetToolkitLibraryDirectory();
        var DynamicMaterialPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Material/");
        var CustomizeObjectPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Object/");

        // Build scene object and material data list
        List<RSVertexData> SceneVertexList = new List<RSVertexData>();
        List<RSGeometryData> SceneGeometryList = new List<RSGeometryData>();
        List<RSMaterialData> SceneMaterialList = new List<RSMaterialData>();
        List<RSCustomizeObject> SceneCustomizeObjectList = new List<RSCustomizeObject>();
        foreach (var SceneObject in SceneObjectList)
        {
            if (SceneObject.Type == RSSceneUtility.ESceneObjectType.Mesh)
            {
                var Mesh = SceneObject.Mesh;
                var Transform = SceneObject.Renderer.transform;

                var MeshIndices = Mesh.triangles;
                int SubmeshCount = SceneObject.Mesh.subMeshCount;
                int SubmeshIndiceOffset = SceneVertexList.Count;
                for (int SubmeshID = 0; SubmeshID < SubmeshCount; ++SubmeshID)
                {
                    int MaterialID = SceneMaterialList.Count;

                    SubMeshDescriptor SubmeshDesc = SceneObject.Mesh.GetSubMesh(SubmeshID);
                    for (int SubmeshIndiceID = 0; SubmeshIndiceID < SubmeshDesc.indexCount; SubmeshIndiceID += 3)
                    {
                        RSGeometryData NewGeometry = RSGeometryData.Default;
                        NewGeometry.MaterialID = MaterialID;
                        NewGeometry.Indice0 = MeshIndices[SubmeshDesc.indexStart + SubmeshIndiceID + 0] + SubmeshIndiceOffset;
                        NewGeometry.Indice1 = MeshIndices[SubmeshDesc.indexStart + SubmeshIndiceID + 1] + SubmeshIndiceOffset;
                        NewGeometry.Indice2 = MeshIndices[SubmeshDesc.indexStart + SubmeshIndiceID + 2] + SubmeshIndiceOffset;
                        SceneGeometryList.Add(NewGeometry);
                    }

                    // Find Appropriate-Material-ProgramID
                    Material SourceMaterial = SceneObject.Renderer.sharedMaterials[SubmeshID];
                    string MaterialNameLower = SourceMaterial.name.ToLower();
                    string ShaderNameLower = SourceMaterial.shader.name.ToLower();
                    int MaterialProgramID = System.Array.FindIndex(DynamicMaterialPrograms, ProgramData =>
                    {
                        string ProgramNameLower = ProgramData.Name.ToLower();
                        return MaterialNameLower.Contains(ProgramNameLower) || ShaderNameLower.Contains(ProgramNameLower);
                    });

                    RSMaterialData NewMaterial = RSMaterialData.CreateFromUnityMaterial(SourceMaterial);
                    NewMaterial.ProgramID = MaterialProgramID;
                    SceneMaterialList.Add(NewMaterial);
                }

                var MeshVertices = Mesh.vertices;
                var MeshUV0s = Mesh.uv;
                var MeshUV1s = Mesh.uv2;
                var MeshNormals = Mesh.normals;
                var MeshTangents = Mesh.tangents;
                for (int VertexID = 0; VertexID < MeshVertices.Length; ++VertexID)
                {
                    RSVertexData NewVertexData = RSVertexData.Default;
                    NewVertexData.Position = Transform.TransformPoint(MeshVertices[VertexID]);
                    NewVertexData.UV0 = RSDataUtility.IsNullOrEmpty(MeshUV0s) ? Vector2.zero : MeshUV0s[VertexID];
                    NewVertexData.UV1 = RSDataUtility.IsNullOrEmpty(MeshUV1s) ? Vector2.zero : MeshUV1s[VertexID];
                    NewVertexData.Normal = RSDataUtility.IsNullOrEmpty(MeshNormals) ? Vector3.up : Transform.TransformVector(MeshNormals[VertexID]).normalized;
                    NewVertexData.Tangent = RSDataUtility.IsNullOrEmpty(MeshNormals) ? Vector3.forward : Transform.TransformVector(MeshTangents[VertexID]).normalized;
                    SceneVertexList.Add(NewVertexData);
                }

            }
            else if (SceneObject.Type == RSSceneUtility.ESceneObjectType.Customize)
            {
                int MaterialID = SceneMaterialList.Count;

                // Add new material data to material list
                {
                    Material SourceMaterial = SceneObject.Renderer.sharedMaterial;
                    string MaterialNameLower = SourceMaterial.name.ToLower();
                    string ShaderNameLower = SourceMaterial.shader.name.ToLower();
                    int MaterialProgramID = System.Array.FindIndex(DynamicMaterialPrograms, ProgramData =>
                    {
                        string ProgramNameLower = ProgramData.Name.ToLower();
                        return MaterialNameLower.Contains(ProgramNameLower) || ShaderNameLower.Contains(ProgramNameLower);
                    });

                    RSMaterialData NewMaterial = RSMaterialData.CreateFromUnityMaterial(SourceMaterial);
                    NewMaterial.ProgramID = MaterialProgramID;
                    SceneMaterialList.Add(NewMaterial);
                }

                // Add new customize object to list
                {
                    string ObjectProgramNameLower = SceneObject.CustomizeObject.DynamicObjectProgramName.ToLower();
                    int ObjectProgramID = System.Array.FindIndex(CustomizeObjectPrograms, ProgramData => ProgramData.Name.ToLower().Contains(ObjectProgramNameLower));

                    RSCustomizeObject CustomizeObject = RSCustomizeObject.Default;
                    CustomizeObject.MaterialID = MaterialID;
                    CustomizeObject.ProgramID = ObjectProgramID;
                    CustomizeObject.Position = SceneObject.Renderer.transform.position;
                    CustomizeObject.Rotation = SceneObject.Renderer.transform.rotation.eulerAngles;
                    CustomizeObject.Scale = SceneObject.Renderer.transform.lossyScale;
                    SceneCustomizeObjectList.Add(CustomizeObject);
                }
            }
        }

        Cubemap SkyboxCubemap = RSDataUtility.CreateSolidCubemap(32, TextureFormat.ARGB32, false, Color.white);

        OutputSceneData = RSSceneData.Default;
        OutputSceneData.VertexList = SceneVertexList.ToArray();
        OutputSceneData.GeometryList = SceneGeometryList.ToArray();
        OutputSceneData.CustomizeObjectList = SceneCustomizeObjectList.ToArray();
        OutputSceneData.MaterialList = SceneMaterialList.ToArray();
        OutputSceneData.SkyboxTexture = SkyboxCubemap;
        OutputSceneData.OctreeList = null;
        OutputSceneData.OctreeGeometryIDList = null;
    }

}
