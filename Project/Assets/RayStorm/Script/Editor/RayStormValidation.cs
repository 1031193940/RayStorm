using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
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
            public bool passed;
            public string error;
        }

        [MenuItem("RayStorm/Validate All")]
        public static void RunAll()
        {
            // Tests create temporary native objects; never discard an unsaved user scene.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var result = new ValidationResult
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsAPI = SystemInfo.graphicsDeviceType.ToString()
            };
            try
            {
                SceneValidation.Run();
                RayStormRuntimeValidation.Run();
                RayStorm.Editor.RayStormGpuValidation.Run();
                RayStormRendererValidation.Run();
                result.passed = true;
                Debug.Log("RAYSTORM_VALIDATION_PASSED");
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
                Debug.LogException(exception);
            }
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationResults"));
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "validation.json"), JsonUtility.ToJson(result, true));
            if (Application.isBatchMode)
                EditorApplication.Exit(result.passed ? 0 : 1);
            else if (!result.passed)
                throw new InvalidOperationException("RayStorm regression validation failed; see the Console and ValidationResults/validation.json.");
        }
    }
}
