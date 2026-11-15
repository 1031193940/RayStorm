using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RayStorm
{
    public static class RayStormDenoisingValidation
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("RayStorm/Validate Denoising")]
        public static void Run()
        {
            Require(!Application.isPlaying, "Run denoising validation in Edit Mode.");
            ValidateHDRImage();
            ValidateRendererOutput();
            Debug.Log("RAYSTORM_DENOISING_VALIDATION_PASSED: HDR color, unchanged source and sample counts, finite output, and renderer output selection.");
        }

        public static void RunAll()
        {
            Run();
            RenderExample();
        }

        private static void ValidateHDRImage()
        {
            const int Width = 64;
            const int Height = 48;
            RenderTexture Source = CreateTexture(Width, Height);
            RenderTexture Destination = CreateTexture(Width, Height);
            Texture2D Upload = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            try
            {
                var Random = new System.Random(137);
                var Pixels = new Color[Width * Height];
                Color Expected = new Color(4, 2, 0.5f, 1);
                for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
                {
                    float Noise = (float)Random.NextDouble() * 1.2f - 0.6f;
                    Pixels[PixelIndex] = new Color(Expected.r + Noise, Expected.g + Noise * 0.5f, Expected.b + Noise * 0.125f, 1 + PixelIndex % 17);
                }
                Upload.SetPixels(Pixels);
                Upload.Apply(false, false);
                Graphics.CopyTexture(Upload, Source);
                Color[] Before = ReadPixels(Source);
                RSDenoiseUtility.Denoise(Source, Destination);
                Color[] After = ReadPixels(Source);
                Color[] Denoised = ReadPixels(Destination);
                AssertUnchanged(Before, After);
                ValidateOutput(Before, Denoised, true);

                double RawError = 0;
                double DenoisedError = 0;
                for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
                {
                    RawError += Math.Pow(Before[PixelIndex].r - Expected.r, 2);
                    DenoisedError += Math.Pow(Denoised[PixelIndex].r - Expected.r, 2);
                }
                Require(DenoisedError < RawError, "Denoising did not reduce noise in the known uniform HDR signal.");
                Debug.Log("RayStorm HDR denoising MSE: " + RawError / Pixels.Length + " -> " + DenoisedError / Pixels.Length);

                Expected = new Color(2, 4, 8, 32);
                for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
                    Pixels[PixelIndex] = Expected;
                Upload.SetPixels(Pixels);
                Upload.Apply(false, false);
                Graphics.CopyTexture(Upload, Source);
                Before = ReadPixels(Source);
                var Timer = System.Diagnostics.Stopwatch.StartNew();
                RSDenoiseUtility.Denoise(Source, Destination);
                Timer.Stop();
                Denoised = ReadPixels(Destination);
                AssertUnchanged(Before, ReadPixels(Source));
                ValidateOutput(Before, Denoised, true);
                Vector3 Mean = Vector3.zero;
                foreach (Color Pixel in Denoised)
                    Mean += new Vector3(Pixel.r, Pixel.g, Pixel.b);
                Mean /= Denoised.Length;
                Require(Mathf.Abs(Mean.x - 2) < 0.04f && Mathf.Abs(Mean.y - 4) < 0.08f && Mathf.Abs(Mean.z - 8) < 0.16f, "Denoising changed constant HDR radiance: " + Mean);
                Debug.Log("RayStorm constant HDR mean: " + Mean + "; OIDN utility milliseconds: " + Timer.Elapsed.TotalMilliseconds);
            }
            finally
            {
                Object.DestroyImmediate(Upload);
                ReleaseTexture(Source);
                ReleaseTexture(Destination);
            }
        }

        private static void ValidateRendererOutput()
        {
            Cubemap Sky = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, new Color(4, 2, 0.5f, 1));
            try
            {
                foreach (bool Enabled in new[] { false, true })
                {
                    RenderTexture CompletedOutput;
                    using (var Fixture = new RenderFixture(17, 13, 2, Enabled))
                    {
                        Fixture.Renderer.SkyboxTexture = Sky;
                        Fixture.Renderer.EnvironmentColorSpace = RSEnvironmentColorSpace.LinearRadiance;
                        Fixture.Render();
                        RSRenderContext Context = Fixture.Context;
                        Color[] Raw = ReadPixels(Context.RaytraceTexture);
                        AssertUnchanged(Fixture.Behaviour.BeforeDenoising, Raw);
                        foreach (Color Pixel in Raw)
                            Require(Pixel.a == 2, "Renderer lost the raw sample count.");
                        Require(Context.OutputTexture == (Enabled ? Context.DenoisedTexture : Context.RaytraceTexture), "Renderer selected the wrong output texture.");
                        Require((Context.DenoisedTexture != null) == Enabled, "Denoising toggle did not control the completed frame.");
                        if (Enabled)
                        {
                            ValidateOutput(Raw, ReadPixels(Context.OutputTexture), true);
                            RenderTexture PreviousResult = Context.DenoisedTexture;
                            Invoke(Fixture.Renderer, "BeginFrame");
                            Require(PreviousResult == null && Context.DenoisedTexture == null && Context.OutputTexture == Context.RaytraceTexture, "Restart retained a stale denoised frame.");
                        }
                        CompletedOutput = Context.OutputTexture;
                    }
                    Require(CompletedOutput == null, "Renderer did not release its completed output texture.");
                }
            }
            finally
            {
                Object.DestroyImmediate(Sky);
            }
        }

        [MenuItem("RayStorm/Render Denoising Comparison")]
        public static void RenderExample()
        {
            Require(!Application.isPlaying, "Render the denoising comparison in Edit Mode.");
            int Width = ReadSetting("RAYSTORM_DENOISING_WIDTH", 480);
            int Height = ReadSetting("RAYSTORM_DENOISING_HEIGHT", 360);
            int Samples = ReadSetting("RAYSTORM_DENOISING_SAMPLES", 32);
            string OutputDirectory = Environment.GetEnvironmentVariable("RAYSTORM_DENOISING_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationResults/Denoising"));
            string ScenePath = "Assets/RayStorm/Example/ExampleScene_CornellBox.unity";
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
                ScenePath = "Assets/RayStorm/Example/ExampleScene_Spheres.unity";
            Require(!SceneManager.GetSceneByPath(ScenePath).isLoaded, "Close one example scene before rendering the isolated comparison.");
            int Layer = FindUnusedLayer();
            Scene PreviousScene = SceneManager.GetActiveScene();
            Scene ExampleScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            UnityEngine.Random.State PreviousRandomState = UnityEngine.Random.state;
            try
            {
                Camera Template = null;
                foreach (GameObject Root in ExampleScene.GetRootGameObjects())
                {
                    foreach (Transform Child in Root.GetComponentsInChildren<Transform>(true))
                        Child.gameObject.layer = Layer;
                    if (Template == null)
                        Template = Root.GetComponentInChildren<Camera>(true);
                }
                Require(Template != null, "Example scene has no camera.");
                Directory.CreateDirectory(OutputDirectory);
                UnityEngine.Random.InitState(137);
                var Timer = System.Diagnostics.Stopwatch.StartNew();
                using (var Fixture = new RenderFixture(Width, Height, Samples, true, Template, ExampleScene))
                {
                    Fixture.Camera.cullingMask = 1 << Layer;
                    Fixture.Render();
                    Timer.Stop();
                    RSRenderContext Context = Fixture.Context;
                    Require(Context.DenoisedTexture != null && Context.OutputTexture == Context.DenoisedTexture, "Example did not produce denoised output.");
                    Color[] Raw = ReadPixels(Context.RaytraceTexture);
                    Color[] Denoised = ReadPixels(Context.OutputTexture);
                    AssertUnchanged(Fixture.Behaviour.BeforeDenoising, Raw);
                    ValidateOutput(Raw, Denoised, false);
                    foreach (Color Pixel in Raw)
                        Require(Pixel.a == Samples, "Example sample count differs from its requested spp.");
                    SaveImages(Context.RaytraceTexture, Path.Combine(OutputDirectory, "raw"));
                    SaveImages(Context.OutputTexture, Path.Combine(OutputDirectory, "denoised"));
                    File.WriteAllText(Path.Combine(OutputDirectory, "comparison.txt"), "Scene: " + ScenePath + "\nSize: " + Width + "x" + Height + "\nSamples per pixel (all pixels): " + Samples + "\nSeed: 137\nUnity: " + Application.unityVersion + "\nGPU: " + SystemInfo.graphicsDeviceName + "\nGraphics API: " + SystemInfo.graphicsDeviceType + "\nProject color space: " + QualitySettings.activeColorSpace + "\nDenoiser: Open Image Denoise, color only\nPNG: Reinhard + sRGB; EXR: linear RGBAFloat, alpha stores sample count.\nRaw and denoised outputs share the same path-traced frame.\nSource and sample-count preservation: passed\nFinite output: passed\nRender + postprocessing seconds (includes original HDR snapshot, excludes file export): " + Timer.Elapsed.TotalSeconds + "\n");
                }
                Debug.Log("RAYSTORM_DENOISING_COMPARISON_PASSED: " + Width + "x" + Height + ", " + Samples + " spp, " + Timer.Elapsed.TotalSeconds + " seconds; " + OutputDirectory);
            }
            finally
            {
                UnityEngine.Random.state = PreviousRandomState;
                if (PreviousScene.IsValid() && PreviousScene.isLoaded)
                    SceneManager.SetActiveScene(PreviousScene);
                EditorSceneManager.CloseScene(ExampleScene, true);
            }
        }

        private static int FindUnusedLayer()
        {
            uint UsedLayers = 0;
            foreach (GameObject SceneObject in Object.FindObjectsOfType<GameObject>())
                UsedLayers |= 1u << SceneObject.layer;
            for (int Layer = 31; Layer >= 0; --Layer)
                if ((UsedLayers & (1u << Layer)) == 0)
                    return Layer;
            throw new InvalidOperationException("No unused layer is available to isolate the example scene.");
        }

        private static int ReadSetting(string Name, int Default)
        {
            string Value = Environment.GetEnvironmentVariable(Name);
            if (string.IsNullOrEmpty(Value))
                return Default;
            Require(int.TryParse(Value, out int Result) && Result > 0, "Invalid positive integer setting: " + Name);
            return Result;
        }

        private static void SaveImages(RenderTexture Source, string BasePath)
        {
            Texture2D HDR = RSDataUtility.RenderTextureToTexture2D(Source, TextureFormat.RGBAFloat);
            Texture2D PNG = null;
            try
            {
                PNG = RSDataUtility.RenderTextureToTexture2D(Source, TextureFormat.RGBA32);
                File.WriteAllBytes(BasePath + ".exr", HDR.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                File.WriteAllBytes(BasePath + ".png", PNG.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(HDR);
                Object.DestroyImmediate(PNG);
            }
        }

        private static RenderTexture CreateTexture(int Width, int Height)
        {
            var Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true };
            Require(Texture.Create(), "Could not allocate validation texture.");
            return Texture;
        }

        public static Color[] ReadPixels(RenderTexture Source)
        {
            Texture2D Readback = RSDataUtility.RenderTextureToTexture2D(Source, TextureFormat.RGBAFloat);
            try
            {
                return Readback.GetPixels();
            }
            finally
            {
                Object.DestroyImmediate(Readback);
            }
        }

        private static void AssertUnchanged(Color[] Before, Color[] After)
        {
            Require(Before != null && Before.Length == After.Length, "Missing original HDR image.");
            for (int PixelIndex = 0; PixelIndex < Before.Length; ++PixelIndex)
                Require(Before[PixelIndex].Equals(After[PixelIndex]), "Denoising modified the original HDR buffer at pixel " + PixelIndex);
        }

        private static void ValidateOutput(Color[] Source, Color[] Result, bool RequireHDR)
        {
            float Maximum = 0;
            for (int PixelIndex = 0; PixelIndex < Result.Length; ++PixelIndex)
            {
                Color Pixel = Result[PixelIndex];
                Require(IsFinite(Pixel.r) && IsFinite(Pixel.g) && IsFinite(Pixel.b) && IsFinite(Pixel.a), "Denoising produced a non-finite pixel.");
                Require(Pixel.a == Source[PixelIndex].a, "Denoising changed the sample-count alpha channel.");
                Maximum = Mathf.Max(Maximum, Pixel.r, Pixel.g, Pixel.b);
            }
            Require(!RequireHDR || Maximum > 1, "Denoising clipped linear HDR radiance to the LDR range.");
        }

        private static bool IsFinite(float Value)
        {
            return !float.IsNaN(Value) && !float.IsInfinity(Value);
        }

        private static void ReleaseTexture(RenderTexture Texture)
        {
            Texture.Release();
            Object.DestroyImmediate(Texture);
        }

        private static void Invoke(RayStormRenderer Renderer, string MethodName)
        {
            try
            {
                typeof(RayStormRenderer).GetMethod(MethodName, PrivateInstance).Invoke(Renderer, null);
            }
            catch (TargetInvocationException Error)
            {
                throw Error.InnerException;
            }
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

        private sealed class RenderFixture : IDisposable
        {
            private readonly GameObject CameraObject;
            private readonly RenderTexture CameraTarget;
            private readonly Texture2D SynchronizationPixel;
            public readonly Camera Camera;
            public readonly RayStormRenderer Renderer;
            public readonly RayStormDenoisingTestBehaviour Behaviour;
            public RSRenderContext Context => (RSRenderContext)typeof(RayStormRenderer).GetField("Context", PrivateInstance).GetValue(Renderer);

            public RenderFixture(int Width, int Height, int Samples, bool DenoisingEnabled, Camera Template = null, Scene Scene = default)
            {
                CameraObject = new GameObject("RayStorm denoising validation") { hideFlags = HideFlags.HideAndDontSave };
                CameraObject.SetActive(false);
                if (Scene.IsValid())
                    SceneManager.MoveGameObjectToScene(CameraObject, Scene);
                Camera = CameraObject.AddComponent<Camera>();
                if (Template != null)
                {
                    Camera.CopyFrom(Template);
                    Camera.transform.SetPositionAndRotation(Template.transform.position, Template.transform.rotation);
                }
                else
                    Camera.cullingMask = 0;
                Camera.enabled = false;
                CameraTarget = new RenderTexture(Width, Height, 0);
                Camera.targetTexture = CameraTarget;
                SynchronizationPixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                Renderer = CameraObject.AddComponent<RayStormRenderer>();
                Renderer.enabled = false;
                Renderer.IsDenoisingEnabled = DenoisingEnabled;
                Renderer.IsDisplayRenderBlock = false;
                Renderer.IsDisplayRenderProgress = false;
                if (Template != null && Template.TryGetComponent(out RayStormRenderer TemplateRenderer))
                {
                    Renderer.SkyboxTexture = TemplateRenderer.SkyboxTexture;
                    Renderer.EnvironmentColorSpace = TemplateRenderer.EnvironmentColorSpace;
                }
                Behaviour = ScriptableObject.CreateInstance<RayStormDenoisingTestBehaviour>();
                Behaviour.FrameCount = 1;
                Behaviour.Samples = Samples;
                Renderer.RayStormBehaviour = Behaviour;
                Set(Renderer, "MaxTasksPerUpdate", 1);
                Set(Renderer, "MaximumQueuedRays", 1024);
                Set(Renderer, "DisplayRefreshInterval", float.PositiveInfinity);
                try
                {
                    Invoke(Renderer, "Awake");
                    Invoke(Renderer, "Start");
                    Require(Context != null && Context.IsRunning, "Renderer initialization failed.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Render()
            {
                int MaximumSteps = (Context.RenderWidth + 7) / 8 * ((Context.RenderHeight + 7) / 8) * ((Behaviour.Samples + 15) / 16) + 1;
                for (int Step = 0; Step < MaximumSteps && Context.IsRunning; ++Step)
                {
                    Invoke(Renderer, "Update");
                    RenderTexture Previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = Context.RaytraceTexture;
                        SynchronizationPixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    }
                    finally
                    {
                        RenderTexture.active = Previous;
                    }
                }
                Require(Context.FrameID == 1 && !Context.IsRunning, "Renderer did not complete the validation frame.");
            }

            public void Dispose()
            {
                Invoke(Renderer, "OnDestroy");
                Camera.targetTexture = null;
                Object.DestroyImmediate(CameraObject);
                Object.DestroyImmediate(CameraTarget);
                Object.DestroyImmediate(SynchronizationPixel);
                Object.DestroyImmediate(Behaviour);
            }
        }
    }

    public sealed class RayStormDenoisingTestBehaviour : RayStormBehaviour
    {
        public int Samples;
        public Color[] BeforeDenoising;

        public override void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            var Tasks = new List<RSRenderTask>();
            for (int Sample = 0; Sample < Samples; Sample += 16)
                AddRenderTasks(Context, Tasks, 8, Mathf.Min(16, Samples - Sample));
            OutputRenderTask = Tasks.ToArray();
        }

        public override void OnFramePostProcess(RSRenderContext Context)
        {
            BeforeDenoising = RayStormDenoisingValidation.ReadPixels(Context.RaytraceTexture);
        }
    }
}
