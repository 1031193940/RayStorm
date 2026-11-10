using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RayStorm
{
    public static class SceneValidation
    {
        [MenuItem("RayStorm/Validate Scene Import and Octree")]
        public static void Run()
        {
            Require(!Application.isPlaying, "Run scene validation in Edit mode.");
            ValidateSplitPlaneCoverage();
            ValidateNearestHits();
            ValidateParallelSlabs();
            ValidateSceneCollection();
            Debug.Log("RayStorm scene validation passed: split-plane coverage, nearest hits, parallel slabs and scene collection.");
        }

        private static void ValidateSplitPlaneCoverage()
        {
            // Regression: the middle triangle was lost at the first split.
            foreach (float CenterHeight in new[] { 100.0f, 2048.0f, 262144.0f })
            {
                var SceneVertexList = new List<RSVertexData>();
                var SceneGeometryList = new List<RSGeometryData>();
                for (int TriangleID = 0; TriangleID < 3; ++TriangleID)
                {
                    float Height = CenterHeight + TriangleID - 1;
                    AddTriangle(SceneVertexList, SceneGeometryList, new Vector3(-1, Height, -1),
                        new Vector3(1, Height, -1), new Vector3(0, Height, 1));
                }
                RSVertexData[] SceneVertices = SceneVertexList.ToArray();
                RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
                RSSceneUtility.BuildSceneOctree(SceneVertices, SceneGeometries, 7,
                    out var OctreeNodes, out var OctreeGeometryIDs);
                var RepresentedGeometries = new HashSet<int>(OctreeGeometryIDs);
                Require(RepresentedGeometries.Count == 3, "Octree dropped a triangle on a split plane.");
                foreach (var OctreeNode in OctreeNodes)
                {
                    Vector3 Minimum = OctreeNode.Center - OctreeNode.Size * 0.5f;
                    Vector3 Maximum = OctreeNode.Center + OctreeNode.Size * 0.5f;
                    Require(Minimum.x < Maximum.x && Minimum.y < Maximum.y && Minimum.z < Maximum.z,
                        "Octree bounds collapsed at large world coordinates.");
                }
                CompareHit(SceneVertices, SceneGeometries, OctreeNodes, OctreeGeometryIDs,
                    new Vector3(0, CenterHeight + 0.5f, 0), Vector3.down);
            }
        }

        private static void ValidateNearestHits()
        {
            var RandomGenerator = new System.Random(89173);
            var SceneVertexList = new List<RSVertexData>();
            var SceneGeometryList = new List<RSGeometryData>();
            for (int TriangleID = 0; TriangleID < 48; ++TriangleID)
            {
                Vector3 Center = RandomVector(RandomGenerator, 5.0f);
                AddTriangle(SceneVertexList, SceneGeometryList, Center + RandomVector(RandomGenerator, 1.0f),
                    Center + RandomVector(RandomGenerator, 1.0f), Center + RandomVector(RandomGenerator, 1.0f));
            }
            RSVertexData[] SceneVertices = SceneVertexList.ToArray();
            RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
            RSSceneUtility.BuildSceneOctree(SceneVertices, SceneGeometries, 7, out var OctreeNodes, out var OctreeGeometryIDs);
            for (int RayID = 0; RayID < 512; ++RayID)
            {
                Vector3 RaySource = RandomVector(RandomGenerator, 10.0f);
                RSGeometryData TargetTriangle = SceneGeometries[RayID % SceneGeometries.Length];
                Vector3 RayTarget = (SceneVertices[TargetTriangle.Indice0].Position +
                    SceneVertices[TargetTriangle.Indice1].Position + SceneVertices[TargetTriangle.Indice2].Position) / 3.0f;
                CompareHit(SceneVertices, SceneGeometries, OctreeNodes, OctreeGeometryIDs, RaySource, (RayTarget - RaySource).normalized);
            }
            RSSceneUtility.BuildSceneOctree(Array.Empty<RSVertexData>(), Array.Empty<RSGeometryData>(),
                7, out var EmptyNodes, out var EmptyGeometryIDs);
            Require(EmptyNodes.Length == 1 && EmptyNodes[0].GeometryCount == 0 && EmptyGeometryIDs.Length == 0,
                "Empty scene should produce one empty root.");
        }

        private static void ValidateParallelSlabs()
        {
            Require(RSMathUtility.RayIntersectWithAABBBox(new Vector3(1, 0, -2), Vector3.forward,
                Vector3.zero, Vector3.one * 2, out float EntryDistance, out _), "Boundary-parallel ray missed.");
            Require(Mathf.Abs(EntryDistance - 1) < 0.00001f, "Unexpected slab entry distance.");
            Require(!RSMathUtility.RayIntersectWithAABBBox(new Vector3(1.1f, 0, -2), Vector3.forward,
                Vector3.zero, Vector3.one * 2, out _, out _), "Outside parallel ray should miss.");
        }

        private static void ValidateSceneCollection()
        {
            var TestObjects = new List<GameObject>();
            var SourceMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                triangles = new[] { 0, 1, 2 }
            };
            RSSceneObject[] CollectedObjects = null;
            Mesh OwnedSnapshot = null;
            try
            {
                var CameraObject = NewTestObject(TestObjects, "Camera");
                Camera MainCamera = CameraObject.AddComponent<Camera>();
                MainCamera.cullingMask = 1 << 31;
                var VisibleObject = NewTestObject(TestObjects, "Visible");
                VisibleObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                MeshRenderer VisibleRenderer = VisibleObject.AddComponent<MeshRenderer>();
                var DisabledObject = NewTestObject(TestObjects, "Disabled");
                DisabledObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                DisabledObject.AddComponent<MeshRenderer>().enabled = false;
                var MaskedObject = NewTestObject(TestObjects, "Masked");
                MaskedObject.layer = 30;
                MaskedObject.AddComponent<MeshFilter>().sharedMesh = SourceMesh;
                MaskedObject.AddComponent<MeshRenderer>();
                NewTestObject(TestObjects, "Line").AddComponent<LineRenderer>();
                NewTestObject(TestObjects, "Empty").AddComponent<MeshRenderer>();

                CollectedObjects = RSSceneUtility.CollectSceneObject(MainCamera);
                RSSceneObject ImportedObject = Array.Find(CollectedObjects, SceneObject => SceneObject.Renderer == VisibleRenderer);
                Require(ImportedObject != null && ImportedObject.OwnsMesh, "Missing normals require an owned snapshot mesh.");
                Require(ImportedObject.Mesh.normals.Length == SourceMesh.vertexCount, "Snapshot normals were not generated.");
                Require(SourceMesh.normals.Length == 0, "Scene collection mutated the source mesh.");
                foreach (var SceneObject in CollectedObjects)
                {
                    if (TestObjects.Contains(SceneObject.Renderer.gameObject))
                        Require(SceneObject.Renderer == VisibleRenderer, "Disabled, masked or unsupported renderer was collected.");
                }
                OwnedSnapshot = ImportedObject.Mesh;
            }
            finally
            {
                RSSceneUtility.ReleaseSceneObjects(CollectedObjects);
                foreach (var TestObject in TestObjects)
                    UnityEngine.Object.DestroyImmediate(TestObject);
                UnityEngine.Object.DestroyImmediate(SourceMesh);
            }
            Require(OwnedSnapshot == null, "Owned snapshot mesh was not destroyed in edit mode.");
        }

        private static GameObject NewTestObject(List<GameObject> TestObjects, string Name)
        {
            var TestObject = new GameObject("RayStormValidation_" + Name) { layer = 31 };
            TestObjects.Add(TestObject);
            return TestObject;
        }

        private static void CompareHit(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries,
            RSOctreeData[] OctreeNodes, int[] OctreeGeometryIDs, Vector3 RaySource, Vector3 RayDirection)
        {
            float BruteDistance = float.PositiveInfinity;
            foreach (var Geometry in SceneGeometries)
                TestTriangle(SceneVertices, Geometry, RaySource, RayDirection, ref BruteDistance);

            float TreeDistance = float.PositiveInfinity;
            var PendingNodes = new Stack<int>();
            PendingNodes.Push(0);
            int VisitedCount = 0;
            while (PendingNodes.Count > 0)
            {
                Require(++VisitedCount <= OctreeNodes.Length, "Octree contains a cycle or shared child.");
                RSOctreeData OctreeNode = OctreeNodes[PendingNodes.Pop()];
                if (!RSMathUtility.RayIntersectWithAABBBox(RaySource, RayDirection, OctreeNode.Center, OctreeNode.Size,
                    out float EntryDistance, out _) || EntryDistance > TreeDistance)
                    continue;
                for (int GeometryOffset = 0; GeometryOffset < OctreeNode.GeometryCount; ++GeometryOffset)
                    TestTriangle(SceneVertices, SceneGeometries[OctreeGeometryIDs[OctreeNode.GeometryStartID + GeometryOffset]],
                        RaySource, RayDirection, ref TreeDistance);
                for (int ChildOffset = 0; ChildOffset < OctreeNode.SubtreeCount; ++ChildOffset)
                    PendingNodes.Push(OctreeNode.SubtreeStartID + ChildOffset);
            }
            bool BruteMissed = float.IsPositiveInfinity(BruteDistance);
            bool TreeMissed = float.IsPositiveInfinity(TreeDistance);
            Require(BruteMissed == TreeMissed && (BruteMissed ||
                Mathf.Abs(BruteDistance - TreeDistance) <= 0.0001f * Mathf.Max(1, BruteDistance)),
                "Octree and brute force disagree on nearest hit.");
        }

        private static void TestTriangle(RSVertexData[] SceneVertices, RSGeometryData Geometry,
            Vector3 RaySource, Vector3 RayDirection, ref float ClosestDistance)
        {
            Vector3 FirstPosition = SceneVertices[Geometry.Indice0].Position;
            Vector3 SecondPosition = SceneVertices[Geometry.Indice1].Position;
            Vector3 ThirdPosition = SceneVertices[Geometry.Indice2].Position;
            if (RSMathUtility.RayIntersectWithTriangle(RaySource, RayDirection, FirstPosition, SecondPosition, ThirdPosition, out var Weights))
            {
                Vector3 Position = RSMathUtility.TriangularInterpolate(FirstPosition, SecondPosition, ThirdPosition, Weights);
                ClosestDistance = Mathf.Min(ClosestDistance, Vector3.Distance(RaySource, Position));
            }
        }

        private static void AddTriangle(List<RSVertexData> SceneVertexList, List<RSGeometryData> SceneGeometryList,
            Vector3 FirstPosition, Vector3 SecondPosition, Vector3 ThirdPosition)
        {
            int VertexStart = SceneVertexList.Count;
            SceneVertexList.Add(new RSVertexData { Position = FirstPosition });
            SceneVertexList.Add(new RSVertexData { Position = SecondPosition });
            SceneVertexList.Add(new RSVertexData { Position = ThirdPosition });
            SceneGeometryList.Add(new RSGeometryData { Indice0 = VertexStart, Indice1 = VertexStart + 1, Indice2 = VertexStart + 2 });
        }

        private static Vector3 RandomVector(System.Random RandomGenerator, float Extent)
        {
            return new Vector3((float)(RandomGenerator.NextDouble() * 2 - 1),
                (float)(RandomGenerator.NextDouble() * 2 - 1), (float)(RandomGenerator.NextDouble() * 2 - 1)) * Extent;
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
