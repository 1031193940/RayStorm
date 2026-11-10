using System;
using UnityEditor;
using UnityEngine;

namespace RayStorm.Editor
{
    // Run with -executeMethod RayStorm.Editor.RayStormGpuValidation.Run (GPU required).
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

        private static ComputeShader LoadShader(string shaderName)
        {
            foreach (string assetGuid in AssetDatabase.FindAssets(shaderName + " t:ComputeShader"))
            {
                ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(assetGuid));
                if (shader != null && shader.name == shaderName)
                    return UnityEngine.Object.Instantiate(shader);
            }
            throw new InvalidOperationException("Missing validation shader: " + shaderName);
        }

        private static RenderTexture CreateTexture(int width, int height)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            texture.Create();
            ClearTexture(texture);
            return texture;
        }

        private static void ClearTexture(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(false, true, Color.clear);
            }
            finally { RenderTexture.active = previous; }
        }

        private static Color[] ReadTexture(RenderTexture texture)
        {
            var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static void AssertNear(float actual, float expected, string label, float tolerance = 0.0001f)
        {
            if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
        }

        private static void Accumulate(ComputeShader shader, RenderTexture texture, int positionX, int positionY,
            int width, int height, int samplesPerPixel, float sampleValue)
        {
            var samples = new Vector4[width * height * samplesPerPixel];
            for (int sampleIndex = 0; sampleIndex < samples.Length; ++sampleIndex)
                samples[sampleIndex] = new Vector4(sampleValue, sampleValue, sampleValue, 1);
            using (var buffer = new ComputeBuffer(samples.Length, sizeof(float) * 4))
            {
                buffer.SetData(samples);
                int kernel = shader.FindKernel("CSKernelMain");
                shader.SetInt("RaytraceWindowPosX", positionX);
                shader.SetInt("RaytraceWindowPosY", positionY);
                shader.SetInt("RaytraceWindowSizeX", width);
                shader.SetInt("RaytraceWindowSizeY", height);
                shader.SetInt("SubpixelCountPerPixel", samplesPerPixel);
                shader.SetBuffer(kernel, "RaytraceWindowBuffer", buffer);
                shader.SetTexture(kernel, "RaytraceOutputTexture", texture);
                shader.GetKernelThreadGroupSizes(kernel, out uint threadCount, out _, out _);
                shader.Dispatch(kernel, Mathf.CeilToInt(width * height / (float)threadCount), 1, 1);
                // Readback also synchronizes the buffer's lifetime with this dispatch.
                ReadTexture(texture);
            }
        }

        private static void Display(ComputeShader shader, RenderTexture source, RenderTexture target, bool overlay, bool gamma)
        {
            int kernel = shader.FindKernel("CSKernelMain");
            shader.SetTexture(kernel, "Source", source);
            shader.SetTexture(kernel, "Target", target);
            shader.SetInt("BlockPosX", 2);
            shader.SetInt("BlockPosY", 1);
            shader.SetInt("BlockSizeX", 1);
            shader.SetInt("BlockSizeY", 1);
            shader.SetVector("BlockColor", new Vector4(0, 1, 1, 1));
            shader.SetInt("ShowRenderBlock", overlay ? 1 : 0);
            shader.SetInt("OutputIsGamma", gamma ? 1 : 0);
            shader.GetKernelThreadGroupSizes(kernel, out uint width, out uint height, out _);
            shader.Dispatch(kernel, Mathf.CeilToInt(target.width / (float)width), Mathf.CeilToInt(target.height / (float)height), 1);
        }

        private static void ValidateAccumulationAndDisplay()
        {
            ComputeShader additive = LoadShader("RayStormAdditive");
            ComputeShader display = LoadShader("RayStormBlock");
            RenderTexture accumulation = CreateTexture(3, 2);
            RenderTexture output = CreateTexture(3, 2);
            try
            {
                Accumulate(additive, accumulation, 0, 0, 3, 2, 1, 0);
                Accumulate(additive, accumulation, 0, 0, 3, 2, 1, 0);
                Accumulate(additive, accumulation, 0, 0, 3, 2, 1, 1);
                foreach (Color pixel in ReadTexture(accumulation))
                {
                    AssertNear(pixel.r, 1.0f / 3.0f, "Three-batch mean");
                    AssertNear(pixel.a, 3, "Three-batch count");
                }

                ClearTexture(accumulation);
                Accumulate(additive, accumulation, 0, 0, 3, 2, 1, 2);
                Accumulate(additive, accumulation, 0, 0, 3, 2, 3, 4);
                foreach (Color pixel in ReadTexture(accumulation))
                {
                    AssertNear(pixel.r, 3.5f, "HDR weighted mean");
                    AssertNear(pixel.a, 4, "Actual sample count");
                }

                ClearTexture(accumulation);
                Accumulate(additive, accumulation, 2, 1, 2, 2, 1, 8);
                Color[] edgePixels = ReadTexture(accumulation);
                for (int pixelIndex = 0; pixelIndex < edgePixels.Length; ++pixelIndex)
                {
                    AssertNear(edgePixels[pixelIndex].r, pixelIndex == 5 ? 8 : 0, "Clipped edge tile RGB");
                    AssertNear(edgePixels[pixelIndex].a, pixelIndex == 5 ? 1 : 0, "Clipped edge tile count");
                }

                ClearTexture(accumulation);
                Accumulate(additive, accumulation, 0, 0, 3, 2, 1, 4);
                Display(display, accumulation, output, false, false);
                foreach (Color pixel in ReadTexture(output)) AssertNear(pixel.r, 0.8f, "Linear display mapping");
                Display(display, accumulation, output, false, true);
                foreach (Color pixel in ReadTexture(output)) AssertNear(pixel.r, Mathf.LinearToGammaSpace(0.8f), "sRGB display mapping");
                Display(display, accumulation, output, true, false);
                Color[] highlightedPixels = ReadTexture(output);
                AssertNear(highlightedPixels[5].r, 0, "Current block overlay");
                AssertNear(highlightedPixels[4].r, 0.8f, "Overlay boundary");
                Display(display, accumulation, output, false, false);
                AssertNear(ReadTexture(output)[5].r, 0.8f, "Completed frame removes overlay");
            }
            finally
            {
                accumulation.Release();
                output.Release();
                UnityEngine.Object.DestroyImmediate(accumulation);
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(additive);
                UnityEngine.Object.DestroyImmediate(display);
            }
        }

        private static void ValidateTracing()
        {
            ComputeShader shader = LoadShader("RayStormRaytracer");
            var environment = new Cubemap(4, TextureFormat.RGBAFloat, false);
            var vertices = new[]
            {
                new RSVertexData { Position = new Vector3(-10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(10, -10, 0), Normal = Vector3.forward },
                new RSVertexData { Position = new Vector3(0, 10, 0), Normal = Vector3.forward }
            };
            using (var vertexBuffer = new ComputeBuffer(3, RSVertexData.GetDataSize()))
            using (var geometryBuffer = new ComputeBuffer(1, RSGeometryData.GetDataSize()))
            using (var octreeBuffer = new ComputeBuffer(1, RSOctreeData.GetDataSize()))
            using (var geometryIndexBuffer = new ComputeBuffer(1, sizeof(int)))
            using (var materialBuffer = new ComputeBuffer(1, RSMaterialData.GetDataSize()))
            using (var sourceBuffer = new ComputeBuffer(3, RSSourceData.GetDataSize()))
            using (var resultBuffer = new ComputeBuffer(3, RSOutputData.GetDataSize()))
            {
                try
                {
                    var environmentPixels = new Color[16];
                    for (int pixelIndex = 0; pixelIndex < environmentPixels.Length; ++pixelIndex)
                        environmentPixels[pixelIndex] = new Color(4, 4, 4, 1);
                    for (int face = 0; face < 6; ++face) environment.SetPixels(environmentPixels, (CubemapFace)face);
                    environment.Apply();

                    vertexBuffer.SetData(vertices);
                    geometryBuffer.SetData(new[] { new RSGeometryData { Indice0 = 0, Indice1 = 1, Indice2 = 2 } });
                    octreeBuffer.SetData(new[] { new RSOctreeData { ParentTreeID = -1, GeometryCount = 1, Size = new Vector3(22, 22, 2) } });
                    geometryIndexBuffer.SetData(new[] { 0 });
                    sourceBuffer.SetData(new[]
                    {
                        new RSSourceData { Position = Vector3.forward, Direction = Vector3.forward, Energy = Vector3.one },
                        new RSSourceData { Position = Vector3.forward, Direction = Vector3.back, Energy = Vector3.one },
                        new RSSourceData { Position = Vector3.back, Direction = Vector3.forward, Energy = Vector3.one }
                    });

                    int kernel = shader.FindKernel("CSKernelMain");
                    shader.SetInt("RayCount", 3);
                    shader.SetInt("RSRandomSeed", 12345);
                    shader.SetInt("SceneOctreeCount", 1);
                    shader.SetInt("SceneGeometryCount", 1);
                    shader.SetBuffer(kernel, "RSSceneVertexBuffer", vertexBuffer);
                    shader.SetBuffer(kernel, "RSSceneGeometryBuffer", geometryBuffer);
                    shader.SetBuffer(kernel, "RSSceneOctreeBuffer", octreeBuffer);
                    shader.SetBuffer(kernel, "RSSceneOctreeGeometryIDBuffer", geometryIndexBuffer);
                    shader.SetBuffer(kernel, "RSSceneMaterialBuffer", materialBuffer);
                    shader.SetBuffer(kernel, "RayStormSourceBuffer", sourceBuffer);
                    shader.SetBuffer(kernel, "RayStormOutputBuffer", resultBuffer);
                    shader.SetTexture(kernel, "RSSkyboxTexture", environment);

                    materialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.one, Roughness = 1, IndexOfRefraction = 1.5f } });
                    shader.Dispatch(kernel, 1, 1, 1);
                    var results = new RSOutputData[3];
                    resultBuffer.GetData(results);
                    for (int rayIndex = 0; rayIndex < results.Length; ++rayIndex)
                        AssertNear(results[rayIndex].Data.x, 4, "HDR environment / front-back diffuse ray " + rayIndex);

                    materialBuffer.SetData(new[] { new RSMaterialData { Albedo = Vector3.zero, Emission = Vector3.one * 3, Roughness = 1, IndexOfRefraction = 1.5f } });
                    shader.Dispatch(kernel, 1, 1, 1);
                    resultBuffer.GetData(results);
                    AssertNear(results[1].Data.x, 3, "Emission uses incoming path throughput");
                    AssertNear(results[2].Data.x, 3, "Backface emission");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(environment);
                    UnityEngine.Object.DestroyImmediate(shader);
                }
            }
        }
    }
}
