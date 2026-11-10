using System.Reflection;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RayStorm
{
    public class RSAssetUtility
    {
        public static string GetToolkitRootDirectory()
        {
            string AssetFilePath = null;

            #if UNITY_EDITOR
            {
                var AssetGUIDList = AssetDatabase.FindAssets(typeof(RSAssetUtility).Name);
                if (AssetGUIDList != null && AssetGUIDList.Length > 0)
                    AssetFilePath = AssetDatabase.GUIDToAssetPath(AssetGUIDList[0]);
            }
            #endif // UNITY_EDITOR

            if (AssetFilePath != null)
            {
                var ToolkitRootFolderPath = "";
                var AssetFilePathSplitted = AssetFilePath.Split('/');
                for (int i = 0; i < AssetFilePathSplitted.Length - 3; ++i)
                    ToolkitRootFolderPath += AssetFilePathSplitted[i] + "/";

                return ToolkitRootFolderPath;
            }

            return null;
        }

        public static T LoadAsset<T>(string AssetName) where T : Object
        {
            GetAssetPath<T>(AssetName, out T AssetInstance);
            return AssetInstance;
        }

        public static string GetAssetPath<T>(string AssetName) where T : Object
        {
            return GetAssetPath<T>(AssetName, out _);
        }

        public static string GetAssetPath<T>(string AssetName, out T LoadedAssetInstance) where T : Object
        {
            LoadedAssetInstance = null;

            #if UNITY_EDITOR
            foreach (string GUID in AssetDatabase.FindAssets(AssetName))
            {
                string AssetFilePath = AssetDatabase.GUIDToAssetPath(GUID);
                LoadedAssetInstance = AssetDatabase.LoadAssetAtPath<T>(AssetFilePath);
                if (LoadedAssetInstance != null)
                    return AssetFilePath;
            }
            #endif // UNITY_EDITOR

            return null;
        }

        public static string GetOutputPath(string FileName)
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, FileName);
#else
            return Path.Combine(Application.persistentDataPath, FileName);
#endif
        }

        public static void SaveTexture2D(Texture2D SourceTexture2D, string FilePath)
        {
            byte[] EncodedBytes;
            var Extension = Path.GetExtension(FilePath).ToLowerInvariant();
            if (Extension == ".png")
                EncodedBytes = SourceTexture2D.EncodeToPNG();
            else if (Extension == ".tga")
                EncodedBytes = SourceTexture2D.EncodeToTGA();
            else if (Extension == ".exr")
                EncodedBytes = SourceTexture2D.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            else
                EncodedBytes = SourceTexture2D.EncodeToJPG();

            string AbsoluteFilePath = Path.GetFullPath(FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(AbsoluteFilePath));
            File.WriteAllBytes(AbsoluteFilePath, EncodedBytes);

#if UNITY_EDITOR
            string AssetDirectory = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (AbsoluteFilePath.StartsWith(AssetDirectory, System.StringComparison.OrdinalIgnoreCase))
            {
                string RelativeAssetPath = "Assets/" + AbsoluteFilePath.Substring(AssetDirectory.Length).Replace('\\', '/');
                AssetDatabase.ImportAsset(RelativeAssetPath);
            }
#endif
        }

        public static System.Type[] FindSubclassOf<T>()
        {
            var SubclassTypes = new List<System.Type>();

            var AssemblyTypes = Assembly.GetAssembly(typeof(T)).GetTypes();
            foreach (var Type in AssemblyTypes)
            {
                if (Type.IsClass && !Type.IsAbstract && Type.IsSubclassOf(typeof(T)))
                    SubclassTypes.Add(Type);
            }

            return SubclassTypes.ToArray();
        }
    }
}
