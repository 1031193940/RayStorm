using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

namespace RayStorm
{
    public struct RSProgramData
    {
        public string Name;
        public string Path;
    }

    public static class RSDynamicUtility
    {
        private class DynamicCompileContext
        {
            public int ProgramID;
            public RSProgramData[] ProgramList;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Helper Functions | 
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static string GetToolkitLibraryDirectory()
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
                var DynamicLibraryFolder = "";
                var AssetFilePathSplitted = AssetFilePath.Split('/');
                for (int i = 0; i < AssetFilePathSplitted.Length - 3; ++i)
                    DynamicLibraryFolder += AssetFilePathSplitted[i] + "/";

                return DynamicLibraryFolder + "Shader/Library/";
            }

            return null;
        }

        private static string[] FindProgramFilesInFolder(string Folder)
        {
            var FolderInfo = new DirectoryInfo(Folder);
            var FolderFiles = FolderInfo.GetFiles();

            var FileNamesList = new List<string>();
            foreach (var FileInfo in FolderFiles)
            {
                var LowerFileName = FileInfo.Name.ToLower();
                if (LowerFileName.Equals("template") || LowerFileName.EndsWith(".meta"))
                    continue;

                FileNamesList.Add(FileInfo.Name);
            }

            return FileNamesList.ToArray();
        }

        public static RSProgramData[] FindDynamicProgramsInFolder(string Folder)
        {
            Folder = Folder.Replace('\\', '/');
            if (!Folder.EndsWith("/"))
                Folder += '/';

            var ProgramFileNames = FindProgramFilesInFolder(Folder);
            var ProgramDataList = new List<RSProgramData>();
            foreach (var FileName in ProgramFileNames)
            {
                RSProgramData ProgramData;
                ProgramData.Name = FileName;
                ProgramData.Path = Folder + FileName;
                ProgramDataList.Add(ProgramData);
            }

            return ProgramDataList.ToArray();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Dynamic Compile Functions | 
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        private static string CompileDynamicProgramCode(DynamicCompileContext Context, string SourceCode)
        {
            string SourceCodeTrimed = SourceCode.Trim();

            string[] IgnoreList = { "__LOOP_BEGIN__", "__LOOP_END__" };
            if (System.Array.FindIndex(IgnoreList, a => a.Equals(SourceCodeTrimed)) >= 0)
                return null;

            string[] EmptyLineList = { "__SPACE_LINE__" };
            if (System.Array.FindIndex(EmptyLineList, a => a.Equals(SourceCodeTrimed)) >= 0)
                return "";

            string CompiledCode = SourceCode;
            if (Context.ProgramID != -1)
            {
                Dictionary<string, string> DictKeywordToReplaceValue = new Dictionary<string, string>
                {
                    { "__PROGRAM_ID__", Context.ProgramID.ToString() },
                    { "__PROGRAM_COUNT__", Context.ProgramList.Length.ToString() },
                    { "__PROGRAM_NAME__", Context.ProgramList[Context.ProgramID].Name },
                };

                foreach (var KeywordToReplaceValue in DictKeywordToReplaceValue)
                    CompiledCode = CompiledCode.Replace(KeywordToReplaceValue.Key, KeywordToReplaceValue.Value);
            }

            return CompiledCode;
        }

        private static void CompileEventOnLoopBegin(DynamicCompileContext Context, StreamWriter Writer, Queue<string> ProgramQueue)
        {
            var LoopProgramList = new List<string>();
            while (ProgramQueue.Count > 0)
            {
                string ProgramLine = ProgramQueue.Dequeue();
                string ProgramLineTrim = ProgramLine.Trim();
                if (ProgramLineTrim.Equals("__LOOP_BEGIN__"))
                    continue;
                else if (ProgramLineTrim.Equals("__LOOP_END__"))
                    break;

                LoopProgramList.Add(ProgramLine);
            }

            for (int ProgramID = 0; ProgramID < Context.ProgramList.Length; ++ProgramID)
            {
                Context.ProgramID = ProgramID;

                for (int LineID = 0; LineID < LoopProgramList.Count; ++LineID)
                {
                    string CompiledCode = CompileDynamicProgramCode(Context, LoopProgramList[LineID]);
                    Writer.WriteLine(CompiledCode);
                }
            }

            Context.ProgramID = -1;
        }

        private static void CompileEventOnDefault(DynamicCompileContext Context, StreamWriter Writer, Queue<string> ProgramQueue)
        {
            string SourceCode = ProgramQueue.Dequeue();
            string CompiledCode = CompileDynamicProgramCode(Context, SourceCode);

            if (CompiledCode != null)
                Writer.WriteLine(CompiledCode);
        }

        public static void CompileProgramFileWithTemplate(string TemplateFilePath, string OutputFilePath, RSProgramData[] ProgramList)
        {
            Queue<string> ProgramQueue = new Queue<string>();
            using (var Reader = new StreamReader(TemplateFilePath))
            {
                while (!Reader.EndOfStream)
                    ProgramQueue.Enqueue(Reader.ReadLine());
            }

            using (var Writer = new StreamWriter(OutputFilePath))
            {
                DynamicCompileContext Context = new DynamicCompileContext();
                Context.ProgramID = -1;
                Context.ProgramList = ProgramList;

                while (ProgramQueue.Count > 0)
                {
                    var ProgramLine = ProgramQueue.Peek();
                    var ProgramLineTrim = ProgramLine.Trim();
                    if (ProgramLineTrim.Equals("__LOOP_BEGIN__"))
                        CompileEventOnLoopBegin(Context, Writer, ProgramQueue);
                    else
                        CompileEventOnDefault(Context, Writer, ProgramQueue);
                }
            }

#if UNITY_EDITOR
            {
                AssetDatabase.ImportAsset(OutputFilePath);
            }
#endif // UNITY_EDITOR
        }

    }
}