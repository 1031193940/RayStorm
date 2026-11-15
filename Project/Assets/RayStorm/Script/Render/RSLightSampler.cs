using System;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public sealed class RSLightSampler : IDisposable
    {
        public const int EnvironmentWidth = 128;
        public const int EnvironmentHeight = 64;
        public int SceneLightCount { get; private set; }
        public int EnvironmentLightID { get; private set; } = -1;

        private ComputeBuffer SceneLightBuffer;
        private ComputeBuffer SceneLightAliasBuffer;
        private ComputeBuffer SceneGeometryLightIDBuffer;
        private ComputeBuffer EnvironmentAliasBuffer;
        private Texture SkyboxTexture;
        private bool EnvironmentIsGamma;

        public void Build(RSVertexData[] Vertices, RSGeometryData[] Geometries, RSMaterialData[] Materials, Light[] Lights, Texture Skybox, bool IsGamma, float SceneRadius, ComputeShader EnvironmentShader)
        {
            Dispose();
            SkyboxTexture = Skybox;
            EnvironmentIsGamma = IsGamma;
            var SceneLights = new List<RSLightData>();
            var Weights = new List<double>();
            float ProjectedArea = Mathf.PI * Mathf.Max(SceneRadius * SceneRadius, 0.0001f);
            foreach (Light Source in Lights)
            {
                if (Source == null || !Source.isActiveAndEnabled || (Source.type != LightType.Directional && Source.type != LightType.Point && Source.type != LightType.Spot))
                    continue;
                Vector3 Radiance = RSDataUtility.ColorToVector(Source.color.linear) * Source.intensity;
                float Power = Luminance(Radiance);
                if (Power <= 0.0f)
                    continue;
                var Light = new RSLightData
                {
                    Type = Source.type == LightType.Directional ? 0 : Source.type == LightType.Point ? 1 : 2,
                    GeometryID = -1,
                    Position = Source.transform.position,
                    Direction = Source.transform.forward,
                    Radiance = Radiance,
                    InnerCosine = Mathf.Cos(0.5f * Source.innerSpotAngle * Mathf.Deg2Rad),
                    OuterCosine = Mathf.Cos(0.5f * Source.spotAngle * Mathf.Deg2Rad)
                };
                SceneLights.Add(Light);
                Weights.Add(Power * (Light.Type == 0 ? ProjectedArea : Light.Type == 1 ? 4.0f * Mathf.PI : 2.0f * Mathf.PI * (1.0f - 0.5f * (Light.InnerCosine + Light.OuterCosine))));
            }

            int[] GeometryLightIDs = new int[Geometries.Length];
            for (int GeometryID = 0; GeometryID < Geometries.Length; ++GeometryID)
            {
                GeometryLightIDs[GeometryID] = -1;
                RSGeometryData Geometry = Geometries[GeometryID];
                Vector3 Emission = Materials[Geometry.MaterialID].Emission;
                float Power = Luminance(Emission);
                if (Power <= 0.0f)
                    continue;
                Vector3 Cross = Vector3.Cross(Vertices[Geometry.Indice1].Position - Vertices[Geometry.Indice0].Position, Vertices[Geometry.Indice2].Position - Vertices[Geometry.Indice0].Position);
                float Area = 0.5f * Cross.magnitude;
                if (Area <= 0.0f)
                    continue;
                GeometryLightIDs[GeometryID] = SceneLights.Count;
                SceneLights.Add(new RSLightData { Type = 3, GeometryID = GeometryID, Area = Area, Direction = Cross / (2.0f * Area), Radiance = Emission });
                Weights.Add(2.0f * Mathf.PI * Area * Power);
            }

            RSAliasData[] EnvironmentAliases = new RSAliasData[1];
            if (Skybox is Cubemap)
            {
                float[] EnvironmentWeights = ReadEnvironmentWeights(Skybox, IsGamma, EnvironmentShader);
                double Sum = 0.0;
                foreach (float Weight in EnvironmentWeights)
                    Sum += Weight;
                double Average = Sum / EnvironmentWeights.Length;
                double Floor = Math.Max(Average * 0.001, 1.0e-12);
                var Distribution = new double[EnvironmentWeights.Length];
                for (int Index = 0; Index < Distribution.Length; ++Index)
                    Distribution[Index] = EnvironmentWeights[Index] + Floor;
                EnvironmentAliases = CreateAliasTable(Distribution);
                EnvironmentLightID = SceneLights.Count;
                SceneLights.Add(new RSLightData { Type = 4, GeometryID = -1 });
                Weights.Add(ProjectedArea * 4.0 * Math.PI * Math.Max(Average, Floor));
            }

            SceneLightCount = SceneLights.Count;
            SceneLightBuffer = CreateBuffer(SceneLights.ToArray(), RSLightData.GetDataSize());
            SceneLightAliasBuffer = CreateBuffer(CreateAliasTable(Weights.ToArray()), RSAliasData.GetDataSize());
            SceneGeometryLightIDBuffer = CreateBuffer(GeometryLightIDs, sizeof(int));
            EnvironmentAliasBuffer = CreateBuffer(EnvironmentAliases, RSAliasData.GetDataSize());
        }

        public void Bind(ComputeShader Shader, int Kernel)
        {
            Shader.SetInt("SceneLightCount", SceneLightCount);
            Shader.SetInt("EnvironmentLightID", EnvironmentLightID);
            Shader.SetInt("EnvironmentWidth", EnvironmentWidth);
            Shader.SetInt("EnvironmentHeight", EnvironmentHeight);
            Shader.SetInt("RSEnvironmentIsGamma", EnvironmentIsGamma ? 1 : 0);
            Shader.SetBuffer(Kernel, "RSSceneLightBuffer", SceneLightBuffer);
            Shader.SetBuffer(Kernel, "RSSceneLightAliasBuffer", SceneLightAliasBuffer);
            Shader.SetBuffer(Kernel, "RSSceneGeometryLightIDBuffer", SceneGeometryLightIDBuffer);
            Shader.SetBuffer(Kernel, "RSEnvironmentAliasBuffer", EnvironmentAliasBuffer);
            if (SkyboxTexture != null)
                Shader.SetTexture(Kernel, "RSSkyboxTexture", SkyboxTexture);
        }

        public static RSAliasData[] CreateAliasTable(double[] Weights)
        {
            int Count = Weights.Length;
            var Table = new RSAliasData[Count];
            if (Count == 0)
                return Table;
            double Total = 0.0;
            foreach (double Weight in Weights)
                Total += Weight;
            var Scaled = new double[Count];
            var Small = new Stack<int>();
            var Large = new Stack<int>();
            for (int Index = 0; Index < Count; ++Index)
            {
                double Probability = Total > 0.0 ? Weights[Index] / Total : 1.0 / Count;
                Table[Index].Probability = (float)Probability;
                Table[Index].Alias = Index;
                Scaled[Index] = Probability * Count;
                if (Scaled[Index] < 1.0)
                    Small.Push(Index);
                else
                    Large.Push(Index);
            }
            while (Small.Count > 0 && Large.Count > 0)
            {
                int Low = Small.Pop();
                int High = Large.Pop();
                Table[Low].Threshold = (float)Scaled[Low];
                Table[Low].Alias = High;
                Scaled[High] -= 1.0 - Scaled[Low];
                if (Scaled[High] < 1.0)
                    Small.Push(High);
                else
                    Large.Push(High);
            }
            foreach (int Index in Small)
                Table[Index].Threshold = 1.0f;
            foreach (int Index in Large)
                Table[Index].Threshold = 1.0f;
            return Table;
        }

        private static float[] ReadEnvironmentWeights(Texture Skybox, bool IsGamma, ComputeShader Shader)
        {
            int Kernel = Shader.FindKernel("CSKernelMain");
            var Weights = new float[EnvironmentWidth * EnvironmentHeight];
            using (var Buffer = new ComputeBuffer(Weights.Length, sizeof(float)))
            {
                Shader.SetInt("EnvironmentWidth", EnvironmentWidth);
                Shader.SetInt("EnvironmentHeight", EnvironmentHeight);
                Shader.SetInt("RSEnvironmentIsGamma", IsGamma ? 1 : 0);
                Shader.SetTexture(Kernel, "RSSkyboxTexture", Skybox);
                Shader.SetBuffer(Kernel, "RSEnvironmentWeightBuffer", Buffer);
                Shader.Dispatch(Kernel, EnvironmentWidth / 8, EnvironmentHeight / 8, 1);
                Buffer.GetData(Weights);
            }
            return Weights;
        }

        private static ComputeBuffer CreateBuffer<T>(T[] Data, int Stride) where T : struct
        {
            var Buffer = new ComputeBuffer(Mathf.Max(1, Data.Length), Stride);
            Buffer.SetData(Data.Length > 0 ? Data : new T[1]);
            return Buffer;
        }

        private static float Luminance(Vector3 Color)
        {
            return Mathf.Max(0.0f, Vector3.Dot(Color, new Vector3(0.2126f, 0.7152f, 0.0722f)));
        }

        public void Dispose()
        {
            RSDataUtility.ReleaseBuffer(ref SceneLightBuffer);
            RSDataUtility.ReleaseBuffer(ref SceneLightAliasBuffer);
            RSDataUtility.ReleaseBuffer(ref SceneGeometryLightIDBuffer);
            RSDataUtility.ReleaseBuffer(ref EnvironmentAliasBuffer);
            SceneLightCount = 0;
            EnvironmentLightID = -1;
        }
    }
}
