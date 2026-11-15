using System;
using UnityEngine;

namespace RayStorm.Editor
{
    public static class RayStormGpuValidation
    {
        public static void Run()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                throw new InvalidOperationException("RayStorm GPU validation requires compute shaders and ARGBFloat.");

            ValidateAccumulationAndDisplay();
            ValidateTracing();
            Debug.Log("RAYSTORM_GPU_VALIDATION_PASSED: weighted HDR accumulation, edge tiles, display, environment, linear albedo, HDR emission, BVH hit ties, two-sided diffuse and finite rough reflections.");
        }

        private static ComputeShader LoadShader(string ShaderName)
        {
            ComputeShader Shader = RSAssetUtility.LoadAsset<ComputeShader>(ShaderName);
            if (Shader == null)
                throw new InvalidOperationException("Missing validation shader: " + ShaderName);
            return UnityEngine.Object.Instantiate(Shader);
        }

        private static RenderTexture CreateTexture(int Width, int Height)
        {
            var Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            Texture.Create();
            RSDataUtility.ClearRenderTexture(Texture, Color.clear);
            return Texture;
        }

        private static Color[] ReadTexture(RenderTexture Texture)
        {
            Texture2D Readback = RSDataUtility.RenderTextureToTexture2D(Texture, TextureFormat.RGBAFloat);
            try
            {
                return Readback.GetPixels();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(Readback);
            }
        }

        private static void AssertNear(float Actual, float Expected, string Label, float Tolerance = 0.0001f)
        {
            if (!(Mathf.Abs(Actual - Expected) <= Tolerance))
                throw new InvalidOperationException(Label + ": expected " + Expected + ", got " + Actual);
        }

        private static void Accumulate(ComputeShader Shader, RenderTexture Texture, ComputeBuffer Buffer, int PositionX, int PositionY, int Width, int Height, int SamplesPerPixel, float SampleValue, bool AlternateBlue = false)
        {
            var Samples = new Vector4[Width * Height * SamplesPerPixel];
            for (int SampleIndex = 0; SampleIndex < Samples.Length; ++SampleIndex)
                Samples[SampleIndex] = new Vector4(SampleValue, SampleValue, AlternateBlue ? (SampleIndex % 2) * 2 * SampleValue : SampleValue, 1);
            Buffer.SetData(Samples, 0, 0, Samples.Length);
            int Kernel = Shader.FindKernel("CSKernelMain");
            Shader.SetInt("RaytraceWindowPosX", PositionX);
            Shader.SetInt("RaytraceWindowPosY", PositionY);
            Shader.SetInt("RaytraceWindowSizeX", Width);
            Shader.SetInt("RaytraceWindowSizeY", Height);
            Shader.SetInt("SubpixelCountPerPixel", SamplesPerPixel);
            Shader.SetBuffer(Kernel, "RaytraceWindowBuffer", Buffer);
            Shader.SetTexture(Kernel, "RaytraceOutputTexture", Texture);
            Shader.GetKernelThreadGroupSizes(Kernel, out uint ThreadCount, out _, out _);
            Shader.Dispatch(Kernel, Mathf.CeilToInt(Width * Height / (float)ThreadCount), 1, 1);
        }

        private static void Display(ComputeShader Shader, RenderTexture Source, RenderTexture Target, bool Overlay, bool Gamma, RectInt Block = default, float Progress = -1)
        {
            int Kernel = Shader.FindKernel("CSKernelMain");
            Shader.SetTexture(Kernel, "Source", Source);
            Shader.SetTexture(Kernel, "Target", Target);
            Shader.SetInt("BlockPosX", Block.x);
            Shader.SetInt("BlockPosY", Block.y);
            Shader.SetInt("BlockSizeX", Block.width);
            Shader.SetInt("BlockSizeY", Block.height);
            Shader.SetInt("ShowRenderBlock", Overlay ? 1 : 0);
            Shader.SetInt("ShowRenderProgress", Progress >= 0 ? 1 : 0);
            Shader.SetFloat("RenderProgress", Progress);
            Shader.SetInt("OutputIsGamma", Gamma ? 1 : 0);
            Shader.GetKernelThreadGroupSizes(Kernel, out uint Width, out uint Height, out _);
            Shader.Dispatch(Kernel, Mathf.CeilToInt(Target.width / (float)Width), Mathf.CeilToInt(Target.height / (float)Height), 1);
        }

