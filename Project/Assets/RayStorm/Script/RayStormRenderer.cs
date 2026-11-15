using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Serialization;

namespace RayStorm
{
    public enum RSEnvironmentColorSpace
    {
        UnityImportedColor = 0,
        LinearRadiance = 1
    }

    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class RayStormRenderer : MonoBehaviour
    {
        [FormerlySerializedAs("IsDisplayOctree")] public bool IsDisplayBVH;
        public bool IsDisplayRenderBlock = true;
        public bool IsDisplayRenderProgress = true;
        [Tooltip("Denoise completed frames with Open Image Denoise using the linear HDR color image.")] public bool IsDenoisingEnabled = true;
        public Texture SkyboxTexture;
        public RSEnvironmentColorSpace EnvironmentColorSpace = RSEnvironmentColorSpace.UnityImportedColor;
        public RayStormBehaviour RayStormBehaviour;

        [SerializeField] private ComputeShader RaytraceComputeShader;
        [SerializeField] private ComputeShader AdditiveComputeShader;
        [SerializeField] private ComputeShader BlockComputeShader;
        [SerializeField] private ComputeShader EnvironmentComputeShader;
        [SerializeField, Min(1)] private int MaxTasksPerUpdate = 8;
        [SerializeField, Min(0.1f)] private float SubmissionBudgetMilliseconds = 4.0f;
        [SerializeField, Min(1)] private int MaximumQueuedRays = 524288;
        [SerializeField, Min(0.0f)] private float DisplayRefreshInterval = 0.1f;

        private RSRenderContext Context;
        private ComputeBuffer SceneVertexBuffer;
        private ComputeBuffer SceneGeometryBuffer;
        private ComputeBuffer SceneMaterialBuffer;
        private ComputeBuffer SceneBVHBuffer;
        private ComputeBuffer SceneBVHGeometryIDBuffer;
        private ComputeBuffer RaytraceOutputBuffer;
        private RenderTexture DisplayTexture;
        private RenderTexture RaytraceStatistics;
        private Cubemap OwnedSkybox;
        private bool OwnsBehaviour;
        private bool FramePrepared;
        private RSVertexData[] SceneVertices;
        private RSGeometryData[] SceneGeometries;
        private RSMaterialData[] SceneMaterials;
        private RSBVHData[] SceneBVHs = Array.Empty<RSBVHData>();
        private readonly RSLightSampler SceneLightSampler = new RSLightSampler();
        private RSLightData[] FrameLightData;
        private bool SceneLightsDirty = true;
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
        private Texture FrameSkyboxTexture;
        private uint FrameSkyboxUpdateCount;
        private RSEnvironmentColorSpace FrameEnvironmentColorSpace;
        private int FrameRandomSeed;
        private long TotalSampleCount;
        private long SubmittedSampleCount;
        private float LastDisplayTime = float.NegativeInfinity;
        private readonly System.Diagnostics.Stopwatch SubmissionTimer = new System.Diagnostics.Stopwatch();

        private void Reset()
        {
            AssignEditorShaders();
        }

        private void OnValidate()
        {
            AssignEditorShaders();
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
                if (RaytraceComputeShader == null || AdditiveComputeShader == null || BlockComputeShader == null || EnvironmentComputeShader == null)
                    throw new InvalidOperationException("Assign all four RayStorm compute shaders before building or rendering.");

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
            catch (Exception Error)
            {
                StopWithError(Error);
            }
        }

        private void Update()
        {
            if (Context == null || !Context.IsRunning)
                return;
            try
            {
                if (!FramePrepared || FrameInputsChanged())
                {
                    Context.RenderTaskQueue.Clear();
                    BeginFrame();
                }

                RayStormBehaviour.OnFrameUpdate(Context);
                if (FrameInputsChanged())
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
                    if (LastTask.RefreshPreviewAfterTask)
                        break;
                }

                if (Context.RenderTaskQueue.Count == 0)
                {
                    RayStormBehaviour.OnFramePostProcess(Context);
                    if (IsDenoisingEnabled)
                        DenoiseFrame();
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
                else if (LastTask != null && LastTask.RefreshPreviewAfterTask)
                    RefreshDisplay(null);
                else if (LastTask != null && Time.realtimeSinceStartup - LastDisplayTime >= DisplayRefreshInterval)
                    RefreshDisplay(LastTask);
            }
            catch (Exception Error)
            {
                StopWithError(Error);
            }
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
            if (EnvironmentComputeShader == null)
                EnvironmentComputeShader = RSAssetUtility.LoadAsset<ComputeShader>("RayStormEnvironment");
#endif
        }

        private void BeginFrame()
        {
            ReleaseTexture(Context.DenoisedTexture);
            Context.DenoisedTexture = null;
            TotalSampleCount = 0;
            SubmittedSampleCount = 0;
            LastDisplayTime = float.NegativeInfinity;
            RayStormBehaviour.OnFrameStartup(Context);
            EnsureRenderTextures();
            CaptureCamera();
            SceneLightsDirty |= SkyboxTexture != FrameSkyboxTexture || EnvironmentColorSpace != FrameEnvironmentColorSpace || (SkyboxTexture != null && SkyboxTexture.updateCount != FrameSkyboxUpdateCount);
            FrameSkyboxTexture = SkyboxTexture;
            FrameSkyboxUpdateCount = SkyboxTexture != null ? SkyboxTexture.updateCount : 0;
            FrameEnvironmentColorSpace = EnvironmentColorSpace;
            FrameRandomSeed = UnityEngine.Random.Range(1, int.MaxValue);
            ReloadFrameScene();
            ReloadFrameLights();
            RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);
            RSDataUtility.ClearRenderTexture(RaytraceStatistics, Color.clear);
            RayStormBehaviour.OnFrameCollectRenderTask(Context, out RSRenderTask[] RenderTasks);
            if (RenderTasks == null)
                throw new InvalidOperationException("The render behaviour returned a null task list.");
            foreach (RSRenderTask Task in RenderTasks)
            {
                if (Task == null || Task.SubpixelCountPerPixel <= 0 || Task.WindowPos.x < 0 || Task.WindowPos.y < 0)
                    throw new InvalidOperationException("RayStorm received an invalid render task.");
                Task.WindowSize = new Vector2Int(Mathf.Min(Task.WindowSize.x, Context.RenderWidth - Task.WindowPos.x), Mathf.Min(Task.WindowSize.y, Context.RenderHeight - Task.WindowPos.y));
                if (Task.WindowSize.x > 0 && Task.WindowSize.y > 0)
                {
                    Context.RenderTaskQueue.Enqueue(Task);
                    TotalSampleCount += (long)Task.WindowSize.x * Task.WindowSize.y * Task.SubpixelCountPerPixel;
                }
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

        private bool FrameInputsChanged()
        {
            Camera Camera = Context.MainCamera;
            return Camera.pixelWidth != Context.RenderWidth || Camera.pixelHeight != Context.RenderHeight ||
                   Camera.cameraToWorldMatrix != FrameCameraToWorld || Camera.projectionMatrix != FrameProjection ||
                   Camera.cullingMask != FrameCullingMask || Camera.orthographic != FrameOrthographic ||
                   SkyboxTexture != FrameSkyboxTexture || EnvironmentColorSpace != FrameEnvironmentColorSpace ||
                   (SkyboxTexture != null && SkyboxTexture.updateCount != FrameSkyboxUpdateCount) || !DataEquals(FrameLightData, CollectSceneLights(out _));
        }

        private RSLightData[] CollectSceneLights(out Light[] Lights)
        {
            var CollectedLights = new List<Light>();
            var LightData = new List<RSLightData>();
            foreach (Light Source in FindObjectsOfType<Light>())
            {
                if (!Source.isActiveAndEnabled || (FrameCullingMask & (1 << Source.gameObject.layer)) == 0 || (Source.type != LightType.Directional && Source.type != LightType.Point && Source.type != LightType.Spot))
                    continue;
                CollectedLights.Add(Source);
                var Data = new RSLightData
                {
                    Type = (int)Source.type,
                    Position = Source.transform.position,
                    Direction = Source.transform.forward,
                    Radiance = RSDataUtility.ColorToVector(Source.color.linear) * Source.intensity,
                    InnerCosine = Mathf.Cos(0.5f * Source.innerSpotAngle * Mathf.Deg2Rad),
                    OuterCosine = Mathf.Cos(0.5f * Source.spotAngle * Mathf.Deg2Rad)
                };
                LightData.Add(Data);
            }
            Lights = CollectedLights.ToArray();
            return LightData.ToArray();
        }

        private void ReloadFrameLights()
        {
            RSLightData[] LightData = CollectSceneLights(out Light[] Lights);
            SceneLightsDirty |= !DataEquals(FrameLightData, LightData);
            FrameLightData = LightData;
            if (!SceneLightsDirty)
                return;
            Bounds SceneBounds = new Bounds(SceneVertices.Length > 0 ? SceneVertices[0].Position : Vector3.zero, Vector3.zero);
            foreach (RSVertexData Vertex in SceneVertices)
                SceneBounds.Encapsulate(Vertex.Position);
            bool EnvironmentIsGamma = FrameEnvironmentColorSpace == RSEnvironmentColorSpace.UnityImportedColor && QualitySettings.activeColorSpace == ColorSpace.Gamma;
            SceneLightSampler.Build(SceneVertices, SceneGeometries, SceneMaterials, Lights, GetSkybox(), EnvironmentIsGamma, SceneBounds.extents.magnitude, EnvironmentComputeShader);
            SceneLightsDirty = false;
        }

        private void EnsureRenderTextures()
        {
            int Width = Mathf.Max(1, Context.MainCamera.pixelWidth);
            int Height = Mathf.Max(1, Context.MainCamera.pixelHeight);
            if (Context.RaytraceTexture != null && Context.RenderWidth == Width && Context.RenderHeight == Height)
                return;
            ReleaseTexture(Context.RaytraceTexture);
            ReleaseTexture(Context.DenoisedTexture);
            Context.DenoisedTexture = null;
            ReleaseTexture(DisplayTexture);
            ReleaseTexture(RaytraceStatistics);
            Context.RenderWidth = Width;
            Context.RenderHeight = Height;
            Context.RaytraceTexture = CreateRenderTexture(Width, Height, "RayStorm Accumulation");
            DisplayTexture = CreateRenderTexture(Width, Height, "RayStorm Display");
            RaytraceStatistics = CreateRenderTexture(Width, Height, "RayStorm Statistics");
            RSDataUtility.ClearRenderTexture(Context.RaytraceTexture, Color.clear);
            RSDataUtility.ClearRenderTexture(DisplayTexture, Color.black);
        }

        private static RenderTexture CreateRenderTexture(int Width, int Height, string TextureName)
        {
            RenderTexture Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = TextureName,
                useMipMap = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            if (!Texture.Create())
            {
                DestroyOwnedObject(Texture);
                throw new InvalidOperationException("Could not allocate the RayStorm render texture.");
            }
            return Texture;
        }

        private static void EnsureBuffer(ref ComputeBuffer Buffer, int Count, int Stride)
        {
            Count = Mathf.Max(1, Count);
            if (Buffer != null && Buffer.count >= Count)
                return;
            RSDataUtility.ReleaseBuffer(ref Buffer);
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
            int RayCount = checked(Task.WindowSize.x * Task.WindowSize.y * Task.SubpixelCountPerPixel);
            EnsureBuffer(ref RaytraceOutputBuffer, RayCount, RSOutputData.GetDataSize());

            // ComputeShader assets are shared between renderers.
            RaytraceComputeShader.SetInt("RayCount", RayCount);
            RaytraceComputeShader.SetInt("SceneBVHCount", SceneBVHs.Length);
            RaytraceComputeShader.SetInt("RSRandomSeed", FrameRandomSeed);
            RaytraceComputeShader.SetInt("RenderWidth", Context.RenderWidth);
            RaytraceComputeShader.SetInt("RenderHeight", Context.RenderHeight);
            RaytraceComputeShader.SetInt("RaytraceWindowPosX", Task.WindowPos.x);
            RaytraceComputeShader.SetInt("RaytraceWindowPosY", Task.WindowPos.y);
            RaytraceComputeShader.SetInt("RaytraceWindowSizeX", Task.WindowSize.x);
            RaytraceComputeShader.SetInt("SubpixelCountPerPixel", Task.SubpixelCountPerPixel);
            RaytraceComputeShader.SetInt("CameraOrthographic", FrameOrthographic ? 1 : 0);
            RaytraceComputeShader.SetVector("CameraPosition", FrameCameraPosition);
            RaytraceComputeShader.SetVector("CameraForward", FrameCameraForward);
            RaytraceComputeShader.SetVector("NearPlaneBottomLeft", NearPlaneBottomLeft);
            RaytraceComputeShader.SetVector("NearPlaneHorizontal", NearPlaneHorizontal);
            RaytraceComputeShader.SetVector("NearPlaneVertical", NearPlaneVertical);
            SceneLightSampler.Bind(RaytraceComputeShader, RaytraceKernelID);
            RaytraceComputeShader.SetTexture(RaytraceKernelID, "RaytraceAccumulation", Context.RaytraceTexture);
            RaytraceComputeShader.SetTexture(RaytraceKernelID, "RaytraceStatistics", RaytraceStatistics);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneVertexBuffer", SceneVertexBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneGeometryBuffer", SceneGeometryBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneMaterialBuffer", SceneMaterialBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneBVHBuffer", SceneBVHBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RSSceneBVHGeometryIDBuffer", SceneBVHGeometryIDBuffer);
            RaytraceComputeShader.SetBuffer(RaytraceKernelID, "RayStormOutputBuffer", RaytraceOutputBuffer);
            RaytraceComputeShader.Dispatch(RaytraceKernelID, DivideRoundUp(RayCount, RaytraceThreadCount), 1, 1);

            AdditiveComputeShader.SetInt("RaytraceWindowPosX", Task.WindowPos.x);
            AdditiveComputeShader.SetInt("RaytraceWindowPosY", Task.WindowPos.y);
            AdditiveComputeShader.SetInt("RaytraceWindowSizeX", Task.WindowSize.x);
            AdditiveComputeShader.SetInt("RaytraceWindowSizeY", Task.WindowSize.y);
            AdditiveComputeShader.SetInt("SubpixelCountPerPixel", Task.SubpixelCountPerPixel);
            AdditiveComputeShader.SetTexture(AdditiveKernelID, "RaytraceOutputTexture", Context.RaytraceTexture);
            AdditiveComputeShader.SetTexture(AdditiveKernelID, "RaytraceStatistics", RaytraceStatistics);
            AdditiveComputeShader.SetBuffer(AdditiveKernelID, "RaytraceWindowBuffer", RaytraceOutputBuffer);
            AdditiveComputeShader.Dispatch(AdditiveKernelID, DivideRoundUp(Task.WindowSize.x * Task.WindowSize.y, AdditiveThreadCount), 1, 1);
            SubmittedSampleCount += RayCount;
        }

        private Texture GetSkybox()
        {
            if (FrameSkyboxTexture is Cubemap)
                return FrameSkyboxTexture;
            if (OwnedSkybox == null)
                OwnedSkybox = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBA32, false, Color.black);
            return OwnedSkybox;
        }

        private void DenoiseFrame()
        {
            RenderTexture Result = null;
            try
            {
                Result = CreateRenderTexture(Context.RenderWidth, Context.RenderHeight, "RayStorm Denoised");
                RSDenoiseUtility.Denoise(Context.RaytraceTexture, Result);
                Context.DenoisedTexture = Result;
            }
            catch (Exception Error)
            {
                ReleaseTexture(Result);
                Debug.LogWarning("RayStorm denoising failed; using the original HDR image. " + Error.Message, this);
            }
        }

        private void RefreshDisplay(RSRenderTask Task)
        {
            BlockComputeShader.SetInt("ShowRenderBlock", IsDisplayRenderBlock && Task != null ? 1 : 0);
            BlockComputeShader.SetInt("ShowRenderProgress", IsDisplayRenderProgress && TotalSampleCount > 0 && SubmittedSampleCount < TotalSampleCount ? 1 : 0);
            BlockComputeShader.SetFloat("RenderProgress", TotalSampleCount > 0 ? (float)((double)SubmittedSampleCount / TotalSampleCount) : 0.0f);
            BlockComputeShader.SetInt("OutputIsGamma", QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0);
            if (Task != null)
            {
                BlockComputeShader.SetInt("BlockPosX", Task.WindowPos.x);
                BlockComputeShader.SetInt("BlockPosY", Task.WindowPos.y);
                BlockComputeShader.SetInt("BlockSizeX", Task.WindowSize.x);
                BlockComputeShader.SetInt("BlockSizeY", Task.WindowSize.y);
            }
            BlockComputeShader.SetTexture(BlockKernelID, "Source", Context.OutputTexture);
            BlockComputeShader.SetTexture(BlockKernelID, "Target", DisplayTexture);
            BlockComputeShader.Dispatch(BlockKernelID, DivideRoundUp(Context.RenderWidth, BlockThreadCountX), DivideRoundUp(Context.RenderHeight, BlockThreadCountY), 1);
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
                    float Determinant = LocalToWorld.determinant;
                    if (Mathf.Abs(Determinant) < 1e-12f)
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
                    for (int VertexNumber = 0; VertexNumber < Positions.Length; VertexNumber++)
                    {
                        RSVertexData Vertex = RSVertexData.Default;
                        Vertex.Position = LocalToWorld.MultiplyPoint3x4(Positions[VertexNumber]);
                        Vertex.Normal = NormalMatrix.MultiplyVector(Normals.Length == Positions.Length ? Normals[VertexNumber] : Vector3.up).normalized;
                        if (Tangents.Length == Positions.Length)
                        {
                            Vector4 SourceTangent = Tangents[VertexNumber];
                            Vector3 TangentDirection = LocalToWorld.MultiplyVector(SourceTangent).normalized;
                            Vertex.Tangent = new Vector4(TangentDirection.x, TangentDirection.y, TangentDirection.z, SourceTangent.w * Mathf.Sign(Determinant));
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
                        for (int TriangleStart = 0; TriangleStart < TriangleIndices.Length; TriangleStart += 3)
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
            finally
            {
                RSSceneUtility.ReleaseSceneObjects(Objects);
            }

            RSVertexData[] VertexData = Vertices.ToArray();
            RSGeometryData[] GeometryData = Geometries.ToArray();
            RSMaterialData[] MaterialData = Materials.ToArray();
            if (!DataEquals(SceneVertices, VertexData) || !DataEquals(SceneGeometries, GeometryData))
            {
                SceneLightsDirty = true;
                SceneVertices = VertexData;
                SceneGeometries = GeometryData;
                RSBVHUtility.BuildSceneBVH(VertexData, GeometryData, out SceneBVHs, out int[] BVHGeometryIDs);
                UploadBuffer(ref SceneVertexBuffer, VertexData, RSVertexData.GetDataSize());
                UploadBuffer(ref SceneGeometryBuffer, GeometryData, RSGeometryData.GetDataSize());
                UploadBuffer(ref SceneBVHBuffer, SceneBVHs, RSBVHData.GetDataSize());
                UploadBuffer(ref SceneBVHGeometryIDBuffer, BVHGeometryIDs, sizeof(int));
            }
            if (!DataEquals(SceneMaterials, MaterialData))
            {
                SceneLightsDirty = true;
                SceneMaterials = MaterialData;
                UploadBuffer(ref SceneMaterialBuffer, MaterialData, RSMaterialData.GetDataSize());
            }
        }

        private static bool DataEquals<T>(T[] First, T[] Second) where T : struct
        {
            return First != null && MemoryMarshal.AsBytes(First.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(Second.AsSpan()));
        }

        private void OnRenderImage(RenderTexture Source, RenderTexture Destination)
        {
            Graphics.Blit(DisplayTexture != null ? DisplayTexture : Source, Destination);
        }

        public void SaveRenderTexture()
        {
            if (Context == null || Context.RaytraceTexture == null)
                return;
            Texture2D Image = RSDataUtility.RenderTextureToTexture2D(Context.OutputTexture, TextureFormat.RGBA32);
            try
            {
                RSAssetUtility.SaveTexture2D(Image, RSAssetUtility.GetOutputPath("RaytraceTexture.jpg"));
            }
            finally
            {
                DestroyOwnedObject(Image);
            }
        }

        private void OnDrawGizmos()
        {
            if (RayStormBehaviour != null)
                RayStormBehaviour.OnDrawGizmos();
            if (!IsDisplayBVH)
                return;
            Gizmos.color = Color.green;
            foreach (RSBVHData Node in SceneBVHs)
                if (Node.GeometryCount > 0)
                    Gizmos.DrawWireCube((Node.Minimum + Node.Maximum) * 0.5f, Node.Maximum - Node.Minimum);
        }

        private void StopWithError(Exception Error)
        {
            if (Context != null)
                Context.IsRunning = false;
            enabled = false;
            Debug.LogException(Error, this);
        }

        private static void ReleaseTexture(RenderTexture Texture)
        {
            if (Texture == null)
                return;
            Texture.Release();
            DestroyOwnedObject(Texture);
        }

        private static void DestroyOwnedObject(UnityEngine.Object Resource)
        {
            if (Resource == null)
                return;
            if (Application.isPlaying)
                Destroy(Resource);
            else
                DestroyImmediate(Resource);
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
                RSDataUtility.ReleaseBuffer(ref SceneVertexBuffer);
                RSDataUtility.ReleaseBuffer(ref SceneGeometryBuffer);
                RSDataUtility.ReleaseBuffer(ref SceneMaterialBuffer);
                RSDataUtility.ReleaseBuffer(ref SceneBVHBuffer);
                RSDataUtility.ReleaseBuffer(ref SceneBVHGeometryIDBuffer);
                RSDataUtility.ReleaseBuffer(ref RaytraceOutputBuffer);
                SceneLightSampler.Dispose();
                ReleaseTexture(Context == null ? null : Context.RaytraceTexture);
                ReleaseTexture(Context == null ? null : Context.DenoisedTexture);
                ReleaseTexture(DisplayTexture);
                ReleaseTexture(RaytraceStatistics);
                DestroyOwnedObject(OwnedSkybox);
                if (OwnsBehaviour)
                    DestroyOwnedObject(RayStormBehaviour);
                Context = null;
                DisplayTexture = null;
                RaytraceStatistics = null;
                SceneVertices = null;
                SceneGeometries = null;
                SceneMaterials = null;
            }
        }
    }
}
