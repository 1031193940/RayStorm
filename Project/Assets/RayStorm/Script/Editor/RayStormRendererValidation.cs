using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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
            ValidateEnvironmentRestart();
            ValidateImportedEnvironment();
            ValidateFirstPassPreview();
            ValidateRenderProgress();
            ValidateResizeRestart();
            ValidateConvergenceRestart();
            ValidateMeshImport();
            ValidateSceneReuse();
            ValidateLightReuse();
            ValidateSkinnedSceneReuse();
            ValidateResourceRelease();
            Debug.Log("RayStorm renderer validation passed.");
        }

        private static void ValidateCameraRays()
        {
            ComputeShader Shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/RayStorm/Shader/Editor/RayStormValidation.compute"));
            try
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
                        RSRenderTask Task = new RSRenderTask { WindowPos = new Vector2Int(1, 2), WindowSize = new Vector2Int(13, 9), SubpixelCountPerPixel = 4 };
                        RSSourceData[] Sources = GenerateSources(Shader, Fixture, Task, 0);
                        foreach (RSSourceData Source in Sources)
                        {
                            Ray Expected = Fixture.Camera.ViewportPointToRay(new Vector3(Source.ID.x / 17.0f, Source.ID.y / 13.0f, 0));
                            Require(Vector3.Distance(Source.Position, Expected.origin) < 0.0001f && Vector3.Distance(Source.Direction, Expected.direction) < 0.0001f, "GPU camera ray differs from ViewportPointToRay.");
                        }
                        Require(Vector3.Distance(Sources[0].Position, Sources[Sources.Length - 1].Position) > 0.1f, "Distinct pixels need distinct origins.");
                        ValidatePrimarySampling(Shader, Fixture, Task, Sources);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(Shader);
            }
        }

        private static RSSourceData[] GenerateSources(ComputeShader Shader, Fixture Fixture, RSRenderTask Task, int SampleOffset)
        {
            int Kernel = Shader.FindKernel("CSKernelGenerateSources");
            int RayCount = Task.WindowSize.x * Task.WindowSize.y * Task.SubpixelCountPerPixel;
            RSSourceData[] Sources = new RSSourceData[RayCount];
            using (ComputeBuffer Buffer = new ComputeBuffer(RayCount, RSSourceData.GetDataSize()))
            {
                RSDataUtility.ClearRenderTexture(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, SampleOffset));
                Shader.SetInt("RayCount", RayCount);
                Shader.SetInt("RenderWidth", Fixture.Context.RenderWidth);
                Shader.SetInt("RenderHeight", Fixture.Context.RenderHeight);
                Shader.SetInt("RaytraceWindowPosX", Task.WindowPos.x);
                Shader.SetInt("RaytraceWindowPosY", Task.WindowPos.y);
                Shader.SetInt("RaytraceWindowSizeX", Task.WindowSize.x);
                Shader.SetInt("SubpixelCountPerPixel", Task.SubpixelCountPerPixel);
                Shader.SetInt("RSRandomSeed", 1234567);
                Shader.SetInt("CameraOrthographic", Get<bool>(Fixture.Renderer, "FrameOrthographic") ? 1 : 0);
                Shader.SetVector("CameraPosition", Get<Vector3>(Fixture.Renderer, "FrameCameraPosition"));
                Shader.SetVector("CameraForward", Get<Vector3>(Fixture.Renderer, "FrameCameraForward"));
                Shader.SetVector("NearPlaneBottomLeft", Get<Vector3>(Fixture.Renderer, "NearPlaneBottomLeft"));
                Shader.SetVector("NearPlaneHorizontal", Get<Vector3>(Fixture.Renderer, "NearPlaneHorizontal"));
                Shader.SetVector("NearPlaneVertical", Get<Vector3>(Fixture.Renderer, "NearPlaneVertical"));
                Shader.SetTexture(Kernel, "RaytraceAccumulation", Fixture.Context.RaytraceTexture);
                Shader.SetBuffer(Kernel, "RayStormSourceOutput", Buffer);
                Shader.GetKernelThreadGroupSizes(Kernel, out uint ThreadCount, out _, out _);
                Shader.Dispatch(Kernel, (RayCount + (int)ThreadCount - 1) / (int)ThreadCount, 1, 1);
                Buffer.GetData(Sources);
            }
            return Sources;
        }

        private static void ValidatePrimarySampling(ComputeShader Shader, Fixture Fixture, RSRenderTask Task, RSSourceData[] Expected)
        {
            foreach (int Tile in new[] { 1, 0 })
            {
                int OffsetX = Tile == 0 ? 0 : 5;
                RSRenderTask TileTask = new RSRenderTask { WindowPos = Task.WindowPos + new Vector2Int(OffsetX, 0), WindowSize = new Vector2Int(Tile == 0 ? 5 : 8, Task.WindowSize.y), SubpixelCountPerPixel = Task.SubpixelCountPerPixel };
                RSSourceData[] Actual = GenerateSources(Shader, Fixture, TileTask, 0);
                for (int RayIndex = 0; RayIndex < Actual.Length; ++RayIndex)
                {
                    int PixelIndex = RayIndex / Task.SubpixelCountPerPixel;
                    int ExpectedPixel = PixelIndex / TileTask.WindowSize.x * Task.WindowSize.x + PixelIndex % TileTask.WindowSize.x + OffsetX;
                    Require(Actual[RayIndex].Equals(Expected[ExpectedPixel * Task.SubpixelCountPerPixel + RayIndex % Task.SubpixelCountPerPixel]), "Tile split changed primary samples.");
                }
            }
            RSRenderTask PassTask = new RSRenderTask { WindowPos = Task.WindowPos, WindowSize = Task.WindowSize, SubpixelCountPerPixel = 1 };
            for (int Pass = 0; Pass < Task.SubpixelCountPerPixel; ++Pass)
            {
                RSSourceData[] Actual = GenerateSources(Shader, Fixture, PassTask, Pass);
                for (int PixelIndex = 0; PixelIndex < Actual.Length; ++PixelIndex)
                {
                    Require(Actual[PixelIndex].Equals(Expected[PixelIndex * Task.SubpixelCountPerPixel + Pass]), "Pass split changed primary samples.");
                    if (Pass > 0)
                        Require(!Actual[PixelIndex].ID.Equals(Expected[PixelIndex * Task.SubpixelCountPerPixel].ID), "Sample offset repeated primary samples.");
                }
            }
        }

        private static void ValidateAlternatingRenderers()
        {
            Color LinearColor = new Color(0.18f, 1, 4, 1);
            Color ImportedColor = new Color(4, 0.18f, 1, 1);
            Color EncodedColor = QualitySettings.activeColorSpace == ColorSpace.Gamma ? ImportedColor.gamma : ImportedColor;
            Cubemap LinearSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, LinearColor);
            Cubemap ImportedSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, EncodedColor);
            try
            {
                using (Fixture LinearRenderer = new Fixture(7, 5))
                using (Fixture ImportedRenderer = new Fixture(9, 3))
                {
                    LinearRenderer.Renderer.SkyboxTexture = LinearSky;
                    ImportedRenderer.Renderer.SkyboxTexture = ImportedSky;
                    ImportedRenderer.Renderer.EnvironmentColorSpace = RSEnvironmentColorSpace.UnityImportedColor;
                    LinearRenderer.Step();
                    ImportedRenderer.Step();
                    LinearRenderer.Step();
                    ImportedRenderer.Step();
                    LinearColor.a = 2;
                    ImportedColor.a = 2;
                    AssertPixel(LinearRenderer.Context.RaytraceTexture, LinearColor);
                    AssertPixel(ImportedRenderer.Context.RaytraceTexture, ImportedColor);
                    Require(LinearRenderer.Context.FrameID == 1 && ImportedRenderer.Context.FrameID == 1, "Both renderers must complete their own output frame.");
                }
            }
            finally
            {
                Object.DestroyImmediate(LinearSky);
                Object.DestroyImmediate(ImportedSky);
            }
        }

        private static void ValidateEnvironmentRestart()
        {
            Color ReplacementColor = new Color(0.5f, 0.25f, 2, 1);
            Cubemap FirstSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, new Color(0.18f, 1, 4, 1));
            Cubemap SecondSky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, ReplacementColor);
            try
            {
                using (Fixture Fixture = new Fixture(3, 2))
                {
                    Fixture.Renderer.SkyboxTexture = FirstSky;
                    Fixture.Step();
                    Fixture.Renderer.SkyboxTexture = SecondSky;
                    Fixture.Step();
                    Require(Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 6 && Get<long>(Fixture.Renderer, "TotalSampleCount") == 12, "Environment restart retained progress.");
                    AssertPixel(Fixture.Context.RaytraceTexture, ReplacementColor);

                    Fixture.Renderer.EnvironmentColorSpace = RSEnvironmentColorSpace.UnityImportedColor;
                    Fixture.Step();
                    Color Expected = QualitySettings.activeColorSpace == ColorSpace.Gamma ? ReplacementColor.linear : ReplacementColor;
                    AssertPixel(Fixture.Context.RaytraceTexture, Expected);
                    Fixture.Step();
                    Require(Fixture.Context.FrameID == 1 && Fixture.Behaviour.FrameFinishCount == 1, "Environment restart failed.");
                }
            }
            finally
            {
                Object.DestroyImmediate(FirstSky);
                Object.DestroyImmediate(SecondSky);
            }
        }

        private static void ValidateImportedEnvironment()
        {
            string FolderName = "RayStormColorValidation_" + Guid.NewGuid().ToString("N");
            string FolderPath = "Assets/" + FolderName;
            Texture2D Source = new Texture2D(16, 8, TextureFormat.RGBAFloat, false, true);
            try
            {
                AssetDatabase.CreateFolder("Assets", FolderName);
                Color Expected = new Color(0.18f, 1, 4, 1);
                Color[] Pixels = new Color[Source.width * Source.height];
                for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
                    Pixels[PixelIndex] = Expected;
                Source.SetPixels(Pixels);
                Source.Apply();
                string TexturePath = FolderPath + "/KnownRadiance.exr";
                File.WriteAllBytes(Path.Combine(Application.dataPath, FolderName, "KnownRadiance.exr"), Source.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter Importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                Importer.textureShape = TextureImporterShape.TextureCube;
                Importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                Importer.textureCompression = TextureImporterCompression.Uncompressed;
                Importer.mipmapEnabled = false;
                Importer.sRGBTexture = true;
                Importer.SaveAndReimport();
                Cubemap Environment = AssetDatabase.LoadAssetAtPath<Cubemap>(TexturePath);
                using (Fixture Fixture = new Fixture(3, 2))
                {
                    Fixture.Renderer.SkyboxTexture = Environment;
                    Fixture.Renderer.EnvironmentColorSpace = RSEnvironmentColorSpace.UnityImportedColor;
                    Fixture.Step();
                    Fixture.Step();
                    Expected.a = 2;
                    AssertAllPixels(Fixture.Context.RaytraceTexture, Expected, 0.002f);
                    ValidateDisplayAndExport(Fixture, Expected);
                }
            }
            finally
            {
                Object.DestroyImmediate(Source);
                AssetDatabase.DeleteAsset(FolderPath);
            }
        }

        private static void ValidateDisplayAndExport(Fixture Fixture, Color Radiance)
        {
            RenderTexture Destination = new RenderTexture(3, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D ScreenReadback = null;
            Texture2D Export = null;
            GameObject PresentationObject = null;
            RenderTexture PreviousActive = RenderTexture.active;
            bool PreviousSRGBWrite = GL.sRGBWrite;
            try
            {
                Destination.Create();
                PresentationObject = new GameObject("RayStorm presentation validation") { hideFlags = HideFlags.HideAndDontSave };
                Camera PresentationCamera = PresentationObject.AddComponent<Camera>();
                PresentationCamera.enabled = false;
                PresentationCamera.cullingMask = 0;
                PresentationCamera.clearFlags = CameraClearFlags.SolidColor;
                PresentationCamera.backgroundColor = Color.magenta;
                PresentationCamera.targetTexture = Destination;
                RayStormPresentationTestBridge Bridge = PresentationObject.AddComponent<RayStormPresentationTestBridge>();
                Bridge.Renderer = Fixture.Renderer;
                PresentationCamera.Render();
                ScreenReadback = RSDataUtility.RenderTextureToTexture2D(Destination, TextureFormat.RGBAFloat);
                Export = RSDataUtility.RenderTextureToTexture2D(Fixture.Context.RaytraceTexture, TextureFormat.RGBA32);
                Color Expected = new Color(Mathf.LinearToGammaSpace(Radiance.r / (1 + Radiance.r)), Mathf.LinearToGammaSpace(Radiance.g / (1 + Radiance.g)), Mathf.LinearToGammaSpace(Radiance.b / (1 + Radiance.b)), 1);
                Color[] ScreenPixels = ScreenReadback.GetPixels();
                Color[] ExportPixels = Export.GetPixels();
                for (int PixelIndex = 0; PixelIndex < ScreenPixels.Length; ++PixelIndex)
                {
                    AssertColor(ScreenPixels[PixelIndex], Expected, 1.0f / 255.0f);
                    AssertColor(ExportPixels[PixelIndex], Expected, 1.0f / 255.0f);
                }
            }
            finally
            {
                Object.DestroyImmediate(PresentationObject);
                RenderTexture.active = PreviousActive;
                GL.sRGBWrite = PreviousSRGBWrite;
                Object.DestroyImmediate(ScreenReadback);
                Object.DestroyImmediate(Export);
                Destination.Release();
                Object.DestroyImmediate(Destination);
            }
        }

        private static void ValidateFirstPassPreview()
        {
            Cubemap Sky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Color.red);
            RSTexturePathtracerBehaviour Behaviour = ScriptableObject.CreateInstance<RSTexturePathtracerBehaviour>();
            try
            {
                using (Fixture Fixture = new Fixture(129, 3))
                {
                    Fixture.Renderer.RayStormBehaviour = Behaviour;
                    Fixture.Renderer.SkyboxTexture = Sky;
                    Fixture.Renderer.IsDisplayRenderProgress = false;
                    Fixture.Camera.cullingMask = 0;
                    Set(Fixture.Renderer, "MaxTasksPerUpdate", 3);
                    Set(Fixture.Renderer, "SubmissionBudgetMilliseconds", float.PositiveInfinity);
                    Set(Fixture.Renderer, "DisplayRefreshInterval", float.PositiveInfinity);
                    Fixture.Step();

                    Require(Fixture.Context.FrameID == 0 && Fixture.Context.RenderTaskQueue.Peek().SubpixelCountPerPixel == 15, "The first preview must defer refinement until the next Update.");
                    Require(Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 129 * 3 && Get<long>(Fixture.Renderer, "TotalSampleCount") == 129 * 3 * 816, "First-pass sample progress differs.");
                    float DisplayRed = QualitySettings.activeColorSpace == ColorSpace.Gamma ? Mathf.LinearToGammaSpace(0.5f) : 0.5f;
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(1, 0, 0, 1));
                    AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "DisplayTexture"), new Color(DisplayRed, 0, 0, 1));

                    Fixture.Step();
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(1, 0, 0, 16));
                }
            }
            finally
            {
                Object.DestroyImmediate(Behaviour);
                Object.DestroyImmediate(Sky);
            }
        }

        private static void ValidateRenderProgress()
        {
            using (Fixture Fixture = new Fixture(64, 48))
            using (Fixture Other = new Fixture(11, 9))
            {
                RSRenderTask First = new RSRenderTask { WindowPos = new Vector2Int(60, 45), WindowSize = new Vector2Int(8, 8), SubpixelCountPerPixel = 2 };
                Fixture.Behaviour.RenderTasks = new[]
                {
                    First,
                    new RSRenderTask { WindowSize = new Vector2Int(0, 2), SubpixelCountPerPixel = 1 },
                    new RSRenderTask { WindowSize = new Vector2Int(-1, 2), SubpixelCountPerPixel = 1 },
                    new RSRenderTask { WindowPos = new Vector2Int(64, 0), WindowSize = Vector2Int.one, SubpixelCountPerPixel = 1 },
                    new RSRenderTask { WindowPos = Vector2Int.one, WindowSize = new Vector2Int(3, 2), SubpixelCountPerPixel = 5 }
                };
                Fixture.Renderer.IsDisplayRenderBlock = false;
                Set(Fixture.Renderer, "LastDisplayTime", float.PositiveInfinity);
                Set(Fixture.Renderer, "DisplayRefreshInterval", float.PositiveInfinity);
                Fixture.Step();
                Require(First.WindowSize == new Vector2Int(4, 3) && Fixture.Context.RenderTaskQueue.Count == 1 && Get<long>(Fixture.Renderer, "TotalSampleCount") == 54 && Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 24, "Clipped task sample weights differ.");
                Color[] Progress = ReadDisplayPixels(Fixture.Renderer);
                AssertColor(Progress[27], Color.white);
                AssertColor(Progress[28], Color.black);

                Other.Renderer.IsDisplayRenderBlock = false;
                Other.Renderer.IsDisplayRenderProgress = false;
                Other.Step();
                AssertAllPixels(Get<RenderTexture>(Other.Renderer, "DisplayTexture"), Color.black);
                Invoke(Fixture.Renderer, "RefreshDisplay", (object)null);
                Progress = ReadDisplayPixels(Fixture.Renderer);
                AssertColor(Progress[27], Color.white);
                AssertColor(Progress[28], Color.black);
                Invoke(Other.Renderer, "RefreshDisplay", (object)null);
                AssertAllPixels(Get<RenderTexture>(Other.Renderer, "DisplayTexture"), Color.black);

                Fixture.Renderer.IsDisplayRenderProgress = false;
                Invoke(Fixture.Renderer, "RefreshDisplay", (object)null);
                AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "DisplayTexture"), Color.black);
                Fixture.Renderer.IsDisplayRenderBlock = true;
                Invoke(Fixture.Renderer, "RefreshDisplay", First);
                Require(Array.Exists(ReadDisplayPixels(Fixture.Renderer), Pixel => Pixel.r + Pixel.g + Pixel.b > 0.001f), "Block display switch was ignored.");
                Fixture.Renderer.IsDisplayRenderBlock = false;
                Invoke(Fixture.Renderer, "RefreshDisplay", First);
                AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "DisplayTexture"), Color.black);
                Fixture.Renderer.IsDisplayRenderBlock = true;
                Fixture.Renderer.IsDisplayRenderProgress = true;
                Fixture.Step();
                Require(Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 54 && Fixture.Context.FrameID == 1, "Completed sample count differs.");
                AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "DisplayTexture"), Color.black);

                Fixture.Behaviour.RenderTasks = new[]
                {
                    new RSRenderTask { WindowSize = new Vector2Int(64, 48), SubpixelCountPerPixel = 500000 },
                    new RSRenderTask { WindowSize = new Vector2Int(64, 48), SubpixelCountPerPixel = 500000 }
                };
                BeginFrame(Fixture);
                Require(Get<long>(Fixture.Renderer, "TotalSampleCount") == 3072000000L && Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 0 && float.IsNegativeInfinity(Get<float>(Fixture.Renderer, "LastDisplayTime")), "Frame restart or long sample total differs.");
                Fixture.Behaviour.RenderTasks = Array.Empty<RSRenderTask>();
                BeginFrame(Fixture);
                Fixture.Step();
                Require(Get<long>(Fixture.Renderer, "TotalSampleCount") == 0 && Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 0, "Empty frame retained progress.");
                AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "DisplayTexture"), Color.black);
            }
        }

        private static Color[] ReadDisplayPixels(RayStormRenderer Renderer)
        {
            Texture2D Readback = RSDataUtility.RenderTextureToTexture2D(Get<RenderTexture>(Renderer, "DisplayTexture"), TextureFormat.RGBAFloat);
            try
            {
                return Readback.GetPixels();
            }
            finally
            {
                Object.DestroyImmediate(Readback);
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
                Require(Get<long>(Fixture.Renderer, "SubmittedSampleCount") == 31 * 19 && Get<long>(Fixture.Renderer, "TotalSampleCount") == 31 * 19 * 2, "Resize retained previous sample progress.");
                Require(PreviousAccumulation == null, "Resize must destroy the previous accumulation texture.");
                AssertPixel(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 1));
                Fixture.Step();
                Require(Fixture.Context.FrameID == 1 && Fixture.Behaviour.FrameFinishCount == 1, "Only the replacement frame may finish.");
                AssertPixel(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 2));
            }
        }

        private static void ValidateConvergenceRestart()
        {
            using (Fixture Fixture = new Fixture(3, 2))
            {
                Fixture.Behaviour.RenderTasks = new[]
                {
                    new RSRenderTask { WindowSize = new Vector2Int(3, 2), SubpixelCountPerPixel = 127 },
                    new RSRenderTask { WindowSize = new Vector2Int(3, 2) },
                    new RSRenderTask { WindowSize = new Vector2Int(3, 2) }
                };
                Fixture.Step();
                AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 127));
                Fixture.Step();
                RenderTexture Statistics = Get<RenderTexture>(Fixture.Renderer, "RaytraceStatistics");
                AssertAllPixels(Statistics, new Color(0, 0, 0, 1));
                ComputeBuffer Buffer = Get<ComputeBuffer>(Fixture.Renderer, "RaytraceOutputBuffer");
                Vector4[] Markers = new Vector4[6];
                for (int Index = 0; Index < Markers.Length; ++Index)
                    Markers[Index] = Vector4.one * 8;
                Buffer.SetData(Markers, 0, 0, Markers.Length);
                Fixture.Step();
                Buffer.GetData(Markers, 0, 0, Markers.Length);
                foreach (Vector4 Marker in Markers)
                    Require(Marker == Vector4.one * 8, "Converged pixels must skip tracing.");
                AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 128));

                Fixture.Step();
                AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 127));
                AssertAllPixels(Statistics, Color.clear);
                Fixture.Step();
                Fixture.SetSize(2, 1);
                Fixture.Step();
                Require(Statistics == null, "Resize must release the previous statistics texture.");
                AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 127));
                AssertAllPixels(Get<RenderTexture>(Fixture.Renderer, "RaytraceStatistics"), Color.clear);
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
                Object.DestroyImmediate(MeshObject);
                Object.DestroyImmediate(SourceMesh);
            }
        }

        private static void ValidateLightReuse()
        {
            GameObject Surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            GameObject LightObject = new GameObject("RayStorm light reuse") { layer = 31 };
            Material Material = new Material(Shader.Find("Standard"));
            Cubemap Sky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Color.black);
            ComputeBuffer LightBuffer = null;
            try
            {
                Surface.layer = 31;
                Surface.transform.position = Vector3.forward * 2;
                Surface.transform.localScale = Vector3.one * 10;
                Material.SetColor("_Color", Color.white);
                Material.SetFloat("_Metallic", 0);
                Material.SetFloat("_Glossiness", 0);
                Surface.GetComponent<Renderer>().sharedMaterial = Material;
                Light Source = LightObject.AddComponent<Light>();
                Source.type = LightType.Directional;
                Source.color = Color.white;
                Source.intensity = 2;
                using (Fixture Fixture = new Fixture(3, 2))
                {
                    Fixture.Camera.cullingMask = 1 << 31;
                    Fixture.Camera.orthographic = true;
                    Fixture.Camera.orthographicSize = 0.1f;
                    Fixture.Behaviour.CollectScene = true;
                    Fixture.Renderer.SkyboxTexture = Sky;
                    Fixture.Step();
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(2 / Mathf.PI, 2 / Mathf.PI, 2 / Mathf.PI, 1));
                    RSLightSampler Sampler = Get<RSLightSampler>(Fixture.Renderer, "SceneLightSampler");
                    LightBuffer = GetLightBuffer(Sampler);
                    RSBVHData[] BVHs = Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs");
                    ComputeBuffer GeometryBuffer = Get<ComputeBuffer>(Fixture.Renderer, "SceneGeometryBuffer");
                    Fixture.Step();
                    Fixture.Step();
                    Require(ReferenceEquals(LightBuffer, GetLightBuffer(Sampler)), "Static frame rebuilt light data.");
                    Source.intensity = 4;
                    Fixture.Step();
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(4 / Mathf.PI, 4 / Mathf.PI, 4 / Mathf.PI, 1));
                    Require(!ReferenceEquals(LightBuffer, GetLightBuffer(Sampler)), "Light intensity did not rebuild light data.");
                    Source.transform.rotation = Quaternion.Euler(0, 60, 0);
                    Fixture.Step();
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(2 / Mathf.PI, 2 / Mathf.PI, 2 / Mathf.PI, 1));
                    Source.enabled = false;
                    Fixture.Step();
                    AssertAllPixels(Fixture.Context.RaytraceTexture, new Color(0, 0, 0, 1));
                    Require(Sampler.SceneLightCount == 1, "Disabled light was collected.");
                    Source.enabled = true;
                    LightObject.layer = 30;
                    Require(!(bool)Invoke(Fixture.Renderer, "FrameInputsChanged"), "Excluded light layer invalidated the frame.");
                    LightObject.layer = 31;
                    foreach (LightType Type in new[] { LightType.Point, LightType.Spot })
                    {
                        Source.type = Type;
                        Require((bool)Invoke(Fixture.Renderer, "FrameInputsChanged"), "Light type did not invalidate the frame.");
                        Fixture.Step();
                        var Data = new RSLightData[Sampler.SceneLightCount];
                        GetLightBuffer(Sampler).GetData(Data);
                        Require(Data[0].Type == (Type == LightType.Point ? 1 : 2), "Light type was not uploaded.");
                    }
                    for (int Face = 0; Face < 6; ++Face)
                        Sky.SetPixels(new[] { new Color(0.25f, 1, 4, 1) }, (CubemapFace)Face);
                    Sky.Apply();
                    Require((bool)Invoke(Fixture.Renderer, "FrameInputsChanged"), "In-place HDR update did not invalidate the frame.");
                    Fixture.Step();
                    Require(!(bool)Invoke(Fixture.Renderer, "FrameInputsChanged"), "Updated HDR state was not captured.");
                    Material.SetColor("_EmissionColor", Color.white * 2);
                    BeginFrame(Fixture);
                    Require(Sampler.SceneLightCount == 4, "Emissive material was not added to light sampling.");
                    Require(ReferenceEquals(BVHs, Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs")) && ReferenceEquals(GeometryBuffer, Get<ComputeBuffer>(Fixture.Renderer, "SceneGeometryBuffer")), "Lighting changes rebuilt geometry.");
                    LightBuffer = GetLightBuffer(Sampler);
                }
                Require(!LightBuffer.IsValid(), "Light buffer was not released.");
            }
            finally
            {
                Object.DestroyImmediate(Surface);
                Object.DestroyImmediate(LightObject);
                Object.DestroyImmediate(Material);
                Object.DestroyImmediate(Sky);
            }
        }

        private static ComputeBuffer GetLightBuffer(RSLightSampler Sampler)
        {
            return (ComputeBuffer)typeof(RSLightSampler).GetField("SceneLightBuffer", PrivateInstance).GetValue(Sampler);
        }

        private static void ValidateSceneReuse()
        {
            Mesh SourceMesh = new Mesh { vertices = new[] { new Vector3(0, 0, 4), new Vector3(1, 0, 4), new Vector3(1, 1, 4), new Vector3(0, 1, 4) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            Material Material = new Material(Shader.Find("Standard"));
            GameObject MeshObject = new GameObject("RayStorm reuse mesh") { layer = 31 };
            GameObject AddedObject = null;
            Cubemap Sky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Color.white);
            try
            {
                SourceMesh.RecalculateNormals();
                MeshObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                MeshRenderer MeshRenderer = MeshObject.AddComponent<MeshRenderer>();
                MeshRenderer.sharedMaterial = Material;
                using (Fixture Fixture = new Fixture(3, 2))
                {
                    Fixture.Camera.cullingMask = 1 << 31;
                    Fixture.Behaviour.CollectScene = true;
                    Fixture.Step();
                    RSBVHData[] BVHs = Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs");
                    string[] BufferFields = { "SceneVertexBuffer", "SceneGeometryBuffer", "SceneBVHBuffer", "SceneBVHGeometryIDBuffer" };
                    ComputeBuffer[] Buffers = new ComputeBuffer[BufferFields.Length];
                    for (int BufferIndex = 0; BufferIndex < Buffers.Length; ++BufferIndex)
                        Buffers[BufferIndex] = Get<ComputeBuffer>(Fixture.Renderer, BufferFields[BufferIndex]);
                    Fixture.Step();
                    Fixture.Step();
                    Require(Fixture.Behaviour.FrameStartCount == 2 && ReferenceEquals(BVHs, Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs")), "New frame rebuilt unchanged geometry.");
                    Fixture.Camera.transform.position = Vector3.right;
                    Fixture.Step();
                    Require(ReferenceEquals(BVHs, Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs")), "Camera change rebuilt geometry.");
                    Fixture.Renderer.SkyboxTexture = Sky;
                    Fixture.Step();
                    Require(ReferenceEquals(BVHs, Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs")), "Environment change rebuilt geometry.");

                    Material.SetFloat("_Metallic", 0.75f);
                    Require(ReferenceEquals(BVHs, BeginFrame(Fixture)), "Material change rebuilt geometry.");
                    RSMaterialData[] Materials = new RSMaterialData[1];
                    Get<ComputeBuffer>(Fixture.Renderer, "SceneMaterialBuffer").GetData(Materials);
                    Require(Materials[0].Metallic == 0.75f, "Material buffer was not updated.");
                    for (int BufferIndex = 0; BufferIndex < Buffers.Length; ++BufferIndex)
                        Require(ReferenceEquals(Buffers[BufferIndex], Get<ComputeBuffer>(Fixture.Renderer, BufferFields[BufferIndex])), "Unchanged geometry buffer was replaced.");

                    Vector3[] Positions = SourceMesh.vertices;
                    Positions[0] += Vector3.left;
                    SourceMesh.vertices = Positions;
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                    RSVertexData[] Vertices = new RSVertexData[4];
                    Get<ComputeBuffer>(Fixture.Renderer, "SceneVertexBuffer").GetData(Vertices);
                    Require(Vertices[0].Position.Equals(Positions[0]), "In-place vertices were not uploaded.");
                    SourceMesh.SetTriangles(new[] { 0, 1, 3, 1, 2, 3 }, 0);
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                    RSGeometryData[] Geometries = new RSGeometryData[2];
                    Get<ComputeBuffer>(Fixture.Renderer, "SceneGeometryBuffer").GetData(Geometries);
                    Require(Geometries[0].Indice2 == 3 && Geometries[1].Indice0 == 1, "In-place indices were not uploaded.");
                    MeshObject.transform.position = new Vector3(2, 1, 0);
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                    MeshRenderer.enabled = false;
                    AssertSceneChanged(Fixture, ref BVHs, 0);
                    MeshRenderer.enabled = true;
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                    MeshObject.layer = 30;
                    AssertSceneChanged(Fixture, ref BVHs, 0);
                    MeshObject.layer = 31;
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                    AddedObject = new GameObject("RayStorm added mesh") { layer = 31 };
                    AddedObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                    AddedObject.AddComponent<MeshRenderer>().sharedMaterial = Material;
                    AssertSceneChanged(Fixture, ref BVHs, 4);
                    Object.DestroyImmediate(AddedObject);
                    AssertSceneChanged(Fixture, ref BVHs, 2);
                }
            }
            finally
            {
                Object.DestroyImmediate(AddedObject);
                Object.DestroyImmediate(MeshObject);
                Object.DestroyImmediate(SourceMesh);
                Object.DestroyImmediate(Material);
                Object.DestroyImmediate(Sky);
            }
        }

        private static void ValidateSkinnedSceneReuse()
        {
            Mesh SourceMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            GameObject MeshObject = new GameObject("RayStorm skinned mesh") { layer = 31 };
            GameObject BoneObject = new GameObject("RayStorm bone");
            try
            {
                BoneObject.transform.SetParent(MeshObject.transform, false);
                SourceMesh.RecalculateNormals();
                SourceMesh.bindposes = new[] { Matrix4x4.identity };
                BoneWeight Weight = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                SourceMesh.boneWeights = new[] { Weight, Weight, Weight };
                SkinnedMeshRenderer MeshRenderer = MeshObject.AddComponent<SkinnedMeshRenderer>();
                MeshRenderer.sharedMesh = SourceMesh;
                MeshRenderer.bones = new[] { BoneObject.transform };
                MeshRenderer.rootBone = BoneObject.transform;
                using (Fixture Fixture = new Fixture(3, 2))
                {
                    Fixture.Camera.cullingMask = 1 << 31;
                    Fixture.Behaviour.CollectScene = true;
                    RSBVHData[] BVHs = BeginFrame(Fixture);
                    RSVertexData[] Before = new RSVertexData[3];
                    Get<ComputeBuffer>(Fixture.Renderer, "SceneVertexBuffer").GetData(Before);
                    Require(ReferenceEquals(BVHs, BeginFrame(Fixture)), "Unchanged pose rebuilt geometry.");
                    BoneObject.transform.localPosition = Vector3.right * 0.5f;
                    AssertSceneChanged(Fixture, ref BVHs, 1);
                    RSVertexData[] After = new RSVertexData[3];
                    Get<ComputeBuffer>(Fixture.Renderer, "SceneVertexBuffer").GetData(After);
                    Require(Vector3.Distance(After[0].Position - Before[0].Position, Vector3.right * 0.5f) < 0.0001f, "Skinned pose was not uploaded.");
                }
            }
            finally
            {
                Object.DestroyImmediate(MeshObject);
                Object.DestroyImmediate(SourceMesh);
            }
        }

        private static RSBVHData[] BeginFrame(Fixture Fixture)
        {
            Fixture.Context.RenderTaskQueue.Clear();
            Invoke(Fixture.Renderer, "BeginFrame");
            return Get<RSBVHData[]>(Fixture.Renderer, "SceneBVHs");
        }

        private static void AssertSceneChanged(Fixture Fixture, ref RSBVHData[] Previous, int GeometryCount)
        {
            RSBVHData[] Current = BeginFrame(Fixture);
            Require(!ReferenceEquals(Previous, Current) && Get<RSGeometryData[]>(Fixture.Renderer, "SceneGeometries").Length == GeometryCount, "Scene change did not rebuild geometry.");
            Previous = Current;
        }

        private static void ValidateResourceRelease()
        {
            using (Fixture Fixture = new Fixture(5, 3))
            {
                Fixture.Step();
                RenderTexture Accumulation = Fixture.Context.RaytraceTexture;
                RenderTexture Display = Get<RenderTexture>(Fixture.Renderer, "DisplayTexture");
                RenderTexture Statistics = Get<RenderTexture>(Fixture.Renderer, "RaytraceStatistics");
                Cubemap FallbackSky = Get<Cubemap>(Fixture.Renderer, "OwnedSkybox");
                string[] BufferFields = { "SceneVertexBuffer", "SceneGeometryBuffer", "SceneMaterialBuffer", "SceneBVHBuffer", "SceneBVHGeometryIDBuffer", "RaytraceOutputBuffer" };
                ComputeBuffer[] Buffers = new ComputeBuffer[BufferFields.Length];
                for (int BufferIndex = 0; BufferIndex < Buffers.Length; ++BufferIndex)
                    Buffers[BufferIndex] = Get<ComputeBuffer>(Fixture.Renderer, BufferFields[BufferIndex]);
                Invoke(Fixture.Renderer, "OnDestroy");
                Require(Accumulation == null && Display == null && Statistics == null && FallbackSky == null, "Owned textures must be destroyed.");
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
                AssertColor(Readback.GetPixel(Texture.width / 2, Texture.height / 2), Expected);
            }
            finally
            {
                Object.DestroyImmediate(Readback);
            }
        }

        private static void AssertAllPixels(RenderTexture Texture, Color Expected, float Tolerance = 0.001f)
        {
            Texture2D Readback = RSDataUtility.RenderTextureToTexture2D(Texture, TextureFormat.RGBAFloat);
            try
            {
                foreach (Color Actual in Readback.GetPixels())
                    AssertColor(Actual, Expected, Tolerance);
            }
            finally
            {
                Object.DestroyImmediate(Readback);
            }
        }

        private static void AssertColor(Color Actual, Color Expected, float Tolerance = 0.001f)
        {
            Require(Mathf.Abs(Actual.r - Expected.r) <= Tolerance && Mathf.Abs(Actual.g - Expected.g) <= Tolerance && Mathf.Abs(Actual.b - Expected.b) <= Tolerance && Mathf.Abs(Actual.a - Expected.a) <= Tolerance, "Unexpected pixel: " + Actual + "; expected " + Expected);
        }

        private static object Invoke(RayStormRenderer Renderer, string MethodName, params object[] Arguments)
        {
            MethodInfo Method = typeof(RayStormRenderer).GetMethod(MethodName, PrivateInstance);
            try
            {
                return Method.Invoke(Renderer, Arguments);
            }
            catch (TargetInvocationException Error)
            {
                throw Error.InnerException;
            }
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
            if (!Condition)
                throw new InvalidOperationException(Message);
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
                Renderer.IsDenoisingEnabled = false;
                Renderer.EnvironmentColorSpace = RSEnvironmentColorSpace.LinearRadiance;
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
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void SetSize(int Width, int Height)
            {
                RenderTexture Previous = CameraTarget;
                CameraTarget = new RenderTexture(Width, Height, 0);
                Camera.targetTexture = CameraTarget;
                Object.DestroyImmediate(Previous);
            }

            public void Step()
            {
                Invoke(Renderer, "Update");
                Require(Context.IsRunning, "Renderer stopped unexpectedly during integration validation.");
            }

            public void Dispose()
            {
                Invoke(Renderer, "OnDestroy");
                Camera.targetTexture = null;
                Object.DestroyImmediate(CameraObject);
                Object.DestroyImmediate(CameraTarget);
                Object.DestroyImmediate(Behaviour);
            }
        }
    }

    [ExecuteInEditMode]
    public sealed class RayStormPresentationTestBridge : MonoBehaviour
    {
        public RayStormRenderer Renderer;

        private void OnRenderImage(RenderTexture Source, RenderTexture Destination)
        {
            typeof(RayStormRenderer).GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Renderer, new object[] { Source, Destination });
        }
    }

    public sealed class RayStormRendererTestBehaviour : RayStormBehaviour
    {
        public bool CollectScene;
        public RSRenderTask[] RenderTasks;
        public int FrameStartCount;
        public int FrameFinishCount;

        public override void OnFrameStartup(RSRenderContext Context)
        {
            FrameStartCount++;
        }

        public override void OnFrameFinished(RSRenderContext Context)
        {
            FrameFinishCount++;
        }

        public override void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneObject[] OutputSceneObject)
        {
            OutputSceneObject = CollectScene ? RSSceneUtility.CollectSceneObject(Context.MainCamera) : Array.Empty<RSSceneObject>();
        }

        public override void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            if (RenderTasks != null)
            {
                OutputRenderTask = RenderTasks;
                return;
            }
            OutputRenderTask = new RSRenderTask[2];
            for (int Pass = 0; Pass < OutputRenderTask.Length; ++Pass)
            {
                OutputRenderTask[Pass] = new RSRenderTask
                {
                    FrameID = Context.FrameID,
                    WindowSize = new Vector2Int(Context.RenderWidth, Context.RenderHeight),
                    SubpixelCountPerPixel = 1
                };
            }
        }
    }
}
