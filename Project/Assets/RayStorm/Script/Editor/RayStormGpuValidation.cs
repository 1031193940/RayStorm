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
            Debug.Log("RAYSTORM_GPU_VALIDATION_PASSED: weighted HDR accumulation, edge tiles, display, environment, emission and two-sided diffuse.");
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

        private static void Accumulate(ComputeShader Shader, RenderTexture Texture, ComputeBuffer Buffer, int PositionX, int PositionY,
            int Width, int Height, int SamplesPerPixel, float SampleValue)
        {
            var Samples = new Vector4[Width * Height * SamplesPerPixel];
            for (int SampleIndex = 0; SampleIndex < Samples.Length; ++SampleIndex)
                Samples[SampleIndex] = new Vector4(SampleValue, SampleValue, SampleValue, 1);
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

        private static void Display(ComputeShader Shader, RenderTexture Source, RenderTexture Target, bool Overlay, bool Gamma)
        {
            int Kernel = Shader.FindKernel("CSKernelMain");
            Shader.SetTexture(Kernel, "Source", Source);
            Shader.SetTexture(Kernel, "Target", Target);
            Shader.SetInt("BlockPosX", 2);
            Shader.SetInt("BlockPosY", 1);
            Shader.SetInt("BlockSizeX", 1);
            Shader.SetInt("BlockSizeY", 1);
            Shader.SetVector("BlockColor", new Vector4(0, 1, 1, 1));
            Shader.SetInt("ShowRenderBlock", Overlay ? 1 : 0);
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
            ComputeBuffer SampleBuffer = new ComputeBuffer(Accumulation.width * Accumulation.height * 3, RSOutputData.GetDataSize());
            try
            {
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
                Display(DisplayShader, Accumulation, Output, true, false);
                Color[] HighlightedPixels = ReadTexture(Output);
                AssertNear(HighlightedPixels[5].r, 0, "Current block overlay");
                AssertNear(HighlightedPixels[4].r, 0.8f, "Overlay boundary");
                Display(DisplayShader, Accumulation, Output, false, false);
                AssertNear(ReadTexture(Output)[5].r, 0.8f, "Completed frame removes overlay");
            }
            finally
            {
                SampleBuffer.Release();
                Accumulation.Release();
                Output.Release();
                UnityEngine.Object.DestroyImmediate(Accumulation);
                UnityEngine.Object.DestroyImmediate(Output);
                UnityEngine.Object.DestroyImmediate(Additive);
                UnityEngine.Object.DestroyImmediate(DisplayShader);
            }
        }

        private static void ValidateTracing()
        {
            ComputeShader Shader = LoadShader("RayStormRaytracer");
            Cubemap Environment = RSDataUtility.CreateSolidCubemap(4, TextureFormat.RGBAFloat, false, new Color(4, 4, 4, 1));
            var Vertices = new[]
            {
                new RSVertexData { Position = new Vector3(-10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(0, 10, 0), Normal = Vector3.forward }
            };
            using (var VertexBuffer = new ComputeBuffer(3, RSVertexData.GetDataSize()))
            using (var GeometryBuffer = new ComputeBuffer(1, RSGeometryData.GetDataSize()))
            using (var OctreeBuffer = new ComputeBuffer(1, RSOctreeData.GetDataSize()))
            using (var GeometryIndexBuffer = new ComputeBuffer(1, sizeof(int)))
            using (var MaterialBuffer = new ComputeBuffer(1, RSMaterialData.GetDataSize()))
            using (var SourceBuffer = new ComputeBuffer(3, RSSourceData.GetDataSize()))
            using (var ResultBuffer = new ComputeBuffer(3, RSOutputData.GetDataSize()))
            {
                try
                {
                    VertexBuffer.SetData(Vertices);
                    GeometryBuffer.SetData(new[] { new RSGeometryData { Indice0 = 0, Indice1 = 1, Indice2 = 2 } });
                    OctreeBuffer.SetData(new[] { new RSOctreeData { ParentTreeID = -1, GeometryCount = 1, Size = new Vector3(22, 22, 2) } });
                    GeometryIndexBuffer.SetData(new[] { 0 });
                    SourceBuffer.SetData(new[]
                    {
                        new RSSourceData { Position = Vector3.forward, Direction = Vector3.forward, Energy = Vector3.one },
                        new RSSourceData { Position = Vector3.forward, Direction = Vector3.back, Energy = Vector3.one },
                        new RSSourceData { Position = Vector3.back, Direction = Vector3.forward, Energy = Vector3.one }
                    });

                    int Kernel = Shader.FindKernel("CSKernelMain");
                    Shader.SetInt("RayCount", 3);
                    Shader.SetInt("RSRandomSeed", 12345);
                    Shader.SetInt("SceneOctreeCount", 1);
                    Shader.SetInt("SceneGeometryCount", 1);
                    Shader.SetBuffer(Kernel, "RSSceneVertexBuffer", VertexBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneGeometryBuffer", GeometryBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneOctreeBuffer", OctreeBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneOctreeGeometryIDBuffer", GeometryIndexBuffer);
                    Shader.SetBuffer(Kernel, "RSSceneMaterialBuffer", MaterialBuffer);
                    Shader.SetBuffer(Kernel, "RayStormSourceBuffer", SourceBuffer);
                    Shader.SetBuffer(Kernel, "RayStormOutputBuffer", ResultBuffer);
                    Shader.SetTexture(Kernel, "RSSkyboxTexture", Environment);

                    MaterialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.one, Roughness = 1, IndexOfRefraction = 1.5f } });
                    Shader.Dispatch(Kernel, 1, 1, 1);
                    var Results = new RSOutputData[3];
                    ResultBuffer.GetData(Results);
                    for (int RayIndex = 0; RayIndex < Results.Length; ++RayIndex)
                        AssertNear(Results[RayIndex].Data.x, 4, "HDR environment / front-back diffuse ray " + RayIndex);

                    MaterialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.zero, Emission = Vector3.one * 3, Roughness = 1, IndexOfRefraction = 1.5f } });
                    Shader.Dispatch(Kernel, 1, 1, 1);
                    ResultBuffer.GetData(Results);
                    AssertNear(Results[1].Data.x, 3, "Emission uses incoming path throughput");
                    AssertNear(Results[2].Data.x, 3, "Backface emission");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(Environment);
                    UnityEngine.Object.DestroyImmediate(Shader);
                }
            }
        }
    }
}
