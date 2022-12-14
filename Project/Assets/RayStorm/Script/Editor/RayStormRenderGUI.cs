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

        RendererComponent.ContainsTriangleMesh = EditorGUILayout.Toggle("ContainsTriangleMesh", RendererComponent.ContainsTriangleMesh);
        RendererComponent.ContainsCustomizeObject = EditorGUILayout.Toggle("ContainsCustomizeObject", RendererComponent.ContainsCustomizeObject);
        RendererComponent.ContainsLightSource = EditorGUILayout.Toggle("ContainsLightSource", RendererComponent.ContainsLightSource);
        RendererComponent.ContainsSkybox = EditorGUILayout.Toggle("ContainsSkybox", RendererComponent.ContainsSkybox);
    }

    private void OnInspectorGUIInPlaying(RayStormRenderer RendererComponent)
    {
        if (GUILayout.Button("Save Texture", GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f)))
            RendererComponent.SaveRenderTexture();
    }
}
