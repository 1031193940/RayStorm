using UnityEngine;

namespace RayStorm
{
    public struct RSMaterialData
    {
        public int ProgramID;
        public Vector3 Albedo;
        public Vector3 Emission;
        public float Roughness;
        public Vector4 Extra;

        public static RSMaterialData Default
        {
            get
            {
                RSMaterialData DefaultMaterialData = default(RSMaterialData);
                DefaultMaterialData.Albedo = Vector3.one * 0.5f;
                return DefaultMaterialData;
            }
        }

        public static int GetDataSize()
        {
            return sizeof(int) * 1 + sizeof(float) * (3 + 3 + 1 + 4);
        }

        public static RSMaterialData CreateFromUnityMaterial(Material SourceMaterial)
        {
            RSMaterialData NewMaterialData = RSMaterialData.Default;
            {
                Vector3 Albedo = Vector3.one * 0.9f;
                if (SourceMaterial.HasProperty("_Color"))
                    Albedo = RSDataUtility.ColorToVector(SourceMaterial.GetColor("_Color"));

                Vector3 Emission = Vector3.zero;
                if (SourceMaterial.HasProperty("_EmissionColor"))
                    Emission = RSDataUtility.ColorToVector(SourceMaterial.GetColor("_EmissionColor"));

                float Roughness = 0.0f;
                if (SourceMaterial.HasProperty("_Roughness"))
                    Roughness = SourceMaterial.GetFloat("_Roughness");
                else if (SourceMaterial.HasProperty("_Smoothness"))
                    Roughness = 1.0f - SourceMaterial.GetFloat("_Smoothness");
                else if (SourceMaterial.HasProperty("_Glossiness"))
                    Roughness = 1.0f - SourceMaterial.GetFloat("_Glossiness");

                NewMaterialData.Albedo = Albedo;
                NewMaterialData.Emission = Emission;
                NewMaterialData.Roughness = Roughness;
            }

            return NewMaterialData;
        }
    }
}