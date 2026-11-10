using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RayStorm
{
    public static class RayStormRendererValidation
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("RayStorm/Validate Renderer Integration")]
        public static void Run()
        {
            Require(!Application.isPlaying, "Run renderer validation in Edit Mode.");
            ValidateCameraRays();
            ValidateAlternatingRenderers();
            ValidateResizeRestart();
            ValidateMeshImport();
            ValidateResourceRelease();
            Debug.Log("RayStorm renderer validation passed: perspective/orthographic rays, shared shader isolation, resize restart, mesh fallback, vertex reuse and resource release.");
        }

        private static void ValidateCameraRays()
        {
            using (Fixture Fixture = new Fixture(17, 13))
            {
                Fixture.Camera.transform.SetPositionAndRotation(new Vector3(2, 3, -4), Quaternion.Euler(17, 31, 5));
                Fixture.Camera.nearClipPlane = 0.5f;
                Fixture.Camera.fieldOfView = 63;
                Fixture.Camera.orthographicSize = 4;
                foreach (bool IsOrthographic in new[] { false, true })
                {
                    Fixture.Camera.orthographic = IsOrthographic;
                    Invoke(Fixture.Renderer, "CaptureCamera");
                    RSRenderTask Task = new RSRenderTask
                    {
                        WindowPos = new Vector2Int(1, 2), WindowSize = new Vector2Int(13, 9), SubpixelCountPerPixel = 2
                    };
                    int RayCount = (int)Invoke(Fixture.Renderer, "CollectRaySources", Task);
                    RSSourceData[] Sources = Get<RSSourceData[]>(Fixture.Renderer, "RaySources");
                    for (int RayNumber = 0; RayNumber < RayCount; ++RayNumber)
                    {
                        RSSourceData Source = Sources[RayNumber];
                        Ray Expected = Fixture.Camera.ViewportPointToRay(new Vector3(Source.ID.x / 17.0f, Source.ID.y / 13.0f, 0));
                        Require(Vector3.Distance(Source.Position, Expected.origin) < 0.0001f, "Camera ray origin differs from ViewportPointToRay.");
                        Require(Vector3.Distance(Source.Direction, Expected.direction) < 0.0001f, "Camera ray direction differs from ViewportPointToRay.");
                    }
                    Require(Vector3.Distance(Sources[0].Position, Sources[RayCount - 1].Position) > 0.1f, "Distinct pixels must have distinct near-plane origins.");
                }
            }
        }

        private static void ValidateAlternatingRenderers()
        {
            Cubemap RedSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Color.red);
            Cubemap BlueSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Color.blue);
            try
            {
                using (Fixture RedRenderer = new Fixture(7, 5))
                using (Fixture BlueRenderer = new Fixture(9, 3))
                {
                    RedRenderer.Renderer.SkyboxTexture = RedSky;
                    BlueRenderer.Renderer.SkyboxTexture = BlueSky;
                    RedRenderer.Step();
                    BlueRenderer.Step();
                    RedRenderer.Step();
                    BlueRenderer.Step();
                    AssertPixel(RedRenderer.Context.RaytraceTexture, new Color(1, 0, 0, 2));
                    AssertPixel(BlueRenderer.Context.RaytraceTexture, new Color(0, 0, 1, 2));
                    Require(RedRenderer.Context.FrameID == 1 && BlueRenderer.Context.FrameID == 1, "Both renderers must complete their own output frame.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(RedSky);
                UnityEngine.Object.DestroyImmediate(BlueSky);
            }
        }

        private static void ValidateResizeRestart()
        {
            using (Fixture Fixture = new Fixture(17, 13))
            {
                Fixture.Step();
                RenderTexture PreviousAccumulation = Fixture.Context.RaytraceTexture;
                Require(Fixture.Context.RenderTaskQueue.Count == 1, "Fixture must leave an unfinished frame before resizing.");
                Fixture.SetSize(31, 19);
                Fixture.Step();
                Require(Fixture.Behaviour.FrameStartCount == 2, "Resizing must restart the in-flight output frame.");
                Require(Fixture.Context.FrameID == 0, "An interrupted output frame must not be marked complete.");
                Require(Fixture.Context.RenderWidth == 31 && Fixture.Context.RenderHeight == 19, "Resize must replace the output dimensions.");
                Require(PreviousAccumulation == null, "Resize must destroy the previous accumulation texture.");
                AssertPixel(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 1));
                Fixture.Step();
                Require(Fixture.Context.FrameID == 1 && Fixture.Behaviour.FrameFinishCount == 1, "Only the replacement frame may finish.");
                AssertPixel(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 2));
            }
        }

        private static void ValidateMeshImport()
        {
            Mesh SourceMesh = new Mesh { name = "RayStorm validation mesh" };
            GameObject MeshObject = new GameObject("RayStorm validation mesh");
            try
            {
                SourceMesh.vertices = new[] { Vector3.zero, new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 0) };
                SourceMesh.subMeshCount = 2;
                SourceMesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                SourceMesh.SetTriangles(new[] { 0, 2, 3 }, 1);
                MeshObject.layer = 31;
                MeshObject.transform.localScale = new Vector3(2, 3, 4);
                MeshObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                MeshRenderer MeshRenderer = MeshObject.AddComponent<MeshRenderer>();
                MeshRenderer.sharedMaterials = Array.Empty<Material>();

                using (Fixture Fixture = new Fixture(4, 4))
                {
                    Fixture.Camera.cullingMask = 1 << 31;
                    Fixture.Behaviour.CollectScene = true;
                    Invoke(Fixture.Renderer, "BeginFrame");
                    ComputeBuffer VertexBuffer = Get<ComputeBuffer>(Fixture.Renderer, "SceneVertexBuffer");
                    ComputeBuffer GeometryBuffer = Get<ComputeBuffer>(Fixture.Renderer, "SceneGeometryBuffer");
                    ComputeBuffer MaterialBuffer = Get<ComputeBuffer>(Fixture.Renderer, "SceneMaterialBuffer");
                    Require(VertexBuffer.count == 4, "Multiple submeshes must reuse a single copy of mesh vertices.");
                    Require(GeometryBuffer.count == 2 && MaterialBuffer.count == 2, "Both submeshes need geometry and fallback material records.");
                    RSVertexData[] Vertices = new RSVertexData[4];
                    VertexBuffer.GetData(Vertices);
                    Vector3 ExpectedNormal = Vector3.Cross(Vertices[1].Position - Vertices[0].Position, Vertices[2].Position - Vertices[0].Position).normalized;
                    foreach (RSVertexData Vertex in Vertices)
                    {
                        Require(Vector3.Dot(Vertex.Normal, ExpectedNormal) > 0.999f, "Missing normals must be generated and transformed with the inverse transpose.");
                        Require(!float.IsNaN(Vertex.Tangent.x) && !float.IsInfinity(Vertex.Tangent.x), "Missing tangents must produce finite defaults.");
                    }
                    RSGeometryData[] Geometry = new RSGeometryData[2];
                    GeometryBuffer.GetData(Geometry);
                    Require(Geometry[1].Indice0 == 0 && Geometry[1].Indice1 == 2 && Geometry[1].Indice2 == 3, "Submesh indices must reference the shared vertex range.");
                    Require(SourceMesh != null && SourceMesh.normals.Length == 0, "Fallback normal generation must not mutate or destroy the source mesh.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(MeshObject);
                UnityEngine.Object.DestroyImmediate(SourceMesh);
            }
        }

        private static void ValidateResourceRelease()
        {
            using (Fixture Fixture = new Fixture(5, 3))
            {
                Fixture.Step();
                RenderTexture Accumulation = Fixture.Context.RaytraceTexture;
                RenderTexture Display = Get<RenderTexture>(Fixture.Renderer, "DisplayTexture");
                Cubemap FallbackSky = Get<Cubemap>(Fixture.Renderer, "OwnedSkybox");
                string[] BufferFields = { "SceneVertexBuffer", "SceneGeometryBuffer", "SceneMaterialBuffer", "SceneOctreeBuffer", "SceneOctreeGeometryIDBuffer", "RaytraceSourceBuffer", "RaytraceOutputBuffer" };
                ComputeBuffer[] Buffers = new ComputeBuffer[BufferFields.Length];
                for (int BufferIndex = 0; BufferIndex < Buffers.Length; ++BufferIndex)
                    Buffers[BufferIndex] = Get<ComputeBuffer>(Fixture.Renderer, BufferFields[BufferIndex]);
                Invoke(Fixture.Renderer, "OnDestroy");
                Require(Accumulation == null && Display == null && FallbackSky == null, "Owned textures must be destroyed.");
                foreach (ComputeBuffer Buffer in Buffers)
                    Require(Buffer != null && !Buffer.IsValid(), "All allocated compute buffers must be released.");
                Require(Fixture.Behaviour != null, "A supplied behaviour is borrowed and must not be destroyed by the renderer.");
                Require(Get<RSRenderContext>(Fixture.Renderer, "Context") == null, "Destroy must clear the renderer context.");
                Invoke(Fixture.Renderer, "OnDestroy");
            }
        }

        private static void AssertPixel(RenderTexture Texture, Color Expected)
        {
            Texture2D Readback = RSDataUtility.RenderTextureToTexture2D(Texture, TextureFormat.RGBAFloat);
            try
            {
                Color Actual = Readback.GetPixel(Texture.width / 2, Texture.height / 2);
                Require(Mathf.Abs(Actual.r - Expected.r) < 0.001f && Mathf.Abs(Actual.g - Expected.g) < 0.001f &&
                    Mathf.Abs(Actual.b - Expected.b) < 0.001f && Mathf.Abs(Actual.a - Expected.a) < 0.001f,
                    "Unexpected accumulation pixel: " + Actual + "; expected " + Expected);
            }
            finally { UnityEngine.Object.DestroyImmediate(Readback); }
        }

        private static object Invoke(RayStormRenderer Renderer, string MethodName, params object[] Arguments)
        {
            MethodInfo Method = typeof(RayStormRenderer).GetMethod(MethodName, PrivateInstance);
            Require(Method != null, "Renderer validation method not found: " + MethodName);
            try { return Method.Invoke(Renderer, Arguments); }
            catch (TargetInvocationException Error) { throw Error.InnerException ?? Error; }
        }

        private static T Get<T>(RayStormRenderer Renderer, string FieldName)
        {
            return (T)typeof(RayStormRenderer).GetField(FieldName, PrivateInstance).GetValue(Renderer);
        }

        private static void Set(RayStormRenderer Renderer, string FieldName, object Value)
        {
            typeof(RayStormRenderer).GetField(FieldName, PrivateInstance).SetValue(Renderer, Value);
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition) throw new InvalidOperationException(Message);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject CameraObject;
            private RenderTexture CameraTarget;
            public readonly Camera Camera;
            public readonly RayStormRenderer Renderer;
            public readonly RayStormRendererTestBehaviour Behaviour;
            public RSRenderContext Context { get { return Get<RSRenderContext>(Renderer, "Context"); } }

            public Fixture(int Width, int Height)
            {
                CameraObject = new GameObject("RayStorm validation camera");
                CameraObject.SetActive(false);
                Camera = CameraObject.AddComponent<Camera>();
                Camera.enabled = false;
                Renderer = CameraObject.AddComponent<RayStormRenderer>();
                Renderer.enabled = false;
                Behaviour = ScriptableObject.CreateInstance<RayStormRendererTestBehaviour>();
                Behaviour.FrameCount = 0;
                Renderer.RayStormBehaviour = Behaviour;
                SetSize(Width, Height);
                Set(Renderer, "MaxTasksPerUpdate", 1);
                Set(Renderer, "RaytraceComputeShader", AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/RayStorm/Shader/RayStormRaytracer.compute"));
                Set(Renderer, "AdditiveComputeShader", AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/RayStorm/Shader/RayStormAdditive.compute"));
                Set(Renderer, "BlockComputeShader", AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/RayStorm/Shader/RayStormBlock.compute"));
                try
                {
                    Invoke(Renderer, "Awake");
                    Invoke(Renderer, "Start");
                    Require(Context != null && Context.RaytraceTexture != null && Context.IsRunning, "Renderer initialization failed.");
                }
                catch { Dispose(); throw; }
            }

            public void SetSize(int Width, int Height)
            {
                RenderTexture Previous = CameraTarget;
                CameraTarget = new RenderTexture(Width, Height, 0);
                Camera.targetTexture = CameraTarget;
                if (Previous != null) UnityEngine.Object.DestroyImmediate(Previous);
            }

            public void Step()
            {
                Invoke(Renderer, "Update");
                Require(Context != null && Context.IsRunning, "Renderer stopped unexpectedly during integration validation.");
            }

            public void Dispose()
            {
                if (Renderer != null) Invoke(Renderer, "OnDestroy");
                if (Camera != null) Camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(CameraObject);
                if (CameraTarget != null) UnityEngine.Object.DestroyImmediate(CameraTarget);
                if (Behaviour != null) UnityEngine.Object.DestroyImmediate(Behaviour);
            }
        }
    }

    public sealed class RayStormRendererTestBehaviour : RayStormBehaviour
    {
        public bool CollectScene;
        public int FrameStartCount;
        public int FrameFinishCount;

        public override void OnFrameStartup(RSRenderContext Context) { FrameStartCount++; }
        public override void OnFrameFinished(RSRenderContext Context) { FrameFinishCount++; }
        public override void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneObject[] OutputSceneObject)
        {
            OutputSceneObject = CollectScene ? RSSceneUtility.CollectSceneObject(Context.MainCamera) : Array.Empty<RSSceneObject>();
        }
        public override void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            OutputRenderTask = new RSRenderTask[2];
            for (int Pass = 0; Pass < OutputRenderTask.Length; ++Pass)
                OutputRenderTask[Pass] = new RSRenderTask { FrameID = Context.FrameID,
                    WindowSize = new Vector2Int(Context.RenderWidth, Context.RenderHeight), SubpixelCountPerPixel = 1 };
        }
    }
}
