using UnityEngine;
using UnityEngine.Rendering;

namespace RayStorm
{
    public struct RSMaterialData
    {
        public Vector3 Albedo;
        public Vector3 Emission;
        public float Metallic;
        public float Roughness;
        public float Transparency;
        public float IndexOfRefraction;

        public static RSMaterialData Default
        {
            get
            {
                RSMaterialData DefaultMaterialData = default(RSMaterialData);
                DefaultMaterialData.Albedo = Vector3.one * 0.5f;
                DefaultMaterialData.Roughness = 1.0f;
                DefaultMaterialData.IndexOfRefraction = 1.4f;
                return DefaultMaterialData;
            }
        }

        public static int GetDataSize()
        {
            return sizeof(float) * (3 + 3 + 1 + 1 + 1 + 1);
        }

        public static RSMaterialData CreateFromUnityMaterial(Material SourceMaterial)
        {
            if (SourceMaterial == null)
                return RSMaterialData.Default;

            Vector3 Albedo = Vector3.one * 0.9f;
            if (SourceMaterial.HasProperty("_Color"))
                Albedo = RSDataUtility.ColorToVector(SourceMaterial.GetColor("_Color").linear);

            Vector3 Emission = Vector3.zero;
            if (SourceMaterial.HasProperty("_EmissionColor"))
                Emission = RSDataUtility.ColorToVector(SourceMaterial.GetColor("_EmissionColor"));

            float Metallic = 1.0f;
            if (SourceMaterial.HasProperty("_Metallic"))
                Metallic = SourceMaterial.GetFloat("_Metallic");

            float Roughness = 0.0f;
            if (SourceMaterial.HasProperty("_Roughness"))
                Roughness = SourceMaterial.GetFloat("_Roughness");
            else if (SourceMaterial.HasProperty("_Smoothness"))
                Roughness = 1.0f - SourceMaterial.GetFloat("_Smoothness");
            else if (SourceMaterial.HasProperty("_Glossiness"))
                Roughness = 1.0f - SourceMaterial.GetFloat("_Glossiness");

            float Transparency = 0.0f;
            if (SourceMaterial.renderQueue == (int)RenderQueue.Transparent)
                Transparency = 1.0f;

            RSMaterialData NewMaterialData = RSMaterialData.Default;
            NewMaterialData.Albedo = Albedo;
            NewMaterialData.Emission = Emission;
            NewMaterialData.Metallic = Metallic;
            NewMaterialData.Roughness = Roughness;
            NewMaterialData.Transparency = Transparency;

            return NewMaterialData;
        }
    }
}
