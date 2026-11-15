using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RayStorm
{
    public static class RayStormValidation
    {
        [Serializable]
        private class ValidationResult
        {
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsAPI;
            public string colorSpace;
            public bool passed;
            public string error;
        }

        [MenuItem("RayStorm/Validate All")]
        public static void RunAll()
        {
            ValidationResult Result = new ValidationResult
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            try
            {
                SceneValidation.Run();
                RayStormRuntimeValidation.Run();
                RayStorm.Editor.RayStormGpuValidation.Run();
                RayStorm.Editor.RayStormLightValidation.Run();
                RayStormRendererValidation.Run();
                Result.passed = true;
                Debug.Log("RAYSTORM_VALIDATION_PASSED");
            }
            catch (Exception Error)
            {
                Result.error = Error.ToString();
                Debug.LogException(Error);
            }
            string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationResults"));
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "validation.json"), JsonUtility.ToJson(Result, true));
            if (Application.isBatchMode)
                EditorApplication.Exit(Result.passed ? 0 : 1);
            else if (!Result.passed)
                throw new InvalidOperationException("RayStorm regression validation failed; see the Console and ValidationResults/validation.json.");
        }
    }
}
