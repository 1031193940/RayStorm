using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public static class RSSceneUtility
    {
        public enum ESceneObjectType
        {
            None = 0,
            Mesh,
            Customize,
        }

        public class SceneObject
        {
            public ESceneObjectType Type = ESceneObjectType.None;
            public Mesh Mesh = null;
            public Renderer Renderer = null;
            public RayStormCustomizeObject CustomizeObject = null;
        }

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

        public static SceneObject[] CollectSceneObject()
        {
            List<SceneObject> SceneObjectList = new List<SceneObject>();

            var MeshFilterList = CollectSceneComponents<MeshFilter>();
            foreach (var MeshFilter in MeshFilterList)
            {
                if (MeshFilter.GetComponent<RayStormCustomizeObject>() != null)
                    continue;

                SceneObject NewSceneObject = new SceneObject();
                NewSceneObject.Type = ESceneObjectType.Mesh;
                NewSceneObject.Mesh = MeshFilter.sharedMesh;
                NewSceneObject.Renderer = MeshFilter.GetComponent<Renderer>();
                SceneObjectList.Add(NewSceneObject);
            }

            var SkinnedMeshRendererList = CollectSceneComponents<SkinnedMeshRenderer>();
            foreach (var SkinnedMeshRenderer in SkinnedMeshRendererList)
            {
                if (SkinnedMeshRenderer.GetComponent<RayStormCustomizeObject>() != null)
                    continue;

                Mesh BakedMesh = new Mesh();
                SkinnedMeshRenderer.BakeMesh(BakedMesh);

                SceneObject NewSceneObject = new SceneObject();
                NewSceneObject.Type = ESceneObjectType.Mesh;
                NewSceneObject.Mesh = BakedMesh;
                NewSceneObject.Renderer = SkinnedMeshRenderer.GetComponent<Renderer>();
                SceneObjectList.Add(NewSceneObject);
            }

            var CustomizeObjectList = CollectSceneComponents<RayStormCustomizeObject>();
            foreach (var CustomizeObject in CustomizeObjectList)
            {
                SceneObject NewSceneObject = new SceneObject();
                NewSceneObject.Type = ESceneObjectType.Customize;
                NewSceneObject.Mesh = null;
                NewSceneObject.Renderer = CustomizeObject.GetComponent<Renderer>();
                NewSceneObject.CustomizeObject = CustomizeObject;
                SceneObjectList.Add(NewSceneObject);
            }

            return SceneObjectList.ToArray();
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
            Vector3 AABBSize = AABBMax - AABBMin + Vector3.one * 0.01f;
            Vector3 AABBCenter = (AABBMin + AABBMax) * 0.5f;
            AABBMin = AABBCenter - AABBSize * 0.5f;
            AABBMax = AABBCenter + AABBSize * 0.5f;

            SceneOctreeNode SceneOctreeRoot = new SceneOctreeNode();
            {
                SceneOctreeRoot.Depth = 0;
                SceneOctreeRoot.Center = (AABBMax + AABBMin) * 0.5f;
                SceneOctreeRoot.Size = AABBMax - AABBMin;

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

            var SceneActiveOctreeList = new List<SceneOctreeNode>();
            SceneActiveOctreeList.Add(SceneOctreeRoot);

            while (SceneActiveOctreeList.Count > 0)
            {
                SceneOctreeNode OctreeUnit = SceneActiveOctreeList[0];
                SceneActiveOctreeList.RemoveAt(0);

                if (OctreeUnit.GeometryIDList.Count <= 2 || OctreeUnit.Depth >= MaxRecursionDepth)
                    continue;

                Vector3 SubtreeUnitSize = OctreeUnit.Size * 0.5f;
                foreach (var SubtreeOffsetSign in SubtreeOffsetSignList)
                {
                    SceneOctreeNode SubtreeUnit = new SceneOctreeNode();
                    SubtreeUnit.Depth = OctreeUnit.Depth + 1;
                    SubtreeUnit.Size = SubtreeUnitSize;
                    SubtreeUnit.Center = OctreeUnit.Center + Vector3.Scale(SubtreeUnitSize, SubtreeOffsetSign) * 0.5f;

                    int SubGeometriesCount = OctreeUnit.GeometryIDList.Count;
                    for (int SubGeometryID = 0; SubGeometryID < SubGeometriesCount; ++SubGeometryID)
                    {
                        var GeometryID = OctreeUnit.GeometryIDList[SubGeometryID];
                        var GeometryData = SceneGeometries[GeometryID];
                        var v0 = SceneVertices[GeometryData.Indice0].Position;
                        var v1 = SceneVertices[GeometryData.Indice1].Position;
                        var v2 = SceneVertices[GeometryData.Indice2].Position;
                        if (RSMathUtility.IsTriangleIntersectWithAABBBox(v0, v1, v2, SubtreeUnit.Center, SubtreeUnit.Size))
                            SubtreeUnit.GeometryIDList.Add(GeometryID);
                    }

                    if (SubtreeUnit.GeometryIDList.Count > 0)
                        OctreeUnit.SubtreeList.Add(SubtreeUnit);
                }

                OctreeUnit.GeometryIDList.Clear();
                SceneActiveOctreeList.AddRange(OctreeUnit.SubtreeList);
            }

            return SceneOctreeRoot;
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

        public static void GetSceneAABBBox(SceneObject[] SceneObjects, out Vector3 OutputAABBMin, out Vector3 OutputAABBMax)
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
                    var CornerPoint = SceneObject.Mesh.bounds.center + Vector3.Scale(SceneObject.Mesh.bounds.extents, CornerOffset);
                    OutputAABBMin = Vector3.Min(OutputAABBMin, CornerPoint);
                    OutputAABBMax = Vector3.Max(OutputAABBMax, CornerPoint);
                }
            }
        }

        public static void GetSceneAABBBox(out Vector3 OutputAABBMin, out Vector3 OutputAABBMax)
        {
            SceneObject[] SceneObjects = CollectSceneObject();
            GetSceneAABBBox(SceneObjects, out OutputAABBMin, out OutputAABBMax);
        }
    }
}