using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RayStormRenderer : MonoBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Variables | 
    ////////////////////////////////////////////////////////////////////////////////////////////////////

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
    }

    public void Start()
    {
        RecompileProgramLibraries();

        InitializeRenderContext();

        InitializeRenderShaders();

        RayStormBehaviour.OnLoad(Context);

        RayStormBehaviour.OnStartup(Context);
    }

    public void Update()
    {
        if (!Context.IsRunning)
            return;

        if (Context.RenderTaskQueue.Count == 0)
        {
            RayStormBehaviour.OnFrameStartup(Context);

            ReloadFrameScene();

            ResetFrameRasterizer();

            RSRenderTask[] CollectedRenderTaskList = RayStormBehaviour.OnFrameCollectRenderTask(Context);
            foreach (var RenderTask in CollectedRenderTaskList)
                Context.RenderTaskQueue.Enqueue(RenderTask);
        }
        else
        {
            RayStormBehaviour.OnFrameUpdate(Context);

            RSRenderTask RenderTask = Context.RenderTaskQueue.Dequeue();
            ProcessRenderTask(RenderTask);

            if (Context.RenderTaskQueue.Count == 0)
            {
                RayStormBehaviour.OnFramePostProcess(Context);
            }
        }
    }

    public void OnRenderImage(RenderTexture Source, RenderTexture Destination)
    {
        Graphics.Blit(Context.RaytraceTexture, Destination);
    }

    public void LateUpdate()
    {
        if (!Context.IsRunning)
            return;

        if (Context.RenderTaskQueue.Count == 0)
        {
            RayStormBehaviour.OnFrameFinished(Context);

            Context.FrameID++;
            if (Context.FrameID == RayStormBehaviour.FrameCount)
            {
                RayStormBehaviour.OnFinished(Context);
                Context.IsRunning = false;
            }
        }
    }

    public void OnDrawGizmos()
    {
        if (RayStormBehaviour != null)
            RayStormBehaviour.OnDrawGizmos();

        if (IsDisplayOctree && Context != null && Context.SceneData.OctreeList != null)
        {
            Gizmos.color = Color.green;
            foreach (var OctreeData in Context.SceneData.OctreeList)
            {
                if (OctreeData.SubtreeCount == 0)
                    Gizmos.DrawWireCube(OctreeData.Center, OctreeData.Size);
            }
        }
    }

    public void OnDestroy()
    {
        RayStormBehaviour.OnUnload(Context);

        ReleaseRenderBuffers();
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

    private void ProcessRenderTask(RSRenderTask RenderTask)
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

    private void RecompileProgramLibraries()
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

    private void InitializeRenderContext()
    {
        Camera MainCamera = GetComponent<Camera>();

        int TextureWidth = MainCamera.pixelWidth;
        int TextureHeight = MainCamera.pixelHeight;
        RenderTexture RaytraceTexture = new RenderTexture(TextureWidth, TextureHeight, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        RaytraceTexture.useMipMap = false;
        RaytraceTexture.enableRandomWrite = true;
        RaytraceTexture.filterMode = FilterMode.Point;
        RaytraceTexture.Create();

        Context = new RSRenderContext();
        Context.FrameID = 0;
        Context.MainCamera = MainCamera;
        Context.RenderTaskQueue = new Queue<RSRenderTask>();
        Context.RaytraceTexture = RaytraceTexture;
    }

    private void ReleaseRenderBuffers()
    {
        RSDataUtility.ReleaseBuffer(SceneVertexBuffer);
        RSDataUtility.ReleaseBuffer(SceneGeometryBuffer);
        RSDataUtility.ReleaseBuffer(SceneCustomizeObjectBuffer);
        RSDataUtility.ReleaseBuffer(SceneMaterialBuffer);
        RSDataUtility.ReleaseBuffer(SceneOctreeBuffer);
        RSDataUtility.ReleaseBuffer(SceneOctreeGeometryIDBuffer);
        RSDataUtility.ReleaseBuffer(RaytraceSourceBuffer);
        RSDataUtility.ReleaseBuffer(RaytraceOutputBuffer);
    }

    private void InitializeRenderShaders()
    {
        // Load raytrace compute shader and setup constant uniform variables
        RaytraceComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormRaytracer");

        // Load additive compute shader and setup constant uniform variables
        AdditiveComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormAdditive");
    }

    private void ReloadFrameScene()
    {
        if (SceneVertexBuffer != null)
            return;

        RayStormBehaviour.OnFrameCollectSceneObject(Context, out Context.SceneData);

        RSSceneUtility.BuildSceneOctree(Context.SceneData.VertexList, Context.SceneData.GeometryList, 7,
                out Context.SceneData.OctreeList, out Context.SceneData.OctreeGeometryIDList);

        ReleaseRenderBuffers();

        SkyboxTexture = (SkyboxTexture == null ? Context.SceneData.SkyboxTexture : SkyboxTexture);
        SceneVertexBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.VertexList, RSVertexData.GetDataSize());
        SceneGeometryBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.GeometryList, RSGeometryData.GetDataSize());
        SceneCustomizeObjectBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.CustomizeObjectList, RSCustomizeObject.GetDataSize());
        SceneMaterialBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.MaterialList, RSMaterialData.GetDataSize());
        SceneOctreeBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.OctreeList, RSOctreeData.GetDataSize());
        SceneOctreeGeometryIDBuffer = RSDataUtility.CreateComputeBufferFromData(Context.SceneData.OctreeGeometryIDList, sizeof(int));
    }

    private void ResetFrameRasterizer()
    {
        RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);

        int RaytraceKernelID = RaytraceComputeShader.FindKernel("CSKernelMain");
        RaytraceComputeShader.SetFloat("RSRandomSeed", Random.value);
        RaytraceComputeShader.SetTexture(RaytraceKernelID, "RSSkyboxTexture", SkyboxTexture);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneVertexBuffer", SceneVertexBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneGeometryBuffer", SceneGeometryBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneObjectBuffer", SceneCustomizeObjectBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneMaterialBuffer", SceneMaterialBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeBuffer", SceneOctreeBuffer);
        RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeGeometryIDBuffer", SceneOctreeGeometryIDBuffer);
    }
}
