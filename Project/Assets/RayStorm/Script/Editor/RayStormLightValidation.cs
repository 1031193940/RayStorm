using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RayStorm.Editor
{
    public static class RayStormLightValidation
    {
        private const int SampleCount = 65536;

        public static void Run()
        {
            foreach (LightType Type in new[] { LightType.Directional, LightType.Point, LightType.Spot })
            {
                using (var Fixture = new LightFixture())
                {
                    Light Source = Fixture.Source;
                    Source.type = Type;
                    Source.enabled = true;
                    Source.intensity = Type == LightType.Directional ? 3 : 12;
                    Fixture.Check(Type + " direct illumination", Vector3.one * (2.4f / Mathf.PI));
                    Source.transform.position = Vector3.forward * 4;
                    Fixture.Check(Type + " distance", Vector3.one * (Type == LightType.Directional ? 2.4f : 0.6f) / Mathf.PI);
                    if (Type == LightType.Spot)
                    {
                        Source.transform.position = Vector3.forward * 2;
                        float Cosine = (Mathf.Cos(15 * Mathf.Deg2Rad) + Mathf.Cos(30 * Mathf.Deg2Rad)) * 0.5f;
                        Source.transform.forward = new Vector3(Mathf.Sqrt(1 - Cosine * Cosine), 0, -Cosine);
                        Fixture.Check("Spot transition", Vector3.one * (1.2f / Mathf.PI));
                        Source.transform.forward = Vector3.right;
                        Fixture.Check("Outside spot cone", Vector3.zero);
                    }
                }
            }
            foreach (float Distance in new[] { 1.0f, 3.0f })
            {
                using (var Fixture = new LightFixture())
                {
                    Fixture.Source.type = LightType.Point;
                    Fixture.Source.enabled = true;
                    Fixture.Source.intensity = 12;
                    Fixture.AddQuad(Vector3.forward * Distance, Vector3.right * 4, Vector3.up * 4, 1);
                    Fixture.Check("Shadow endpoint " + Distance, Vector3.one * (Distance < 2 ? 0 : 2.4f / Mathf.PI));
                }
            }
            foreach (int Divisions in new[] { 1, 8 })
            {
                using (var Fixture = new LightFixture())
                {
                    RSMaterialData Emitter = Fixture.Materials[1];
                    Emitter.Emission = Vector3.one * 8;
                    Fixture.Materials[1] = Emitter;
                    Fixture.AddQuad(Vector3.forward, Vector3.right, Vector3.up, 1, Divisions);
                    float Expected = (float)(6.4 * 4 / Math.Sqrt(2) * Math.Atan(1 / Math.Sqrt(2)) / Math.PI);
                    Fixture.Check("Emissive subdivision " + Divisions, Vector3.one * Expected);
                }
            }
            using (var Fixture = new LightFixture())
            {
                Fixture.Environment = new Color(0.25f, 2, 8, 1);
                Fixture.Check("Linear HDR environment", new Vector3(0.2f, 1.6f, 6.4f));
                Fixture.Environment = new Color(0.5f, 1, 4, 1);
                Fixture.EnvironmentIsGamma = true;
                Fixture.Check("Gamma HDR environment", new Vector3(0.21404114f, 1, Mathf.Pow(4, 2.2f)) * 0.8f);
            }
            using (var Fixture = new LightFixture())
            {
                Fixture.Environment = new Color(4, 4, 4, 1);
                RSMaterialData Material = Fixture.Materials[0];
                Material.Roughness = 0;
                Fixture.Materials[0] = Material;
                Fixture.Check("Mixed diffuse and delta", Vector3.one * 3.232f);
                Material.Metallic = 1;
                Material.Albedo = Vector3.one * 0.5f;
                Fixture.Materials[0] = Material;
                Fixture.Check("Mirror environment", Vector3.one * 2);
                Material.Metallic = 0;
                Material.Transparency = 1;
                Material.Albedo = Vector3.one;
                Fixture.Materials[0] = Material;
                Fixture.Check("Glass radiance eta", Vector3.one * (4 * (0.04f + 0.96f / 2.25f)));
                Material.IndexOfRefraction = 1;
                Material.Roughness = 0.3f;
                Fixture.Materials[0] = Material;
                Fixture.Check("Index matched glass", Vector3.one * 4);
            }
            Debug.Log("RAYSTORM_LIGHT_VALIDATION_PASSED: analytic lights, shadow endpoints, emissive area, HDR, MIS and delta transport.");
        }

        private sealed class LightFixture : IDisposable
        {
            public readonly List<RSVertexData> Vertices = new List<RSVertexData>();
            public readonly List<RSGeometryData> Geometries = new List<RSGeometryData>();
            public readonly List<RSMaterialData> Materials = new List<RSMaterialData>();
            public readonly Light Source;
            public Color Environment = Color.black;
            public bool EnvironmentIsGamma;

            public LightFixture()
            {
                Source = new GameObject("RayStorm light validation").AddComponent<Light>();
                Source.enabled = false;
                Source.color = Color.white;
                Source.transform.position = Vector3.forward * 2;
                Source.transform.forward = Vector3.back;
                Source.spotAngle = 60;
                Source.innerSpotAngle = 30;
                Materials.Add(new RSMaterialData { Albedo = Vector3.one * 0.8f, Roughness = 1, IndexOfRefraction = 1.5f });
                Materials.Add(new RSMaterialData { Albedo = Vector3.zero, Roughness = 1, IndexOfRefraction = 1.5f });
                AddQuad(Vector3.zero, Vector3.right * 100, Vector3.up * 100, 0);
            }

            public void AddQuad(Vector3 Center, Vector3 U, Vector3 V, int MaterialID, int Divisions = 1)
            {
                for (int Y = 0; Y < Divisions; ++Y)
                    for (int X = 0; X < Divisions; ++X)
                    {
                        Vector3 Corner = Center + U * (2.0f * X / Divisions - 1) + V * (2.0f * Y / Divisions - 1);
                        Vector3 Normal = Vector3.Cross(U, V).normalized;
                        int First = Vertices.Count;
                        Vertices.Add(new RSVertexData { Position = Corner, Normal = Normal });
                        Vertices.Add(new RSVertexData { Position = Corner + 2 * U / Divisions, Normal = Normal });
                        Vertices.Add(new RSVertexData { Position = Corner + 2 * U / Divisions + 2 * V / Divisions, Normal = Normal });
                        Vertices.Add(new RSVertexData { Position = Corner + 2 * V / Divisions, Normal = Normal });
                        Geometries.Add(new RSGeometryData { MaterialID = MaterialID, Indice0 = First, Indice1 = First + 1, Indice2 = First + 2 });
                        Geometries.Add(new RSGeometryData { MaterialID = MaterialID, Indice0 = First, Indice1 = First + 2, Indice2 = First + 3 });
                    }
            }

            public void Check(string Label, Vector3 Expected)
            {
                ComputeShader Shader = Object.Instantiate(RSAssetUtility.LoadAsset<ComputeShader>("RayStormValidation"));
                Cubemap Skybox = RSDataUtility.CreateSolidCubemap(1, TextureFormat.RGBAFloat, false, Environment);
                var Buffers = new List<ComputeBuffer>();
                using (var Sampler = new RSLightSampler())
                {
                    try
                    {
                        int Kernel = Shader.FindKernel("CSKernelTraceSources");
                        RSVertexData[] VertexData = Vertices.ToArray();
                        RSGeometryData[] GeometryData = Geometries.ToArray();
                        RSBVHUtility.BuildSceneBVH(VertexData, GeometryData, out RSBVHData[] BVHs, out int[] GeometryIDs);
                        Sampler.Build(VertexData, GeometryData, Materials.ToArray(), new[] { Source }, Skybox, EnvironmentIsGamma, 1, RSAssetUtility.LoadAsset<ComputeShader>("RayStormEnvironment"));
                        Sampler.Bind(Shader, Kernel);
                        Bind(Shader, Kernel, Buffers, "RSSceneVertexBuffer", VertexData, RSVertexData.GetDataSize());
                        Bind(Shader, Kernel, Buffers, "RSSceneGeometryBuffer", GeometryData, RSGeometryData.GetDataSize());
                        Bind(Shader, Kernel, Buffers, "RSSceneMaterialBuffer", Materials.ToArray(), RSMaterialData.GetDataSize());
                        Bind(Shader, Kernel, Buffers, "RSSceneBVHBuffer", BVHs, RSBVHData.GetDataSize());
                        Bind(Shader, Kernel, Buffers, "RSSceneBVHGeometryIDBuffer", GeometryIDs, sizeof(int));
                        var Rays = new RSSourceData[SampleCount];
                        for (int Index = 0; Index < Rays.Length; ++Index)
                            Rays[Index] = new RSSourceData { Position = Vector3.forward * 0.5f, Direction = Vector3.back, Energy = Vector3.one };
                        Bind(Shader, Kernel, Buffers, "RayStormSourceBuffer", Rays, RSSourceData.GetDataSize());
                        var Results = new RSOutputData[SampleCount];
                        ComputeBuffer Output = Bind(Shader, Kernel, Buffers, "RayStormOutputBuffer", Results, RSOutputData.GetDataSize());
                        Shader.SetInt("SceneBVHCount", BVHs.Length);
                        Shader.SetInt("RayCount", SampleCount);
                        Shader.SetInt("RSRandomSeed", 173127);
                        Shader.Dispatch(Kernel, SampleCount / 128, 1, 1);
                        Output.GetData(Results);
                        for (int Channel = 0; Channel < 3; ++Channel)
                        {
                            double Sum = 0, Squares = 0;
                            foreach (RSOutputData Result in Results)
                            {
                                float Value = Result.Data[Channel];
                                if (float.IsNaN(Value) || float.IsInfinity(Value) || Value < 0)
                                    throw new InvalidOperationException(Label + " produced invalid radiance.");
                                Sum += Value;
                                Squares += (double)Value * Value;
                            }
                            double Mean = Sum / SampleCount;
                            double StandardError = Math.Sqrt(Math.Max(0, Squares / SampleCount - Mean * Mean) / (SampleCount - 1));
                            if (Math.Abs(Mean - Expected[Channel]) > 0.002 + 5 * StandardError)
                                throw new InvalidOperationException(Label + ": expected " + Expected[Channel] + ", got " + Mean);
                        }
                    }
                    finally
                    {
                        foreach (ComputeBuffer Buffer in Buffers)
                            Buffer.Dispose();
                        Object.DestroyImmediate(Skybox);
                        Object.DestroyImmediate(Shader);
                    }
                }
            }

            private static ComputeBuffer Bind<T>(ComputeShader Shader, int Kernel, List<ComputeBuffer> Buffers, string Name, T[] Values, int Stride) where T : struct
            {
                var Buffer = new ComputeBuffer(Values.Length, Stride);
                Buffer.SetData(Values);
                Shader.SetBuffer(Kernel, Name, Buffer);
                Buffers.Add(Buffer);
                return Buffer;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Source.gameObject);
            }
        }
    }
}
