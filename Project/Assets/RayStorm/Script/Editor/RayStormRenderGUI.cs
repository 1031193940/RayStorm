using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RayStormRenderer))]
public class RayStormRenderGUI : Editor
{
    public override void OnInspectorGUI()
    {
        RayStormRenderer RendererComponent = target as RayStormRenderer;

        if (!EditorApplication.isPlaying)
            OnInspectorGUIInEditor(RendererComponent);
        else
            OnInspectorGUIInPlaying(RendererComponent);

        if (RendererComponent.RayStormBehaviour != null)
            RendererComponent.RayStormBehaviour.OnInspectorGUI();
    }

    private void OnInspectorGUIInEditor(RayStormRenderer RendererComponent)
    {
        var SubclassTypes = RSAssetUtility.FindSubclassOf<RayStormBehaviour>();
        var SubclassNames = RSDataUtility.ConvertArray(SubclassTypes, t => t.Name);

        int OldBehaviourID = -1;
        if (RendererComponent.RayStormBehaviour != null)
        {
            Type CurrentBehaviourType = RendererComponent.RayStormBehaviour.GetType();
            OldBehaviourID = Array.FindIndex(SubclassTypes, t => t.Equals(CurrentBehaviourType));
        }

        int NewBehaviourID = Mathf.Max(EditorGUILayout.Popup("BehaviourType", OldBehaviourID, SubclassNames), 0);
        if (NewBehaviourID != OldBehaviourID)
        {
            RendererComponent.RayStormBehaviour = CreateInstance(SubclassTypes[NewBehaviourID]) as RayStormBehaviour;
        }

        RendererComponent.SkyboxTexture = (Texture)EditorGUILayout.ObjectField("Skybox", RendererComponent.SkyboxTexture, typeof(Cubemap), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
    }

    private void OnInspectorGUIInPlaying(RayStormRenderer RendererComponent)
    {
        string OctreeDisplayState = (RendererComponent.IsDisplayOctree ? "ON" : "OFF");
        if (GUILayout.Button("Display Octree : " + OctreeDisplayState, GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f)))
            RendererComponent.IsDisplayOctree = !RendererComponent.IsDisplayOctree;

        if (GUILayout.Button("Save Texture", GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f)))
            RendererComponent.SaveRenderTexture();
    }
}
