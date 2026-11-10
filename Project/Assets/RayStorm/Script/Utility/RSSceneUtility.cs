using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public static class RSSceneUtility
    {
        private class SceneOctreeNode
        {
            public int Depth = 0;
            public Vector3 Center = Vector3.zero;
            public Vector3 Size = Vector3.zero;
            public List<int> GeometryIDList = new List<int>();
            public List<SceneOctreeNode> SubtreeList = new List<SceneOctreeNode>();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Scene Collect Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static RSSceneObject[] CollectSceneObject(Camera camera = null)
        {
            List<RSSceneObject> CollectedSceneObjectList = new List<RSSceneObject>();

            Renderer[] CollectedMeshRendererList = Object.FindObjectsOfType<Renderer>();
            foreach (Renderer MeshRenderer in CollectedMeshRendererList)
            {
                if (!MeshRenderer.enabled || MeshRenderer.forceRenderingOff ||
                    !MeshRenderer.gameObject.activeInHierarchy ||
                    (camera != null && (camera.cullingMask & (1 << MeshRenderer.gameObject.layer)) == 0))
                    continue;

                RSSceneObject SceneObject = RSSceneObject.Default;
                SceneObject.Renderer = MeshRenderer;
                try
                {
                    if (MeshRenderer is SkinnedMeshRenderer skinnedRenderer)
                    {
                        if (skinnedRenderer.sharedMesh == null)
                            continue;
                        SceneObject.Type = ESceneObjectType.SkeletalMesh;
                        SceneObject.Mesh = new Mesh();
                        SceneObject.OwnsMesh = true;
                        skinnedRenderer.BakeMesh(SceneObject.Mesh);
                    }
                    else if (MeshRenderer is UnityEngine.MeshRenderer && MeshRenderer.TryGetComponent<MeshFilter>(out var meshFilter))
                    {
                        SceneObject.Type = ESceneObjectType.StaticMesh;
                        SceneObject.Mesh = meshFilter.sharedMesh;
                    }
                    else
                        continue;

                    Mesh sourceMesh = SceneObject.Mesh;
                    if (sourceMesh == null || sourceMesh.vertexCount == 0)
                    {
                        ReleaseSceneObject(SceneObject);
                        continue;
                    }
                    if (!sourceMesh.isReadable)
                        throw new System.InvalidOperationException("Mesh must have Read/Write enabled.");
                    for (int submeshIndex = 0; submeshIndex < sourceMesh.subMeshCount; ++submeshIndex)
                    {
                        if (sourceMesh.GetTopology(submeshIndex) != MeshTopology.Triangles)
                            throw new System.InvalidOperationException("Only triangle submeshes are supported.");
                    }

                    if (sourceMesh.normals.Length != sourceMesh.vertexCount)
                    {
                        if (!SceneObject.OwnsMesh)
                        {
                            SceneObject.Mesh = Object.Instantiate(sourceMesh);
                            SceneObject.OwnsMesh = true;
                        }
                        SceneObject.Mesh.RecalculateNormals();
                    }
                    CollectedSceneObjectList.Add(SceneObject);
                }
                catch (System.Exception exception)
                {
                    ReleaseSceneObject(SceneObject);
                    Debug.LogWarning("RayStorm skipped '" + MeshRenderer.name + "': " + exception.Message, MeshRenderer);
                }
            }

            return CollectedSceneObjectList.ToArray();
        }

        public static void ReleaseSceneObjects(RSSceneObject[] sceneObjects)
        {
            if (sceneObjects == null)
                return;
            foreach (var sceneObject in sceneObjects)
                ReleaseSceneObject(sceneObject);
        }

        private static void ReleaseSceneObject(RSSceneObject sceneObject)
        {
            if (sceneObject == null || !sceneObject.OwnsMesh || sceneObject.Mesh == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(sceneObject.Mesh);
            else
                Object.DestroyImmediate(sceneObject.Mesh);
            sceneObject.Mesh = null;
            sceneObject.OwnsMesh = false;
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
        // Scene Octree Algorithm |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static void BuildSceneOctree(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries, int MaxRecursionDepth, out RSOctreeData[] OutputOctreeData, out int[] OutputOctreeGeometryID)
        {
            SceneOctreeNode OctreeRoot = BuildGeometryOctree(SceneVertices, SceneGeometries, MaxRecursionDepth);

            List<int> OctreeGeometries = new List<int>();
            List<RSOctreeData> OctreeNodes = new List<RSOctreeData>();
            {
                List<SceneOctreeNode> OctreeUnitList = new List<SceneOctreeNode>();
                OctreeUnitList.Add(OctreeRoot);

                for (int OctreeID = 0; OctreeID < OctreeUnitList.Count; ++OctreeID)
                {
                    RSOctreeData NewOctreeUnit = RSOctreeData.Default;
                    NewOctreeUnit.ParentTreeID = -1; // Parent octree node ID will be setted latter
                    NewOctreeUnit.Size = OctreeUnitList[OctreeID].Size;
                    NewOctreeUnit.Center = OctreeUnitList[OctreeID].Center;
                    NewOctreeUnit.SubtreeStartID = OctreeUnitList.Count;
                    NewOctreeUnit.SubtreeCount = OctreeUnitList[OctreeID].SubtreeList.Count;
                    NewOctreeUnit.GeometryStartID = OctreeGeometries.Count;
                    NewOctreeUnit.GeometryCount = OctreeUnitList[OctreeID].GeometryIDList.Count;
                    OctreeNodes.Add(NewOctreeUnit);

                    OctreeGeometries.AddRange(OctreeUnitList[OctreeID].GeometryIDList);

                    OctreeUnitList.AddRange(OctreeUnitList[OctreeID].SubtreeList);
                }
            }

            int OctreesCount = OctreeNodes.Count;
            for (int OctreeID = 0; OctreeID < OctreesCount; ++OctreeID)
            {
                RSOctreeData OctreeData = OctreeNodes[OctreeID];
                for (int SubtreeID = OctreeData.SubtreeStartID; SubtreeID < OctreeData.SubtreeStartID + OctreeData.SubtreeCount; ++SubtreeID)
                {
                    RSOctreeData SubtreeData = OctreeNodes[SubtreeID];
                    SubtreeData.ParentTreeID = OctreeID;
                    OctreeNodes[SubtreeID] = SubtreeData;
                }
            }

            OutputOctreeData = OctreeNodes.ToArray();
            OutputOctreeGeometryID = OctreeGeometries.ToArray();
        }

        private static SceneOctreeNode BuildGeometryOctree(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries, int MaxRecursionDepth)
        {
            Vector3 AABBMin, AABBMax;
            GetSceneAABBBox(SceneVertices, out AABBMin, out AABBMax);

            // Correct Bounding-Box
            Vector3 AABBCenter = AABBMin + (AABBMax - AABBMin) * 0.5f;
            Vector3 AABBSize = ConservativeBoxSize(AABBCenter, AABBMax - AABBMin + Vector3.one * 0.01f);

            SceneOctreeNode SceneOctreeRoot = new SceneOctreeNode();
            {
                SceneOctreeRoot.Depth = 0;
                SceneOctreeRoot.Center = AABBCenter;
                SceneOctreeRoot.Size = AABBSize;

                int GeometriesCount = SceneGeometries.Length;
                for (int GeometryID = 0; GeometryID < GeometriesCount; ++GeometryID)
                    SceneOctreeRoot.GeometryIDList.Add(GeometryID);
            }

            var SubtreeOffsetSignList = new Vector3[]
            {
                new Vector3(-1, -1, -1), new Vector3(-1, -1, 1),
                new Vector3(-1,  1, -1), new Vector3(-1,  1, 1),
                new Vector3( 1, -1, -1), new Vector3( 1, -1, 1),
                new Vector3( 1,  1, -1), new Vector3( 1,  1, 1),
            };

            var SceneActiveOctreeList = new Queue<SceneOctreeNode>();
            SceneActiveOctreeList.Enqueue(SceneOctreeRoot);

            while (SceneActiveOctreeList.Count > 0)
            {
                SceneOctreeNode OctreeUnit = SceneActiveOctreeList.Dequeue();

                if (OctreeUnit.GeometryIDList.Count <= 2 || OctreeUnit.Depth >= MaxRecursionDepth)
                    continue;

                Vector3 SubtreeUnitSize = OctreeUnit.Size * 0.5f;
                var assignedGeometryIDs = new HashSet<int>();
                bool hasReducedChild = false;
                foreach (var SubtreeOffsetSign in SubtreeOffsetSignList)
                {
                    SceneOctreeNode SubtreeUnit = new SceneOctreeNode();
                    SubtreeUnit.Depth = OctreeUnit.Depth + 1;
                    SubtreeUnit.Center = OctreeUnit.Center + Vector3.Scale(SubtreeUnitSize, SubtreeOffsetSign) * 0.5f;
                    SubtreeUnit.Size = ConservativeBoxSize(SubtreeUnit.Center, SubtreeUnitSize);

                    int SubGeometriesCount = OctreeUnit.GeometryIDList.Count;
                    for (int SubGeometryID = 0; SubGeometryID < SubGeometriesCount; ++SubGeometryID)
                    {
                        var GeometryID = OctreeUnit.GeometryIDList[SubGeometryID];
                        var GeometryData = SceneGeometries[GeometryID];
                        var v0 = SceneVertices[GeometryData.Indice0].Position;
                        var v1 = SceneVertices[GeometryData.Indice1].Position;
                        var v2 = SceneVertices[GeometryData.Indice2].Position;
                        if (RSMathUtility.IsTriangleIntersectWithAABBBox(v0, v1, v2, SubtreeUnit.Center, SubtreeUnit.Size))
                        {
                            SubtreeUnit.GeometryIDList.Add(GeometryID);
                            assignedGeometryIDs.Add(GeometryID);
                        }
                    }

                    if (SubtreeUnit.GeometryIDList.Count > 0)
                    {
                        OctreeUnit.SubtreeList.Add(SubtreeUnit);
                        hasReducedChild |= SubtreeUnit.GeometryIDList.Count < OctreeUnit.GeometryIDList.Count;
                    }
                }

                // Keep a complete leaf if rounding dropped any triangle, or subdivision
                // would only duplicate the same geometry into every populated child.
                if (assignedGeometryIDs.Count == OctreeUnit.GeometryIDList.Count && hasReducedChild)
                {
                    OctreeUnit.GeometryIDList.Clear();
                    foreach (var subtree in OctreeUnit.SubtreeList)
                        SceneActiveOctreeList.Enqueue(subtree);
                }
                else
                    OctreeUnit.SubtreeList.Clear();
            }

            return SceneOctreeRoot;
        }

        private static Vector3 ConservativeBoxSize(Vector3 center, Vector3 size)
        {
            // Include several float ULPs on both sides. This also prevents thin
            // boxes at large world coordinates from collapsing after subdivision.
            const float relativePadding = 8.0f * 1.192092896e-7f;
            Vector3 padding = new Vector3(
                Mathf.Max(1.0f, Mathf.Abs(center.x)) * relativePadding,
                Mathf.Max(1.0f, Mathf.Abs(center.y)) * relativePadding,
                Mathf.Max(1.0f, Mathf.Abs(center.z)) * relativePadding);
            return size + padding * 2.0f;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // Scene Helper Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        private static void GetSceneAABBBox(RSVertexData[] SceneVertices, out Vector3 OutputAABBMin, out Vector3 OutputAABBMax)
        {
            OutputAABBMin = Vector3.zero;
            OutputAABBMax = Vector3.zero;

            if (SceneVertices.Length > 0)
            {
                OutputAABBMin = Vector3.one * float.MaxValue;
                OutputAABBMax = Vector3.one * float.MinValue;
                foreach (var VertexData in SceneVertices)
                {
                    OutputAABBMin = Vector3.Min(OutputAABBMin, VertexData.Position);
                    OutputAABBMax = Vector3.Max(OutputAABBMax, VertexData.Position);
                }
            }
        }

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
