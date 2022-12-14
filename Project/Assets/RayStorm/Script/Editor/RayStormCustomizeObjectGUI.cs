using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RayStormCustomizeObject))]
public class RayStormCustomizeObjectGUI : Editor
{
    public override void OnInspectorGUI()
    {
        RayStormCustomizeObject CustomizeObject = target as RayStormCustomizeObject;

        var ToolkitLibraryFolder = RSDynamicUtility.GetToolkitLibraryDirectory();
        var DynamicObjectPrograms = RSDynamicUtility.FindDynamicProgramsInFolder(ToolkitLibraryFolder + "Object/");
        var DynamicObjectProgramNames = RSDataUtility.ConvertArray(DynamicObjectPrograms, p => p.Name);

        int OldObjectProgramID = System.Array.FindIndex(DynamicObjectProgramNames, o => o.Equals(CustomizeObject.DynamicObjectProgramName));
        int NewObjectProgramID = EditorGUILayout.Popup("CustomizeObject", OldObjectProgramID, DynamicObjectProgramNames);
        if (NewObjectProgramID != OldObjectProgramID)
        {
            CustomizeObject.DynamicObjectProgramName = DynamicObjectProgramNames[NewObjectProgramID];
        }
    }
}
