using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public static class RSSceneUtility
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Scene Collect Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static RSSceneObject[] CollectSceneObject(Camera MainCamera = null)
        {
            List<RSSceneObject> CollectedSceneObjectList = new List<RSSceneObject>();

            Renderer[] CollectedMeshRendererList = Object.FindObjectsOfType<Renderer>();
            foreach (Renderer MeshRenderer in CollectedMeshRendererList)
            {
                if (!MeshRenderer.enabled || MeshRenderer.forceRenderingOff ||
                    (MainCamera != null && (MainCamera.cullingMask & (1 << MeshRenderer.gameObject.layer)) == 0))
                    continue;

                RSSceneObject SceneObject = RSSceneObject.Default;
                SceneObject.Renderer = MeshRenderer;
                try
                {
                    if (MeshRenderer is SkinnedMeshRenderer SkinnedRenderer)
                    {
                        if (SkinnedRenderer.sharedMesh == null)
                            continue;
                        SceneObject.Type = ESceneObjectType.SkeletalMesh;
                        SceneObject.Mesh = new Mesh();
                        SceneObject.OwnsMesh = true;
                        SkinnedRenderer.BakeMesh(SceneObject.Mesh);
                    }
                    else if (MeshRenderer is UnityEngine.MeshRenderer && MeshRenderer.TryGetComponent<MeshFilter>(out var MeshFilter))
                    {
                        SceneObject.Type = ESceneObjectType.StaticMesh;
                        SceneObject.Mesh = MeshFilter.sharedMesh;
                    }
                    else
                        continue;

                    Mesh SourceMesh = SceneObject.Mesh;
                    if (SourceMesh == null || SourceMesh.vertexCount == 0)
                    {
                        ReleaseSceneObject(SceneObject);
                        continue;
                    }
                    if (!SourceMesh.isReadable)
                        throw new System.InvalidOperationException("Mesh must have Read/Write enabled.");
                    for (int SubmeshID = 0; SubmeshID < SourceMesh.subMeshCount; ++SubmeshID)
                    {
                        if (SourceMesh.GetTopology(SubmeshID) != MeshTopology.Triangles)
                            throw new System.InvalidOperationException("Only triangle submeshes are supported.");
                    }

                    if (SourceMesh.normals.Length != SourceMesh.vertexCount)
                    {
                        if (!SceneObject.OwnsMesh)
                        {
                            SceneObject.Mesh = Object.Instantiate(SourceMesh);
                            SceneObject.OwnsMesh = true;
                        }
                        SceneObject.Mesh.RecalculateNormals();
                    }
                    CollectedSceneObjectList.Add(SceneObject);
                }
                catch (System.Exception Exception)
                {
                    ReleaseSceneObject(SceneObject);
                    Debug.LogWarning("RayStorm skipped '" + MeshRenderer.name + "': " + Exception.Message, MeshRenderer);
                }
            }

            return CollectedSceneObjectList.ToArray();
        }

        public static void ReleaseSceneObjects(RSSceneObject[] SceneObjects)
        {
            if (SceneObjects == null)
                return;
            foreach (var SceneObject in SceneObjects)
                ReleaseSceneObject(SceneObject);
        }

        private static void ReleaseSceneObject(RSSceneObject SceneObject)
        {
            if (SceneObject == null || !SceneObject.OwnsMesh || SceneObject.Mesh == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(SceneObject.Mesh);
            else
                Object.DestroyImmediate(SceneObject.Mesh);
            SceneObject.Mesh = null;
            SceneObject.OwnsMesh = false;
        }

        public static T[] CollectSceneComponents<T>() where T : Object
        {
            var SceneGameObjectList = Object.FindObjectsOfType<GameObject>();

            List<T> CollectedSceneComponentList = new List<T>();
            foreach (var SceneGameObject in SceneGameObjectList)
            {
                var TargetComponent = SceneGameObject.GetComponent<T>();
                if (TargetComponent != null)
                    CollectedSceneComponentList.Add(TargetComponent);
            }

            return CollectedSceneComponentList.ToArray();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Scene Helper Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static void GetSceneAABBBox(RSSceneObject[] SceneObjects, out Vector3 OutputAABBMin, out Vector3 OutputAABBMax)
        {
            OutputAABBMin = Vector3.zero;
            OutputAABBMax = Vector3.zero;
            if (SceneObjects == null || SceneObjects.Length == 0)
                return;

            Vector3[] BoundBoxCornerOffsets = new Vector3[]
            {
            new Vector3(-1, -1, -1), new Vector3(-1, -1, 1),
            new Vector3(-1,  1, -1), new Vector3(-1,  1, 1),
            new Vector3( 1, -1, -1), new Vector3( 1, -1, 1),
            new Vector3( 1,  1, -1), new Vector3( 1,  1, 1)
            };

            OutputAABBMin = Vector3.one * float.MaxValue;
            OutputAABBMax = Vector3.one * float.MinValue;
            foreach (var SceneObject in SceneObjects)
            {
                foreach (var CornerOffset in BoundBoxCornerOffsets)
                {
                    var CornerPoint = SceneObject.Renderer.transform.TransformPoint(
                        SceneObject.Mesh.bounds.center + Vector3.Scale(SceneObject.Mesh.bounds.extents, CornerOffset));
                    OutputAABBMin = Vector3.Min(OutputAABBMin, CornerPoint);
                    OutputAABBMax = Vector3.Max(OutputAABBMax, CornerPoint);
                }
            }
        }

        public static void GetSceneAABBBox(out Vector3 OutputAABBMin, out Vector3 OutputAABBMax)
        {
            RSSceneObject[] SceneObjects = CollectSceneObject();
            try
            {
                GetSceneAABBBox(SceneObjects, out OutputAABBMin, out OutputAABBMax);
            }
            finally
            {
                ReleaseSceneObjects(SceneObjects);
            }
        }
    }
}
