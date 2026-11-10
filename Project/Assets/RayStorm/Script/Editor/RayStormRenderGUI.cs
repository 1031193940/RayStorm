using System;
using UnityEngine;
using UnityEditor;

namespace RayStorm
{
    [CustomEditor(typeof(RayStormRenderer))]
    public class RayStormRenderGUI : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            RayStormRenderer RendererComponent = target as RayStormRenderer;

            serializedObject.Update();
            if (!EditorApplication.isPlaying)
                OnInspectorGUIInEditor();
            else
                OnInspectorGUIInPlaying(RendererComponent);
            serializedObject.ApplyModifiedProperties();

            if (RendererComponent.RayStormBehaviour != null)
                RendererComponent.RayStormBehaviour.OnInspectorGUI();
        }

        private void OnInspectorGUIInEditor()
        {
            DrawPropertiesExcluding(serializedObject, "m_Script", "RayStormBehaviour", "SkyboxTexture");
            SerializedProperty SkyboxProperty = serializedObject.FindProperty("SkyboxTexture");
            SkyboxProperty.objectReferenceValue = EditorGUILayout.ObjectField("Skybox", SkyboxProperty.objectReferenceValue, typeof(Cubemap), false);

            SerializedProperty BehaviourProperty = serializedObject.FindProperty("RayStormBehaviour");
            EditorGUILayout.PropertyField(BehaviourProperty);
            var SubclassTypes = RSAssetUtility.FindSubclassOf<RayStormBehaviour>();
            var SubclassNames = RSDataUtility.ConvertArray(SubclassTypes, t => t.Name);

            int OldBehaviourID = -1;
            RayStormBehaviour CurrentBehaviour = BehaviourProperty.objectReferenceValue as RayStormBehaviour;
            if (CurrentBehaviour != null)
            {
                Type CurrentBehaviourType = CurrentBehaviour.GetType();
                OldBehaviourID = Array.FindIndex(SubclassTypes, t => t.Equals(CurrentBehaviourType));
            }

            EditorGUI.BeginChangeCheck();
            int NewBehaviourID = EditorGUILayout.Popup("Behaviour Type", OldBehaviourID, SubclassNames);
            if (EditorGUI.EndChangeCheck() && NewBehaviourID >= 0 && NewBehaviourID != OldBehaviourID)
            {
                int UndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Change RayStorm Behaviour");
                CurrentBehaviour = CreateInstance(SubclassTypes[NewBehaviourID]) as RayStormBehaviour;
                CurrentBehaviour.name = SubclassTypes[NewBehaviourID].Name;
                Undo.RegisterCreatedObjectUndo(CurrentBehaviour, "Create RayStorm Behaviour");
                BehaviourProperty.objectReferenceValue = CurrentBehaviour;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(CurrentBehaviour);
                Undo.CollapseUndoOperations(UndoGroup);
            }

            if (CurrentBehaviour != null)
            {
                SerializedObject BehaviourSettings = new SerializedObject(CurrentBehaviour);
                BehaviourSettings.Update();
                EditorGUILayout.PropertyField(BehaviourSettings.FindProperty("FrameCount"), new GUIContent("Frame Count", "0 renders indefinitely; positive values limit complete output frames."));
                BehaviourSettings.ApplyModifiedProperties();
            }
        }

        private void OnInspectorGUIInPlaying(RayStormRenderer RendererComponent)
        {
            string OctreeDisplayState = (RendererComponent.IsDisplayOctree ? "ON" : "OFF");
            if (GUILayout.Button("Display Octree : " + OctreeDisplayState, GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f)))
                serializedObject.FindProperty("IsDisplayOctree").boolValue = !RendererComponent.IsDisplayOctree;

            if (GUILayout.Button("Save Texture", GUILayout.Height(EditorGUIUtility.singleLineHeight * 1.5f)))
                RendererComponent.SaveRenderTexture();
        }
    }
}