        private static void ValidateAccumulationAndDisplay()
        {
            ComputeShader Additive = LoadShader("RayStormAdditive");
            ComputeShader DisplayShader = LoadShader("RayStormBlock");
            RenderTexture Accumulation = CreateTexture(3, 2);
            RenderTexture Output = CreateTexture(3, 2);
            RenderTexture Statistics = CreateTexture(3, 2);
            ComputeBuffer SampleBuffer = new ComputeBuffer(128, RSOutputData.GetDataSize());
            try
            {
                Additive.SetTexture(Additive.FindKernel("CSKernelMain"), "RaytraceStatistics", Statistics);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 1, 0);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 1, 0);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 1, 1);
                foreach (Color Pixel in ReadTexture(Accumulation))
                {
                    AssertNear(Pixel.r, 1.0f / 3.0f, "Three-batch mean");
                    AssertNear(Pixel.a, 3, "Three-batch count");
                }

                RSDataUtility.ClearRenderTexture(Accumulation, Color.clear);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 1, 2);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 3, 4);
                foreach (Color Pixel in ReadTexture(Accumulation))
                {
                    AssertNear(Pixel.r, 3.5f, "HDR weighted mean");
                    AssertNear(Pixel.a, 4, "Actual sample count");
                }

                RSDataUtility.ClearRenderTexture(Accumulation, Color.clear);
                Accumulate(Additive, Accumulation, SampleBuffer, 2, 1, 2, 2, 1, 8);
                Color[] EdgePixels = ReadTexture(Accumulation);
                for (int PixelIndex = 0; PixelIndex < EdgePixels.Length; ++PixelIndex)
                {
                    AssertNear(EdgePixels[PixelIndex].r, PixelIndex == 5 ? 8 : 0, "Clipped edge tile RGB");
                    AssertNear(EdgePixels[PixelIndex].a, PixelIndex == 5 ? 1 : 0, "Clipped edge tile count");
                }

                RSDataUtility.ClearRenderTexture(Accumulation, Color.clear);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 3, 2, 1, 4);
                Display(DisplayShader, Accumulation, Output, false, false);
                foreach (Color Pixel in ReadTexture(Output))
                    AssertNear(Pixel.r, 0.8f, "Linear display mapping");
                Display(DisplayShader, Accumulation, Output, false, true);
                foreach (Color Pixel in ReadTexture(Output))
                    AssertNear(Pixel.r, Mathf.LinearToGammaSpace(0.8f), "sRGB display mapping");
                ValidateDisplayFeedback(DisplayShader, 20, 16);
                ValidateDisplayFeedback(DisplayShader, 2, 2);
                ValidateDisplayFeedback(DisplayShader, 1, 1);

                RSDataUtility.ClearRenderTexture(Accumulation, Color.clear);
                RSDataUtility.ClearRenderTexture(Statistics, Color.clear);
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 1, 1, 127, 2);
                Accumulate(Additive, Accumulation, SampleBuffer, 1, 0, 1, 1, 127, 2, true);
                AssertNear(ReadTexture(Statistics)[0].a, 0, "Below minimum sample count");
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 2, 1, 1, 2);
                Color[] Convergence = ReadTexture(Statistics);
                AssertNear(Convergence[0].a, 1, "Constant pixel converges at 128 spp");
                AssertNear(Convergence[1].b, 507.96875f, "Blue channel M2", 0.001f);
                AssertNear(Convergence[1].a, 0, "Blue channel variance prevents convergence");
                Accumulate(Additive, Accumulation, SampleBuffer, 0, 0, 2, 1, 1, 8);
                Color[] Pixels = ReadTexture(Accumulation);
                AssertNear(Pixels[0].r, 2, "Converged pixel retains its mean");
                AssertNear(Pixels[0].a, 128, "Converged pixel retains its sample count");
                AssertNear(Pixels[1].a, 129, "Unconverged pixel continues sampling");
            }
            finally
            {
                SampleBuffer.Release();
                Accumulation.Release();
                Output.Release();
                Statistics.Release();
                UnityEngine.Object.DestroyImmediate(Accumulation);
                UnityEngine.Object.DestroyImmediate(Output);
                UnityEngine.Object.DestroyImmediate(Statistics);
                UnityEngine.Object.DestroyImmediate(Additive);
                UnityEngine.Object.DestroyImmediate(DisplayShader);
            }
        }

        private static void ValidateDisplayFeedback(ComputeShader Shader, int Width, int Height)
        {
            RenderTexture Source = CreateTexture(Width, Height);
            RenderTexture Target = CreateTexture(Width, Height);
            Texture2D Export = null;
            Color Radiance = new Color(4, 2, 0.25f, 7);
            Color Mapped = new Color(0.8f, 2.0f / 3.0f, 0.2f, 1);
            RectInt Block = Width == 20 ? new RectInt(2, 2, 16, 12) : new RectInt(0, 0, 1, 1);
            try
            {
                RSDataUtility.ClearRenderTexture(Source, Radiance);
                foreach (bool Gamma in new[] { false, true })
                {
                    Color Background = Gamma ? Mapped.gamma : Mapped;
                    Display(Shader, Source, Target, true, Gamma, Block);
                    Color[] Pixels = ReadTexture(Target);
                    if (Width == 20)
                    {
                        AssertNear(Vector4.Distance(Pixels[3 * Width + 3], Color.white), 0, "White corner");
                        AssertNear(Vector4.Distance(Pixels[3 * Width + 4], Color.white), 0, "Horizontal corner arm");
                        AssertNear(Vector4.Distance(Pixels[4 * Width + 3], Color.white), 0, "Vertical corner arm");
                        AssertNear(Vector4.Distance(Pixels[3 * Width + 2], Color.black), 0, "Corner outer edge");
                        AssertNear(Vector4.Distance(Pixels[4 * Width + 4], Color.black), 0, "Corner inner edge");
                        AssertNear(Vector4.Distance(Pixels[12 * Width + 16], Color.white), 0, "Opposite corner");
                        AssertNear(Vector4.Distance(Pixels[8 * Width + 10], Background), 0, "Corner preserves block interior");
                        AssertNear(Vector4.Distance(Pixels[8 * Width + 2], Background), 0, "Corner leaves edge center clear");
                        AssertNear(Vector4.Distance(Pixels[3 * Width + 1], Background), 0, "Corner stays inside block");
                    }
                    else
                        AssertNear(Vector4.Distance(Pixels[0], Color.white), 0, "Single-pixel corner");

                    Display(Shader, Source, Target, true, Gamma, new RectInt(Width - 1, Height - 1, 1, 1));
                    Pixels = ReadTexture(Target);
                    AssertNear(Vector4.Distance(Pixels[Pixels.Length - 1], Color.white), 0, "Corner at image boundary");
                    if (Pixels.Length > 1)
                        AssertNear(Vector4.Distance(Pixels[Pixels.Length - 2], Background), 0, "Boundary corner stays inside block");

                    foreach (float Progress in new[] { 0.0f, 0.5f, 1.0f })
                    {
                        Display(Shader, Source, Target, false, Gamma, default, Progress);
                        Pixels = ReadTexture(Target);
                        for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
                        {
                            int X = PixelIndex % Width;
                            int Y = PixelIndex / Width;
                            Color Expected = Y < Mathf.Min(3, Height) ? Y < Mathf.Min(2, Height) && X < (int)(Progress * Width) ? Color.white : Color.black : Background;
                            AssertNear(Vector4.Distance(Pixels[PixelIndex], Expected), 0, "Progress bar and separator");
                        }
                    }
                    Display(Shader, Source, Target, false, Gamma, Block);
                    foreach (Color Pixel in ReadTexture(Target))
                        AssertNear(Vector4.Distance(Pixel, Background), 0, "Disabled feedback restores image");
                }
                foreach (Color Pixel in ReadTexture(Source))
                    AssertNear(Vector4.Distance(Pixel, Radiance), 0, "Feedback preserves source RGBA");
                Export = RSDataUtility.RenderTextureToTexture2D(Source, TextureFormat.RGBA32);
                foreach (Color Pixel in Export.GetPixels())
                    AssertNear(Vector4.Distance(Pixel, Mapped.gamma), 0, "Export excludes feedback", 0.005f);
            }
            finally
            {
                Source.Release();
                Target.Release();
                UnityEngine.Object.DestroyImmediate(Source);
                UnityEngine.Object.DestroyImmediate(Target);
                UnityEngine.Object.DestroyImmediate(Export);
            }
        }

        private static void ValidateTracing()
        {
            const int RoughReflectionRayCount = 32768;
            ComputeShader Shader = LoadShader("RayStormValidation");
            Cubemap Environment = RSDataUtility.CreateSolidCubemap(4, TextureFormat.RGBAFloat, false, new Color(4, 4, 4, 1));
            Material UnityMaterial = new Material(UnityEngine.Shader.Find("Standard"));
            var Vertices = new[]
            {
                new RSVertexData { Position = new Vector3(-10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(0, 10, 0), Normal = Vector3.forward }
            };
            var Geometries = new[]
            {
                new RSGeometryData { Indice0 = 0, Indice1 = 1, Indice2 = 2 },
                new RSGeometryData { Indice0 = 0, Indice1 = 1, Indice2 = 2, MaterialID = 1 }
            };
            using (var VertexBuffer = new ComputeBuffer(3, RSVertexData.GetDataSize()))
            using (var GeometryBuffer = new ComputeBuffer(2, RSGeometryData.GetDataSize()))
            using (var BVHBuffer = new ComputeBuffer(3, RSBVHData.GetDataSize()))
            using (var GeometryIndexBuffer = new ComputeBuffer(2, sizeof(int)))
            using (var MaterialBuffer = new ComputeBuffer(2, RSMaterialData.GetDataSize()))
            using (var SourceBuffer = new ComputeBuffer(RoughReflectionRayCount, RSSourceData.GetDataSize()))
            using (var ResultBuffer = new ComputeBuffer(RoughReflectionRayCount, RSOutputData.GetDataSize()))
            using (var LightSampler = new RSLightSampler())
            {
                try
                {
                    VertexBuffer.SetData(Vertices);
                    GeometryBuffer.SetData(Geometries);
                    // Visit geometry 1 first so the equal-distance hit on geometry 0 must replace it.
                    var Node = new RSBVHData { Minimum = new Vector3(-11, -11, -1), Maximum = new Vector3(11, 11, 1) };
                    var Root = Node;
                    Root.StartID = 1;
                    Node.GeometryCount = 1;
                    var Right = Node;
                    Right.StartID = 1;
                    BVHBuffer.SetData(new[] { Root, Node, Right });
                    GeometryIndexBuffer.SetData(new[] { 1, 0 });
                    SourceBuffer.SetData(new[] { new RSSourceData { Position = Vector3.forward, Direction = Vector3.forward, Energy = Vector3.one }, new RSSourceData { Position = Vector3.forward, Direction = Vector3.back, Energy = Vector3.one }, new RSSourceData { Position = Vector3.back, Direction = Vector3.forward, Energy = Vector3.one } });

                    int Kernel = Shader.FindKernel("CSKernelTraceSources");
                    LightSampler.Build(Vertices, Geometries, new[] { RSMaterialData.Default, RSMaterialData.Default }, Array.Empty<Light>(), null, false, 1, null);
                    LightSampler.Bind(Shader, Kernel);
                    Shader.SetInt("RayCount", 3);
                    Shader.SetInt("RSRandomSeed", 12345);
                    Shader.SetInt("SceneBVHCount", 3);
                    Shader.SetBuffer(Kernel, "RSSceneVertexBuffer", VertexBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneGeometryBuffer", GeometryBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneBVHBuffer", BVHBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneBVHGeometryIDBuffer", GeometryIndexBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneMaterialBuffer", MaterialBuffer);
                    Shader.SetBuffer(Kernel, "RayStormSourceBuffer", SourceBuffer);
                    Shader.SetBuffer(Kernel, "RayStormOutputBuffer", ResultBuffer);
                    Shader.SetTexture(Kernel, "RSSkyboxTexture", Environment);
                    Shader.SetInt("RSEnvironmentIsGamma", 0);

                    var OtherMaterial = new RSMaterialData { Emission = Vector3.one * 9, Roughness = 1, IndexOfRefraction = 1.5f };
                    MaterialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.one, Roughness = 1, IndexOfRefraction = 1.5f }, OtherMaterial });
                    Shader.Dispatch(Kernel, 1, 1, 1);
                    var Results = new RSOutputData[3];
                    ResultBuffer.GetData(Results);
                    for (int RayIndex = 0; RayIndex < Results.Length; ++RayIndex)
                        AssertNear(Results[RayIndex].Data.x, 4, "HDR environment / front-back diffuse ray " + RayIndex);

                    MaterialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.zero, Emission = Vector3.one * 3, Roughness = 1, IndexOfRefraction = 1.5f }, OtherMaterial });
                    Shader.Dispatch(Kernel, 1, 1, 1);
                    ResultBuffer.GetData(Results);
                    AssertNear(Results[1].Data.x, 3, "Lower geometry ID wins equal-distance hit");
                    AssertNear(Results[2].Data.x, 3, "Backface emission");

                    var MaterialTargets = new UnityEngine.Object[] { UnityMaterial };
                    UnityEditor.MaterialEditor.GetMaterialProperty(MaterialTargets, "_Color").colorValue = new Color(0.5f, 0.25f, 0.75f, 1);
                    UnityEditor.MaterialEditor.GetMaterialProperty(MaterialTargets, "_EmissionColor").colorValue = new Color(0.18f, 1, 4, 1);
                    RSMaterialData ImportedMaterial = RSMaterialData.CreateFromUnityMaterial(UnityMaterial);
                    AssertNear(Vector3.Distance(ImportedMaterial.Albedo, new Vector3(0.21404114f, 0.05087609f, 0.52252155f)), 0, "Linear albedo");
                    AssertNear(Vector3.Distance(ImportedMaterial.Emission, new Vector3(0.18f, 1, 4)), 0, "HDR emission");
                    ImportedMaterial.Albedo = Vector3.zero;
                    ImportedMaterial.Metallic = 0;
                    ImportedMaterial.Roughness = 1;
                    MaterialBuffer.SetData(new[] { ImportedMaterial, OtherMaterial });
                    Shader.Dispatch(Kernel, 1, 1, 1);
                    ResultBuffer.GetData(Results);
                    for (int RayIndex = 1; RayIndex < Results.Length; ++RayIndex)
                        AssertNear(Vector3.Distance(Results[RayIndex].Data, new Vector3(0.18f, 1, 4)), 0, "Imported emission");

                    var RoughSources = new RSSourceData[RoughReflectionRayCount];
                    var RoughSource = new RSSourceData { Position = Vector3.forward, Direction = Vector3.back, Energy = Vector3.one };
                    for (int RayIndex = 0; RayIndex < RoughSources.Length; ++RayIndex)
                        RoughSources[RayIndex] = RoughSource;
                    SourceBuffer.SetData(RoughSources);
                    MaterialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.one, Metallic = 1, Roughness = 0.2f }, OtherMaterial });
                    Shader.SetInt("RayCount", RoughReflectionRayCount);
                    Shader.GetKernelThreadGroupSizes(Kernel, out uint ThreadCount, out _, out _);
                    Shader.Dispatch(Kernel, Mathf.CeilToInt(RoughReflectionRayCount / (float)ThreadCount), 1, 1);

                    var RoughResults = new RSOutputData[RoughReflectionRayCount];
                    ResultBuffer.GetData(RoughResults);
                    double RadianceSum = 0.0;
                    for (int RayIndex = 0; RayIndex < RoughResults.Length; ++RayIndex)
                    {
                        Vector4 Result = RoughResults[RayIndex].Data;
                        float Radiance = Result.x + Result.y + Result.z;
                        if (float.IsNaN(Radiance) || float.IsInfinity(Radiance))
                            throw new InvalidOperationException("Non-finite rough reflection at ray " + RayIndex + ": " + Result);
                        RadianceSum += Radiance;
                    }
                    AssertNear((float)(RadianceSum / (3 * RoughReflectionRayCount)), 4.0f, "Mean rough metal reflection", 0.1f);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(Environment);
                    UnityEngine.Object.DestroyImmediate(UnityMaterial);
                    UnityEngine.Object.DestroyImmediate(Shader);
                }
            }
        }
    }
}
