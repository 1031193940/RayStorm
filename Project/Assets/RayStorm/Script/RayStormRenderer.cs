using System;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class RayStormRenderer : MonoBehaviour
    {
        public bool IsDisplayOctree;
        public Texture SkyboxTexture;
        public RayStormBehaviour RayStormBehaviour;

        [SerializeField] private ComputeShader RaytraceComputeShader;
        [SerializeField] private ComputeShader AdditiveComputeShader;
        [SerializeField] private ComputeShader BlockComputeShader;
        [SerializeField, Min(1)] private int MaxTasksPerUpdate = 8;
        [SerializeField, Min(0.1f)] private float SubmissionBudgetMilliseconds = 4.0f;
        [SerializeField, Min(1)] private int MaximumQueuedRays = 524288;
        [SerializeField, Min(0.0f)] private float DisplayRefreshInterval = 0.1f;

        private RSRenderContext Context;
        private ComputeBuffer SceneVertexBuffer;
        private ComputeBuffer SceneGeometryBuffer;
        private ComputeBuffer SceneMaterialBuffer;
        private ComputeBuffer SceneOctreeBuffer;
        private ComputeBuffer SceneOctreeGeometryIDBuffer;
        private ComputeBuffer RaytraceSourceBuffer;
        private ComputeBuffer RaytraceOutputBuffer;
        private RenderTexture DisplayTexture;
        private Cubemap OwnedSkybox;
        private bool OwnsBehaviour;
        private bool FramePrepared;
        private RSSourceData[] RaySources = Array.Empty<RSSourceData>();
        private RSOctreeData[] SceneOctrees = Array.Empty<RSOctreeData>();
        private int SceneGeometryCount;
        private int RaytraceKernelID;
        private int AdditiveKernelID;
        private int BlockKernelID;
        private uint RaytraceThreadCount;
        private uint AdditiveThreadCount;
        private uint BlockThreadCountX;
        private uint BlockThreadCountY;
        private Matrix4x4 FrameCameraToWorld;
        private Matrix4x4 FrameProjection;
        private int FrameCullingMask;
        private Vector3 FrameCameraPosition;
        private Vector3 FrameCameraForward;
        private Vector3 NearPlaneBottomLeft;
        private Vector3 NearPlaneHorizontal;
        private Vector3 NearPlaneVertical;
        private bool FrameOrthographic;
        private float LastDisplayTime = float.NegativeInfinity;
        private readonly System.Diagnostics.Stopwatch SubmissionTimer = new System.Diagnostics.Stopwatch();

        private void Reset() { AssignEditorShaders(); }

        private void OnValidate()
        {
            MaxTasksPerUpdate = Mathf.Max(1, MaxTasksPerUpdate);
            MaximumQueuedRays = Mathf.Max(1, MaximumQueuedRays);
            SubmissionBudgetMilliseconds = Mathf.Max(0.1f, SubmissionBudgetMilliseconds);
            DisplayRefreshInterval = Mathf.Max(0, DisplayRefreshInterval);
            AssignEditorShaders();
        }

        private void AssignEditorShaders()
        {
#if UNITY_EDITOR
            if (RaytraceComputeShader == null)
                RaytraceComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormRaytracer");
            if (AdditiveComputeShader == null)
                AdditiveComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormAdditive");
            if (BlockComputeShader == null)
                BlockComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormBlock");
#endif
        }

        private void Awake()
        {
            if (RayStormBehaviour == null)
            {
                RayStormBehaviour = ScriptableObject.CreateInstance<RSDefaultBehaviour>();
                OwnsBehaviour = true;
            }
        }

        private void Start()
        {
            try
            {
                if (!SystemInfo.supportsComputeShaders ||
                    !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
                    !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                    throw new NotSupportedException("RayStorm requires compute shaders and writable ARGBFloat render textures.");

                AssignEditorShaders();
                if (RaytraceComputeShader == null || AdditiveComputeShader == null || BlockComputeShader == null)
                    throw new InvalidOperationException("Assign all three RayStorm compute shaders before building or rendering.");

                RaytraceKernelID = RaytraceComputeShader.FindKernel("CSKernelMain");
                AdditiveKernelID = AdditiveComputeShader.FindKernel("CSKernelMain");
                BlockKernelID = BlockComputeShader.FindKernel("CSKernelMain");
                RaytraceComputeShader.GetKernelThreadGroupSizes(RaytraceKernelID, out RaytraceThreadCount, out _, out _);
                AdditiveComputeShader.GetKernelThreadGroupSizes(AdditiveKernelID, out AdditiveThreadCount, out _, out _);
                BlockComputeShader.GetKernelThreadGroupSizes(BlockKernelID, out BlockThreadCountX, out BlockThreadCountY, out _);

                Context = new RSRenderContext
                {
                    MainCamera = GetComponent<Camera>(),
                    RenderTaskQueue = new Queue<RSRenderTask>()
                };
                EnsureRenderTextures();
                RayStormBehaviour.OnLoad(Context);
                RayStormBehaviour.OnStartup(Context);
            }
            catch (Exception Error) { StopWithError(Error); }
        }

        private void Update()
        {
            if (Context == null || !Context.IsRunning)
                return;
            try
            {
                if (FramePrepared && CameraChanged())
                {
                    Context.RenderTaskQueue.Clear();
                    FramePrepared = false;
                }
                if (!FramePrepared)
                    BeginFrame();

                RayStormBehaviour.OnFrameUpdate(Context);
                if (CameraChanged())
                {
                    Context.RenderTaskQueue.Clear();
                    FramePrepared = false;
                    return;
                }

                SubmissionTimer.Restart();
                int SubmittedTaskCount = 0;
                long SubmittedRayCount = 0;
                RSRenderTask LastTask = null;
                while (Context.RenderTaskQueue.Count > 0 && SubmittedTaskCount < MaxTasksPerUpdate)
                {
                    RSRenderTask NextTask = Context.RenderTaskQueue.Peek();
                    long NextRayCount = (long)NextTask.WindowSize.x * NextTask.WindowSize.y * NextTask.SubpixelCountPerPixel;
                    if (SubmittedTaskCount > 0 &&
                        (SubmissionTimer.Elapsed.TotalMilliseconds >= SubmissionBudgetMilliseconds ||
                         SubmittedRayCount + NextRayCount > MaximumQueuedRays))
                        break;
                    LastTask = Context.RenderTaskQueue.Dequeue();
                    ProcessRenderTask(LastTask);
                    SubmittedTaskCount++;
                    SubmittedRayCount += NextRayCount;
                }

                if (Context.RenderTaskQueue.Count == 0)
                {
                    RayStormBehaviour.OnFramePostProcess(Context);
                    RefreshDisplay(null);
                    RayStormBehaviour.OnFrameFinished(Context);
                    Context.FrameID++;
                    FramePrepared = false;
                    if (RayStormBehaviour.FrameCount > 0 && Context.FrameID >= RayStormBehaviour.FrameCount)
                    {
                        Context.IsRunning = false;
                        RayStormBehaviour.OnFinished(Context);
                    }
                }
                else if (LastTask != null && Time.realtimeSinceStartup - LastDisplayTime >= DisplayRefreshInterval)
                    RefreshDisplay(LastTask);
            }
            catch (Exception Error) { StopWithError(Error); }
        }

        private void BeginFrame()
        {
            RayStormBehaviour.OnFrameStartup(Context);
            EnsureRenderTextures();
            CaptureCamera();
            ReloadFrameScene();
            RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);
            RayStormBehaviour.OnFrameCollectRenderTask(Context, out RSRenderTask[] RenderTasks);
            if (RenderTasks == null)
                throw new InvalidOperationException("The render behaviour returned a null task list.");
            foreach (RSRenderTask Task in RenderTasks)
            {
                if (Task == null || Task.SubpixelCountPerPixel <= 0 || Task.WindowPos.x < 0 || Task.WindowPos.y < 0)
                    throw new InvalidOperationException("RayStorm received an invalid render task.");
                Task.WindowSize = new Vector2Int(
                    Mathf.Min(Task.WindowSize.x, Context.RenderWidth - Task.WindowPos.x),
                    Mathf.Min(Task.WindowSize.y, Context.RenderHeight - Task.WindowPos.y));
                if (Task.WindowSize.x > 0 && Task.WindowSize.y > 0)
                    Context.RenderTaskQueue.Enqueue(Task);
            }
            FramePrepared = true;
        }

        private void CaptureCamera()
        {
            Camera Camera = Context.MainCamera;
            FrameCameraToWorld = Camera.cameraToWorldMatrix;
            FrameProjection = Camera.projectionMatrix;
            FrameCullingMask = Camera.cullingMask;
            FrameCameraPosition = Camera.transform.position;
            FrameCameraForward = Camera.transform.forward;
            FrameOrthographic = Camera.orthographic;
            NearPlaneBottomLeft = Camera.ViewportToWorldPoint(new Vector3(0, 0, Camera.nearClipPlane));
            NearPlaneHorizontal = Camera.ViewportToWorldPoint(new Vector3(1, 0, Camera.nearClipPlane)) - NearPlaneBottomLeft;
            NearPlaneVertical = Camera.ViewportToWorldPoint(new Vector3(0, 1, Camera.nearClipPlane)) - NearPlaneBottomLeft;
        }

        private bool CameraChanged()
        {
            Camera Camera = Context.MainCamera;
            return Camera.pixelWidth != Context.RenderWidth || Camera.pixelHeight != Context.RenderHeight ||
                   Camera.cameraToWorldMatrix != FrameCameraToWorld || Camera.projectionMatrix != FrameProjection ||
                   Camera.cullingMask != FrameCullingMask || Camera.orthographic != FrameOrthographic;
        }

        private void EnsureRenderTextures()
        {
            int Width = Mathf.Max(1, Context.MainCamera.pixelWidth);
            int Height = Mathf.Max(1, Context.MainCamera.pixelHeight);
            if (Context.RaytraceTexture != null && Context.RenderWidth == Width && Context.RenderHeight == Height)
                return;
            ReleaseTexture(Context.RaytraceTexture);
            ReleaseTexture(DisplayTexture);
            Context.RenderWidth = Width;
            Context.RenderHeight = Height;
            Context.RaytraceTexture = CreateRenderTexture(Width, Height, "RayStorm Accumulation");
            DisplayTexture = CreateRenderTexture(Width, Height, "RayStorm Display");
            RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);
            RSDataUtility.ClearRenderTexture(DisplayTexture, Color.black);
        }

        private static RenderTexture CreateRenderTexture(int Width, int Height, string TextureName)
        {
            RenderTexture Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = TextureName, useMipMap = false, enableRandomWrite = true, filterMode = FilterMode.Point
            };
            if (!Texture.Create())
            {
                DestroyOwnedObject(Texture);
                throw new InvalidOperationException("Could not allocate the RayStorm render texture.");
            }
            return Texture;
        }

        private int CollectRaySources(RSRenderTask Task)
        {
            int RayCount = checked(Task.WindowSize.x * Task.WindowSize.y * Task.SubpixelCountPerPixel);
            if (RaySources.Length < RayCount)
                RaySources = new RSSourceData[RayCount];
            int RayNumber = 0;
            for (int PixelY = 0; PixelY < Task.WindowSize.y; PixelY++)
            for (int PixelX = 0; PixelX < Task.WindowSize.x; PixelX++)
            for (int Sample = 0; Sample < Task.SubpixelCountPerPixel; Sample++)
            {
                float ScreenX = Task.WindowPos.x + PixelX + UnityEngine.Random.value;
                float ScreenY = Task.WindowPos.y + PixelY + UnityEngine.Random.value;
                Vector3 Origin = NearPlaneBottomLeft + NearPlaneHorizontal * (ScreenX / Context.RenderWidth)
                    + NearPlaneVertical * (ScreenY / Context.RenderHeight);
                RaySources[RayNumber++] = new RSSourceData
                {
                    ID = new Vector2(ScreenX, ScreenY), Position = Origin,
                    Direction = FrameOrthographic ? FrameCameraForward : (Origin - FrameCameraPosition).normalized,
                    Energy = Vector3.one
                };
            }
            return RayCount;
        }

        private static void EnsureBuffer(ref ComputeBuffer Buffer, int Count, int Stride)
        {
            Count = Mathf.Max(1, Count);
            if (Buffer != null && Buffer.IsValid() && Buffer.count >= Count && Buffer.stride == Stride)
                return;
            ReleaseBuffer(ref Buffer);
            Buffer = new ComputeBuffer(Count, Stride);
        }

        private static void UploadBuffer<T>(ref ComputeBuffer Buffer, T[] Data, int Stride) where T : struct
        {
            EnsureBuffer(ref Buffer, Data.Length, Stride);
            if (Data.Length > 0)
                Buffer.SetData(Data, 0, 0, Data.Length);
            else
                Buffer.SetData(new T[1]);
        }

        private void ProcessRenderTask(RSRenderTask Task)
        {
            int RayCount = CollectRaySources(Task);
            EnsureBuffer(ref RaytraceSourceBuffer, RayCount, RSSourceData.GetDataSize());
            EnsureBuffer(ref RaytraceOutputBuffer, RayCount, RSOutputData.GetDataSize());
            RaytraceSourceBuffer.SetData(RaySources, 0, 0, RayCount);

            // Assets are shared: bind every instance-specific resource immediately before dispatch.
            RaytraceComputeShader.SetInt("RayCount", RayCount);
            RaytraceComputeShader.SetInt("SceneOctreeCount", SceneOctrees.Length);
            RaytraceComputeShader.SetInt("SceneGeometryCount", SceneGeometryCount);
            RaytraceComputeShader.SetInt("RSRandomSeed", UnityEngine.Random.Range(1, int.MaxValue));
            RaytraceComputeShader.SetTexture(RaytraceKernelID, "RSSkyboxTexture", GetSkybox());
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneVertexBuffer", SceneVertexBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneGeometryBuffer", SceneGeometryBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneMaterialBuffer", SceneMaterialBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeBuffer", SceneOctreeBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneOctreeGeometryIDBuffer", SceneOctreeGeometryIDBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RayStormSourceBuffer", RaytraceSourceBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RayStormOutputBuffer", RaytraceOutputBuffer);
            RaytraceComputeShader.Dispatch(RaytraceKernelID, DivideRoundUp(RayCount, RaytraceThreadCount), 1, 1);

            AdditiveComputeShader.SetInt("RaytraceWindowPosX", Task.WindowPos.x);
            AdditiveComputeShader.SetInt("RaytraceWindowPosY", Task.WindowPos.y);
            AdditiveComputeShader.SetInt("RaytraceWindowSizeX", Task.WindowSize.x);
            AdditiveComputeShader.SetInt("RaytraceWindowSizeY", Task.WindowSize.y);
            AdditiveComputeShader.SetInt("SubpixelCountPerPixel", Task.SubpixelCountPerPixel);
            AdditiveComputeShader.SetTexture(AdditiveKernelID, "RaytraceOutputTexture", Context.RaytraceTexture);
            AdditiveComputeShader.SetBuffer(AdditiveKernelID, "RaytraceWindowBuffer", RaytraceOutputBuffer);
            AdditiveComputeShader.Dispatch(AdditiveKernelID, DivideRoundUp(Task.WindowSize.x * Task.WindowSize.y, AdditiveThreadCount), 1, 1);
        }

        private Texture GetSkybox()
        {
            if (SkyboxTexture is Cubemap)
                return SkyboxTexture;
            if (OwnedSkybox == null)
                OwnedSkybox = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBA32, false, Color.black);
            return OwnedSkybox;
        }

        private void RefreshDisplay(RSRenderTask Task)
        {
            BlockComputeShader.SetInt("ShowRenderBlock", Task == null ? 0 : 1);
            BlockComputeShader.SetInt("OutputIsGamma", QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0);
            BlockComputeShader.SetInt("BlockPosX", Task == null ? 0 : Task.WindowPos.x);
            BlockComputeShader.SetInt("BlockPosY", Task == null ? 0 : Task.WindowPos.y);
            BlockComputeShader.SetInt("BlockSizeX", Task == null ? 0 : Task.WindowSize.x);
            BlockComputeShader.SetInt("BlockSizeY", Task == null ? 0 : Task.WindowSize.y);
            BlockComputeShader.SetVector("BlockColor", new Vector4(0, 1, 1, 0.2f));
            BlockComputeShader.SetTexture(BlockKernelID, "Source", Context.RaytraceTexture);
            BlockComputeShader.SetTexture(BlockKernelID, "Target", DisplayTexture);
            BlockComputeShader.Dispatch(BlockKernelID, DivideRoundUp(Context.RenderWidth, BlockThreadCountX),
                DivideRoundUp(Context.RenderHeight, BlockThreadCountY), 1);
            LastDisplayTime = Time.realtimeSinceStartup;
        }

        private static int DivideRoundUp(int Count, uint GroupSize)
        {
            return (int)(((long)Count + GroupSize - 1) / GroupSize);
        }

        private void ReloadFrameScene()
        {
            RayStormBehaviour.OnFrameCollectSceneObject(Context, out RSSceneObject[] Objects);
            List<RSVertexData> Vertices = new List<RSVertexData>();
            List<RSGeometryData> Geometries = new List<RSGeometryData>();
            List<RSMaterialData> Materials = new List<RSMaterialData>();
            try
            {
                foreach (RSSceneObject SceneObject in Objects ?? Array.Empty<RSSceneObject>())
                {
                    if (SceneObject == null || SceneObject.Mesh == null || SceneObject.Renderer == null)
                        continue;
                    Mesh Mesh = SceneObject.Mesh;
                    Matrix4x4 LocalToWorld = SceneObject.Renderer.localToWorldMatrix;
                    if (Mathf.Abs(LocalToWorld.determinant) < 1e-12f)
                    {
                        Debug.LogWarning("RayStorm skipped a mesh with a singular transform: " + SceneObject.Renderer.name, SceneObject.Renderer);
                        continue;
                    }
                    Matrix4x4 NormalMatrix = LocalToWorld.inverse.transpose;
                    Vector3[] Positions = Mesh.vertices;
                    Vector3[] Normals = Mesh.normals;
                    Vector4[] Tangents = Mesh.tangents;
                    Vector2[] UV0 = Mesh.uv;
                    Vector2[] UV1 = Mesh.uv2;
                    Material[] UnityMaterials = SceneObject.Renderer.sharedMaterials;
                    int VertexStart = Vertices.Count;
                    // Append vertices only once; all submeshes share this base.
                    for (int VertexNumber = 0; VertexNumber < Positions.Length; VertexNumber++)
                    {
                        RSVertexData Vertex = RSVertexData.Default;
                        Vertex.Position = LocalToWorld.MultiplyPoint3x4(Positions[VertexNumber]);
                        Vertex.Normal = NormalMatrix.MultiplyVector(Normals.Length == Positions.Length ? Normals[VertexNumber] : Vector3.up).normalized;
                        if (Tangents.Length == Positions.Length)
                        {
                            Vector4 SourceTangent = Tangents[VertexNumber];
                            Vector3 TangentDirection = LocalToWorld.MultiplyVector(SourceTangent).normalized;
                            Vertex.Tangent = new Vector4(TangentDirection.x, TangentDirection.y, TangentDirection.z,
                                SourceTangent.w * Mathf.Sign(LocalToWorld.determinant));
                        }
                        Vertex.UV0 = UV0.Length == Positions.Length ? UV0[VertexNumber] : Vector2.zero;
                        Vertex.UV1 = UV1.Length == Positions.Length ? UV1[VertexNumber] : Vector2.zero;
                        Vertices.Add(Vertex);
                    }
                    for (int Submesh = 0; Submesh < Mesh.subMeshCount; Submesh++)
                    {
                        if (Mesh.GetTopology(Submesh) != MeshTopology.Triangles)
                            continue;
                        int MaterialID = Materials.Count;
                        Materials.Add(RSMaterialData.CreateFromUnityMaterial(Submesh < UnityMaterials.Length ? UnityMaterials[Submesh] : null));
                        int[] TriangleIndices = Mesh.GetTriangles(Submesh);
                        for (int TriangleStart = 0; TriangleStart + 2 < TriangleIndices.Length; TriangleStart += 3)
                            Geometries.Add(new RSGeometryData
                            {
                                MaterialID = MaterialID,
                                Indice0 = VertexStart + TriangleIndices[TriangleStart],
                                Indice1 = VertexStart + TriangleIndices[TriangleStart + 1],
                                Indice2 = VertexStart + TriangleIndices[TriangleStart + 2]
                            });
                    }
                }
            }
            finally { RSSceneUtility.ReleaseSceneObjects(Objects); }

            RSVertexData[] VertexData = Vertices.ToArray();
            RSGeometryData[] GeometryData = Geometries.ToArray();
            SceneGeometryCount = GeometryData.Length;
            RSSceneUtility.BuildSceneOctree(VertexData, GeometryData, 7, out SceneOctrees, out int[] OctreeGeometryIDs);
            UploadBuffer(ref SceneVertexBuffer, VertexData, RSVertexData.GetDataSize());
            UploadBuffer(ref SceneGeometryBuffer, GeometryData, RSGeometryData.GetDataSize());
            UploadBuffer(ref SceneMaterialBuffer, Materials.ToArray(), RSMaterialData.GetDataSize());
            UploadBuffer(ref SceneOctreeBuffer, SceneOctrees, RSOctreeData.GetDataSize());
            UploadBuffer(ref SceneOctreeGeometryIDBuffer, OctreeGeometryIDs, sizeof(int));
        }

        private void OnRenderImage(RenderTexture Source, RenderTexture Destination)
        {
            Graphics.Blit(DisplayTexture != null ? DisplayTexture : Source, Destination);
        }

        public void SaveRenderTexture()
        {
            if (Context == null || Context.RaytraceTexture == null)
                return;
            Texture2D Image = RSDataUtility.RenderTextureToTexture2D(Context.RaytraceTexture, TextureFormat.RGBA32);
            try { RSAssetUtility.SaveTexture2D(Image, RSAssetUtility.GetOutputPath("RaytraceTexture.jpg")); }
            finally { DestroyOwnedObject(Image); }
        }

        private void OnDrawGizmos()
        {
            if (RayStormBehaviour != null)
                RayStormBehaviour.OnDrawGizmos();
            if (!IsDisplayOctree)
                return;
            Gizmos.color = Color.green;
            foreach (RSOctreeData Node in SceneOctrees)
                if (Node.SubtreeCount == 0)
                    Gizmos.DrawWireCube(Node.Center, Node.Size);
        }

        private void StopWithError(Exception Error)
        {
            if (Context != null) Context.IsRunning = false;
            enabled = false;
            Debug.LogException(Error, this);
        }

        private static void ReleaseBuffer(ref ComputeBuffer Buffer)
        {
            if (Buffer != null) Buffer.Release();
            Buffer = null;
        }

        private static void ReleaseTexture(RenderTexture Texture)
        {
            if (Texture == null) return;
            Texture.Release();
            DestroyOwnedObject(Texture);
        }

        private static void DestroyOwnedObject(UnityEngine.Object Value)
        {
            if (Value == null) return;
            if (Application.isPlaying) Destroy(Value);
            else DestroyImmediate(Value);
        }

        private void OnDestroy()
        {
            try
            {
                if (Context != null && RayStormBehaviour != null)
                    RayStormBehaviour.OnUnload(Context);
            }
            finally
            {
                ReleaseBuffer(ref SceneVertexBuffer);
                ReleaseBuffer(ref SceneGeometryBuffer);
                ReleaseBuffer(ref SceneMaterialBuffer);
                ReleaseBuffer(ref SceneOctreeBuffer);
                ReleaseBuffer(ref SceneOctreeGeometryIDBuffer);
                ReleaseBuffer(ref RaytraceSourceBuffer);
                ReleaseBuffer(ref RaytraceOutputBuffer);
                ReleaseTexture(Context == null ? null : Context.RaytraceTexture);
                ReleaseTexture(DisplayTexture);
                DestroyOwnedObject(OwnedSkybox);
                if (OwnsBehaviour) DestroyOwnedObject(RayStormBehaviour);
                Context = null;
                DisplayTexture = null;
            }
        }
    }
}
