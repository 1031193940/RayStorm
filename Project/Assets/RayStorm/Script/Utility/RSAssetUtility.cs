using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
    using UnityEditor;
#endif // UNITY_EDIOTR

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
            T AssetInstance = null;
            GetAssetPath<T>(AssetName, out AssetInstance);
            return AssetInstance;
        }

        public static string GetAssetPath<T>(string AssetName) where T : Object
        {
            T AssetInstance = null;
            return GetAssetPath<T>(AssetName, out AssetInstance);
        }

        public static string GetAssetPath<T>(string AssetName, out T LoadedAssetInstance) where T : Object
        {
            LoadedAssetInstance = null;

            string FoundAssetFilePath = null;

            #if UNITY_EDITOR
            {
                var AssetGUIDList = AssetDatabase.FindAssets(AssetName);
                if (AssetGUIDList != null && AssetGUIDList.Length > 0)
                {
                    foreach (var GUID in AssetGUIDList)
                    {
                        var AssetFilePath = AssetDatabase.GUIDToAssetPath(GUID);
                        var AssetInstance = AssetDatabase.LoadAssetAtPath<T>(AssetFilePath);
                        if (AssetInstance != null)
                        {
                            FoundAssetFilePath = AssetFilePath;
                            LoadedAssetInstance = AssetInstance;
                            break;
                        }
                    }
                }
            }
            #endif // UNITY_EDITOR

            return FoundAssetFilePath;
        }

        public static string GetOutputPath(string FileName)
        {
#if UNITY_EDITOR
            return System.IO.Path.Combine(Application.dataPath, FileName);
#else
            return System.IO.Path.Combine(Application.persistentDataPath, FileName);
#endif
        }

        public static void SaveTexture2D(Texture2D SourceTexture2D, string FilePath)
        {
            if (SourceTexture2D == null)
                throw new System.ArgumentNullException(nameof(SourceTexture2D));

            byte[] EncodedBytes;
            var Extension = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
            if (Extension == ".png")
                EncodedBytes = SourceTexture2D.EncodeToPNG();
            else if (Extension == ".tga")
                EncodedBytes = SourceTexture2D.EncodeToTGA();
            else if (Extension == ".exr")
                EncodedBytes = SourceTexture2D.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            else
                EncodedBytes = SourceTexture2D.EncodeToJPG();

            string AbsoluteFilePath = System.IO.Path.GetFullPath(FilePath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AbsoluteFilePath));
            System.IO.File.WriteAllBytes(AbsoluteFilePath, EncodedBytes);

#if UNITY_EDITOR
            string AssetDirectory = System.IO.Path.GetFullPath(Application.dataPath) + System.IO.Path.DirectorySeparatorChar;
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
