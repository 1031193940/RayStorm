using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RayStorm
{
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

                RSRenderTask[] CollectedRenderTaskList;
                RayStormBehaviour.OnFrameCollectRenderTask(Context, out CollectedRenderTaskList);
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

            if (IsDisplayOctree && SceneOctreeBuffer != null && SceneOctreeBuffer.count > 0)
            {
                RSOctreeData[] SceneOctree = new RSOctreeData[SceneOctreeBuffer.count];
                SceneOctreeBuffer.GetData(SceneOctree);

                Gizmos.color = Color.green;
                foreach (var OctreeData in SceneOctree)
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
            RSSceneObject[] CollectedSceneObjects;
            RayStormBehaviour.OnFrameCollectSceneObject(Context, out CollectedSceneObjects);

            // Find Dynamic Programs
            var ToolkitLibraryFolder = RSDynamicUtility.GetToolkitLibraryDirectory();
            var DynamicMaterialPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Material/");

            List<RSVertexData> SceneVertexList = new List<RSVertexData>();
            List<RSGeometryData> SceneGeometryList = new List<RSGeometryData>();
            List<RSMaterialData> SceneMaterialList = new List<RSMaterialData>();
            foreach (RSSceneObject SceneObject in CollectedSceneObjects)
            {
                int[] MeshIndices = SceneObject.Mesh.triangles;
                for (int SubmeshID = 0; SubmeshID < SceneObject.Mesh.subMeshCount; ++SubmeshID)
                {
                    SubMeshDescriptor SubmeshDescriptor = SceneObject.Mesh.GetSubMesh(SubmeshID);
                    for (int SubmeshIndiceID = 0; SubmeshIndiceID < SubmeshDescriptor.indexCount; SubmeshIndiceID += 3)
                    {
                        RSGeometryData Geometry = RSGeometryData.Default;
                        Geometry.MaterialID = SceneMaterialList.Count;
                        Geometry.Indice0 = MeshIndices[SubmeshDescriptor.indexStart + SubmeshIndiceID + 0] + SceneVertexList.Count;
                        Geometry.Indice1 = MeshIndices[SubmeshDescriptor.indexStart + SubmeshIndiceID + 1] + SceneVertexList.Count;
                        Geometry.Indice2 = MeshIndices[SubmeshDescriptor.indexStart + SubmeshIndiceID + 2] + SceneVertexList.Count;
                        SceneGeometryList.Add(Geometry);
                    }

                    Material UnityMaterial = SceneObject.Renderer.sharedMaterials[SubmeshID];
                    if (UnityMaterial != null)
                    {
                        string MeshMaterialNameLower = UnityMaterial.name.ToLower();
                        string MeshMaterialShaderNameLower = UnityMaterial.shader.name.ToLower();
                        int MeshMaterialProgramID = System.Array.FindIndex(DynamicMaterialPrograms, ProgramData =>
                        {
                            string ProgramNameLower = ProgramData.Name.ToLower();
                            return MeshMaterialNameLower.Contains(ProgramNameLower) || MeshMaterialShaderNameLower.Contains(ProgramNameLower);
                        });

                        RSMaterialData MeshMaterial = RSMaterialData.CreateFromUnityMaterial(UnityMaterial);
                        MeshMaterial.ProgramID = MeshMaterialProgramID;
                        SceneMaterialList.Add(MeshMaterial);
                    }
                    else
                    {
                        SceneMaterialList.Add(RSMaterialData.Default);
                    }

                    Vector3[] MeshVertices = SceneObject.Mesh.vertices;
                    Vector3[] MeshNormals = SceneObject.Mesh.normals;
                    Vector4[] MeshTangents = SceneObject.Mesh.tangents;
                    Vector2[] MeshTexcoord0 = SceneObject.Mesh.uv;
                    Vector2[] MeshTexcoord1 = SceneObject.Mesh.uv2;
                    for (int VertexID = 0; VertexID < MeshVertices.Length; ++VertexID)
                    {
                        RSVertexData MeshVertex = RSVertexData.Default;
                        MeshVertex.Position = SceneObject.Renderer.transform.TransformPoint(MeshVertices[VertexID]);
                        MeshVertex.Normal = SceneObject.Renderer.transform.TransformVector(MeshNormals[VertexID]);
                        MeshVertex.Tangent = SceneObject.Renderer.transform.TransformVector(MeshTangents[VertexID]);
                        MeshVertex.UV0 = MeshTexcoord0[VertexID];
                        MeshVertex.UV1 = MeshTexcoord1[VertexID];
                        SceneVertexList.Add(MeshVertex);
                    }
                }
            }

            RSVertexData[] SceneVertices = SceneVertexList.ToArray();
            if (RSDataUtility.IsNullOrEmpty(SceneVertices))
                SceneVertices = new RSVertexData[] { RSVertexData.Default };

            RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
            if (RSDataUtility.IsNullOrEmpty(SceneGeometries))
                SceneGeometries = new RSGeometryData[] { RSGeometryData.Default };

            RSMaterialData[] SceneMaterials = SceneMaterialList.ToArray();
            if (RSDataUtility.IsNullOrEmpty(SceneMaterials))
                SceneMaterials = new RSMaterialData[] { RSMaterialData.Default };

            RSOctreeData[] SceneOctrees;
            int[] SceneOctreeGeometryIDs;
            RSSceneUtility.BuildSceneOctree(SceneVertices, SceneGeometries, 7, out SceneOctrees, out SceneOctreeGeometryIDs);

            ReleaseRenderBuffers();

            SceneVertexBuffer = RSDataUtility.CreateComputeBufferFromData(SceneVertices, RSVertexData.GetDataSize());
            SceneGeometryBuffer = RSDataUtility.CreateComputeBufferFromData(SceneGeometries, RSGeometryData.GetDataSize());
            SceneMaterialBuffer = RSDataUtility.CreateComputeBufferFromData(SceneMaterials, RSMaterialData.GetDataSize());
            SceneOctreeBuffer = RSDataUtility.CreateComputeBufferFromData(SceneOctrees, RSOctreeData.GetDataSize());
            SceneOctreeGeometryIDBuffer = RSDataUtility.CreateComputeBufferFromData(SceneOctreeGeometryIDs, sizeof(int));

            if (SkyboxTexture == null)
                SkyboxTexture = RSDataUtility.CreateSolidCubemap(32, TextureFormat.ARGB32, false, Color.white);
        }

        private void ResetFrameRasterizer()
        {
            RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);

            int RaytraceKernelID = RaytraceComputeShader.FindKernel("CSKernelMain");
            RaytraceComputeShader.SetFloat("RSRandomSeed", Random.value);
            RaytraceComputeShader.SetTexture(RaytraceKernelID, "RSSkyboxTexture", SkyboxTexture);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneVertexBuffer", SceneVertexBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneGeometryBuffer", SceneGeometryBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneMaterialBuffer", SceneMaterialBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeBuffer", SceneOctreeBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeGeometryIDBuffer", SceneOctreeGeometryIDBuffer);
        }
    }
}