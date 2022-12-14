using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RayStormRenderer : MonoBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Variables | 
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [SerializeField] public bool ContainsTriangleMesh = true;
    [SerializeField] public bool ContainsCustomizeObject = true;
    [SerializeField] public bool ContainsLightSource = true;
    [SerializeField] public bool ContainsSkybox = true;

    [SerializeField] public bool IsDisplayOctree = false;
    [SerializeField] public Texture SkyboxTexture = null;
    [SerializeField] public RayStormBehaviour RayStormBehaviour = null;

    private RSRenderContext Context = null;

    private ComputeShader RaytraceComputeShader = null;
    private ComputeShader AdditiveComputeShader = null;

    private ComputeBuffer SceneVertexBuffer = null;
    private ComputeBuffer SceneGeometryBuffer = null;
    private ComputeBuffer SceneCustomizeObjectBuffer = null;
    private ComputeBuffer SceneMaterialBuffer = null;
    private ComputeBuffer SceneOctreeBuffer = null;
    private ComputeBuffer SceneOctreeGeometryIDBuffer = null;
    private ComputeBuffer RaytraceSourceBuffer = null;
    private ComputeBuffer RaytraceOutputBuffer = null;

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Unity Functions | 
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Awake()
    {
        if (RayStormBehaviour == null)
            RayStormBehaviour = ScriptableObject.CreateInstance(typeof(RSDefaultBehaviour)) as RayStormBehaviour;

        Context = new RSRenderContext();
        Context.MainCamera = GetComponent<Camera>();
        Context.RenderTaskQueue = new Queue<RSRenderTask>();
        Context.ContainsTriangleMesh = ContainsTriangleMesh;
        Context.ContainsCustomizeObject = ContainsCustomizeObject;
        Context.ContainsLightSource = ContainsLightSource;
        Context.ContainsSkybox = ContainsSkybox;
    }

    public void Start()
    {
        RecompileProgramLibraries();

        int TextureWidth = Context.MainCamera.pixelWidth;
        int TextureHeight = Context.MainCamera.pixelHeight;
        Context.RaytraceTexture = new RenderTexture(TextureWidth, TextureHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Context.RaytraceTexture.useMipMap = false;
        Context.RaytraceTexture.enableRandomWrite = true;
        Context.RaytraceTexture.filterMode = FilterMode.Point;
        Context.RaytraceTexture.Create();

        RSSceneData CollectedSceneData = CollectSceneData();
        SkyboxTexture = (SkyboxTexture == null ? CollectedSceneData.SkyboxTexture : SkyboxTexture);
        SceneVertexBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.VertexList, RSVertexData.GetDataSize());
        SceneGeometryBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.GeometryList, RSGeometryData.GetDataSize());
        SceneCustomizeObjectBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.CustomizeObjectList, RSCustomizeObject.GetDataSize());
        SceneMaterialBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.MaterialList, RSMaterialData.GetDataSize());
        SceneOctreeBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.OctreeList, RSOctreeData.GetDataSize());
        SceneOctreeGeometryIDBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedSceneData.OctreeGeometryIDList, sizeof(int));

        // Load raytrace compute shader and setup constant uniform variables
        RaytraceComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormRaytracer");
        {
            var RaytraceKernelID = RaytraceComputeShader.FindKernel("CSKernelMain");
            RaytraceComputeShader.SetFloat("RSRandomSeed", Random.value);
            RaytraceComputeShader.SetTexture(RaytraceKernelID, "RSSkyboxTexture", SkyboxTexture);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneVertexBuffer", SceneVertexBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneGeometryBuffer", SceneGeometryBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneObjectBuffer", SceneCustomizeObjectBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneMaterialBuffer", SceneMaterialBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeBuffer", SceneOctreeBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeGeometryIDBuffer", SceneOctreeGeometryIDBuffer);
        }

        // Load additive compute shader and setup constant uniform variables
        AdditiveComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormAdditive");

        RayStormBehaviour.OnLoad(Context);

        RayStormBehaviour.OnCollectRenderTask(Context);
    }

    public void Update()
    {
        RayStormBehaviour.OnUpdate(Context);
    }

    public void OnRenderImage(RenderTexture Source, RenderTexture Destination)
    {
        if (Context.RenderTaskQueue.Count > 0)
        {
            RSRenderTask RenderTask = Context.RenderTaskQueue.Dequeue();
            ProcessRenderTask(RenderTask);
        }

        Graphics.Blit(Context.RaytraceTexture, Destination);
    }

    public void OnDrawGizmos()
    {
        if (RayStormBehaviour != null)
            RayStormBehaviour.OnDrawGizmos();

        if (IsDisplayOctree && SceneOctreeBuffer != null && SceneOctreeBuffer.count > 0)
        {
            RSOctreeData[] OctreeList = new RSOctreeData[SceneOctreeBuffer.count];
            SceneOctreeBuffer.GetData(OctreeList);

            Gizmos.color = Color.green;
            foreach (var OctreeData in OctreeList)
            {
                if (OctreeData.SubtreeCount == 0)
                    Gizmos.DrawWireCube(OctreeData.Center, OctreeData.Size);
            }
        }
    }

    public void OnDestroy()
    {
        RayStormBehaviour.OnUnload(Context);

        RSDataUtility.ReleaseBuffer(SceneVertexBuffer);
        RSDataUtility.ReleaseBuffer(SceneGeometryBuffer);
        RSDataUtility.ReleaseBuffer(SceneCustomizeObjectBuffer);
        RSDataUtility.ReleaseBuffer(SceneMaterialBuffer);
        RSDataUtility.ReleaseBuffer(SceneOctreeBuffer);
        RSDataUtility.ReleaseBuffer(SceneOctreeGeometryIDBuffer);
        RSDataUtility.ReleaseBuffer(RaytraceSourceBuffer);
        RSDataUtility.ReleaseBuffer(RaytraceOutputBuffer);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Callback Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SaveRenderTexture()
    {
        if (Context.RaytraceTexture != null)
        {
            Texture2D RaytraceTexture2D = RSDataUtility.RenderTextureToTexture2D(Context.RaytraceTexture, TextureFormat.RGBA32);
            RSAssetUtility.SaveTexture2D(RaytraceTexture2D, "Assets/RaytraceTexture.jpg");
        }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Helper Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    private RSSourceData[] CollectRaySource(RSRenderTask RenderTask)
    {
        Camera MainCamera = Context.MainCamera;

        List<RSSourceData> RaySourceList = new List<RSSourceData>();
        for (int y = 0; y < RenderTask.WindowSize.y; ++y)
        {
            for (int x = 0; x < RenderTask.WindowSize.x; ++x)
            {
                for (int z = 0; z < RenderTask.SubpixelCountPerPixel; ++z)
                {
                    var ScreenPos = new Vector3((RenderTask.WindowPos.x + x) + Random.value, (RenderTask.WindowPos.y + y) + Random.value, 0.0f);
                    var RayDirection = MainCamera.ScreenPointToRay(ScreenPos).direction.normalized;

                    RSSourceData NewRaySource;
                    NewRaySource.ID = new Vector2(ScreenPos.x, ScreenPos.y);
                    NewRaySource.Position = MainCamera.transform.position;
                    NewRaySource.Direction = RayDirection;
                    NewRaySource.Energy = Vector3.one;
                    RaySourceList.Add(NewRaySource);
                }
            }
        }

        return RaySourceList.ToArray();
    }

    protected void ProcessRenderTask(RSRenderTask RenderTask)
    {
        RSDataUtility.ReleaseBuffer(RaytraceOutputBuffer);
        RaytraceOutputBuffer = RSDataUtility.CreateComputeBuffer<RSOutputData>(RenderTask.WindowSize.x * RenderTask.WindowSize.y * RenderTask.SubpixelCountPerPixel, RSOutputData.GetDataSize());

        RSSourceData[] CollectedRaySources = CollectRaySource(RenderTask);
        RSDataUtility.ReleaseBuffer(RaytraceSourceBuffer);
        RaytraceSourceBuffer = RSDataUtility.CreateComputeBufferFromData(CollectedRaySources, RSSourceData.GetDataSize());

        int RaytraceThreadGroupX = Mathf.CeilToInt(RenderTask.WindowSize.x * RenderTask.WindowSize.y * RenderTask.SubpixelCountPerPixel / 1024.0f);
        int RaytraceKernelID = RaytraceComputeShader.FindKernel("CSKernelMain");
        RaytraceComputeShader.SetFloat("RSRandomSeed", Random.value);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RayStormSourceBuffer", RaytraceSourceBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RayStormOutputBuffer", RaytraceOutputBuffer);
        RaytraceComputeShader.Dispatch(RaytraceKernelID, RaytraceThreadGroupX, 1, 1);

        int AdditiveThreadGroupX = Mathf.CeilToInt(RenderTask.WindowSize.x * RenderTask.WindowSize.y / 1024.0f);
        int AdditiveKernelID = AdditiveComputeShader.FindKernel("CSKernelMain");
        AdditiveComputeShader.SetInt("RaytraceWindowPosX", RenderTask.WindowPos.x);
        AdditiveComputeShader.SetInt("RaytraceWindowPosY", RenderTask.WindowPos.y);
        AdditiveComputeShader.SetInt("RaytraceWindowPosZ", RenderTask.FrameID);
        AdditiveComputeShader.SetInt("RaytraceWindowSizeX", RenderTask.WindowSize.x);
        AdditiveComputeShader.SetInt("RaytraceWindowSizeY", RenderTask.WindowSize.y);
        AdditiveComputeShader.SetInt("RaytraceWindowSizeZ", RenderTask.SubpixelCountPerPixel);
        AdditiveComputeShader.SetTexture(AdditiveKernelID, "RaytraceOutputTexture", Context.RaytraceTexture);
        AdditiveComputeShader.SetBuffer(AdditiveKernelID, "RaytraceWindowBuffer", RaytraceOutputBuffer);
        AdditiveComputeShader.Dispatch(AdditiveKernelID, AdditiveThreadGroupX, 1, 1);
    }

    protected void RecompileProgramLibraries()
    {
        var ToolkitLibraryFolder = RSDynamicUtility.GetToolkitLibraryDirectory();

        // Recompile dynamic object program include file
        var ObjectLibraryTemplateFilePath = ToolkitLibraryFolder + "Object/Template";
        var ObjectLibraryIncludeFilePath = ToolkitLibraryFolder + "DynamicObjectInclude.cginc";
        var DynamicCustomizeObjectPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Object/");
        RSDynamicUtility.CompileProgramFileWithTemplate(ObjectLibraryTemplateFilePath, ObjectLibraryIncludeFilePath, DynamicCustomizeObjectPrograms);

        // Recompile dynamic light program include file
        var LightLibraryTemplateFilePath = ToolkitLibraryFolder + "Light/Template";
        var LightLibraryOutputFilePath = ToolkitLibraryFolder + "DynamicLightInclude.cginc";
        var DynamicLightPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Light/");
        RSDynamicUtility.CompileProgramFileWithTemplate(LightLibraryTemplateFilePath, LightLibraryOutputFilePath, DynamicLightPrograms);

        // Recompile dynamic material program include file
        var MaterialLibraryTemplateFilePath = ToolkitLibraryFolder + "Material/Template";
        var MaterialLibraryOutputFilePath = ToolkitLibraryFolder + "DynamicMaterialInclude.cginc";
        var DynamicMaterialPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Material/");
        RSDynamicUtility.CompileProgramFileWithTemplate(MaterialLibraryTemplateFilePath, MaterialLibraryOutputFilePath, DynamicMaterialPrograms);
    }

    protected RSSceneData CollectSceneData()
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

        if (SceneVertexList.Count == 0)
            SceneVertexList.Add(RSVertexData.Default);

        if (SceneGeometryList.Count == 0)
            SceneGeometryList.Add(RSGeometryData.Default);

        if (SceneMaterialList.Count == 0)
            SceneMaterialList.Add(RSMaterialData.Default);

        if (SceneCustomizeObjectList.Count == 0)
            SceneCustomizeObjectList.Add(RSCustomizeObject.Default);

        // Build scene octree data list
        RSVertexData[] SceneVertices = SceneVertexList.ToArray();
        RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
        RSCustomizeObject[] SceneCustomizeObjects = SceneCustomizeObjectList.ToArray();
        RSMaterialData[] SceneMaterials = SceneMaterialList.ToArray();
        RSOctreeData[] SceneOctrees = null;
        int[] SceneOctreeGeometryIDs = null;
        RSSceneUtility.BuildSceneOctree(SceneVertices, SceneGeometries, 7, out SceneOctrees, out SceneOctreeGeometryIDs);

        Cubemap SkyboxCubemap = RSDataUtility.CreateSolidCubemap(32, TextureFormat.ARGB32, false, Color.white);

        RSSceneData SceneData = RSSceneData.Default;
        SceneData.VertexList = SceneVertices;
        SceneData.GeometryList = SceneGeometries;
        SceneData.CustomizeObjectList = SceneCustomizeObjects;
        SceneData.MaterialList = SceneMaterials;
        SceneData.OctreeList = SceneOctrees;
        SceneData.OctreeGeometryIDList = SceneOctreeGeometryIDs;
        SceneData.SkyboxTexture = SkyboxCubemap;
        return SceneData;
    }

}
