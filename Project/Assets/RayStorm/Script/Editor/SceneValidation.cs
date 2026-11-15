using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RayStorm
{
    public static class SceneValidation
    {
        [MenuItem("RayStorm/Validate Scene Import and BVH")]
        public static void Run()
        {
            Require(!Application.isPlaying, "Run scene validation in Edit mode.");
            ValidateDegenerateBounds();
            ValidateNearestHits();
            ValidateParallelSlabs();
            ValidateSceneCollection();
            Debug.Log("RayStorm scene validation passed: BVH coverage, nearest hits, parallel slabs and scene collection.");
        }

        private static void ValidateDegenerateBounds()
        {
            foreach (float CenterHeight in new[] { 100.0f, 2048.0f, 262144.0f })
            {
                var SceneVertexList = new List<RSVertexData>();
                var SceneGeometryList = new List<RSGeometryData>();
                for (int TriangleID = 0; TriangleID < 17; ++TriangleID)
                    AddTriangle(SceneVertexList, SceneGeometryList, new Vector3(-1, CenterHeight, -1), new Vector3(1, CenterHeight, -1), new Vector3(0, CenterHeight, 1));
                RSVertexData[] SceneVertices = SceneVertexList.ToArray();
                RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
                RSBVHUtility.BuildSceneBVH(SceneVertices, SceneGeometries, out var Nodes, out var GeometryIDs);
                Require(Nodes.Length > 1, "BVH failed to split triangles with coincident centers.");
                ValidateCoverage(SceneVertices, SceneGeometries, Nodes, GeometryIDs);
                CompareHit(SceneVertices, SceneGeometries, Nodes, GeometryIDs, new Vector3(0, CenterHeight + 0.5f, 0), Vector3.down);
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
                AddTriangle(SceneVertexList, SceneGeometryList, Center + RandomVector(RandomGenerator, 1.0f), Center + RandomVector(RandomGenerator, 1.0f), Center + RandomVector(RandomGenerator, 1.0f));
            }
            RSVertexData[] SceneVertices = SceneVertexList.ToArray();
            RSGeometryData[] SceneGeometries = SceneGeometryList.ToArray();
            RSBVHUtility.BuildSceneBVH(SceneVertices, SceneGeometries, out var Nodes, out var GeometryIDs);
            ValidateCoverage(SceneVertices, SceneGeometries, Nodes, GeometryIDs);
            RandomGenerator = new System.Random(89173);
            for (int RayID = 0; RayID < 512; ++RayID)
            {
                Vector3 RaySource = RandomVector(RandomGenerator, 10.0f);
                RSGeometryData TargetTriangle = SceneGeometries[RayID % SceneGeometries.Length];
                Vector3 RayTarget = (SceneVertices[TargetTriangle.Indice0].Position + SceneVertices[TargetTriangle.Indice1].Position + SceneVertices[TargetTriangle.Indice2].Position) / 3.0f;
                CompareHit(SceneVertices, SceneGeometries, Nodes, GeometryIDs, RaySource, (RayTarget - RaySource).normalized);
            }
            RSBVHUtility.BuildSceneBVH(Array.Empty<RSVertexData>(), Array.Empty<RSGeometryData>(), out var EmptyNodes, out var EmptyGeometryIDs);
            Require(EmptyNodes.Length == 0 && EmptyGeometryIDs.Length == 0, "Empty scene should produce an empty BVH.");
        }

        private static void ValidateCoverage(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries, RSBVHData[] Nodes, int[] GeometryIDs)
        {
            Require(GeometryIDs.Length == SceneGeometries.Length, "BVH reference count differs from scene geometry.");
            var SeenGeometries = new HashSet<int>();
            var PendingNodes = new Stack<(int NodeID, int Depth)>();
            PendingNodes.Push((0, 0));
            int VisitedCount = 0;
            while (PendingNodes.Count > 0)
            {
                var (NodeID, Depth) = PendingNodes.Pop();
                RSBVHData Node = Nodes[NodeID];
                Require(++VisitedCount <= Nodes.Length && Depth <= 31, "BVH exceeds traversal bounds.");
                Require(Node.Minimum.x < Node.Maximum.x && Node.Minimum.y < Node.Maximum.y && Node.Minimum.z < Node.Maximum.z, "BVH bounds collapsed.");
                if (Node.GeometryCount == 0)
                {
                    Require(Node.StartID > NodeID && Node.StartID + 1 < Nodes.Length, "BVH child range is invalid.");
                    for (int ChildID = Node.StartID; ChildID <= Node.StartID + 1; ++ChildID)
                    {
                        RSBVHData Child = Nodes[ChildID];
                        Require(Contains(Node, Child.Minimum) && Contains(Node, Child.Maximum), "BVH parent does not enclose its child.");
                        PendingNodes.Push((ChildID, Depth + 1));
                    }
                    continue;
                }
                for (int GeometryOffset = 0; GeometryOffset < Node.GeometryCount; ++GeometryOffset)
                {
                    int GeometryID = GeometryIDs[Node.StartID + GeometryOffset];
                    Require(SeenGeometries.Add(GeometryID), "BVH contains a duplicate geometry reference.");
                    RSGeometryData Geometry = SceneGeometries[GeometryID];
                    Require(Contains(Node, SceneVertices[Geometry.Indice0].Position) && Contains(Node, SceneVertices[Geometry.Indice1].Position) && Contains(Node, SceneVertices[Geometry.Indice2].Position), "BVH leaf does not enclose its geometry.");
                }
            }
            Require(VisitedCount == Nodes.Length && SeenGeometries.Count == SceneGeometries.Length, "BVH contains unreachable nodes or missing geometry.");
        }

        private static bool Contains(RSBVHData Node, Vector3 Position)
        {
            return Position.x >= Node.Minimum.x && Position.x <= Node.Maximum.x && Position.y >= Node.Minimum.y && Position.y <= Node.Maximum.y && Position.z >= Node.Minimum.z && Position.z <= Node.Maximum.z;
        }

        private static void ValidateParallelSlabs()
        {
            Require(RSMathUtility.RayIntersectWithAABBBox(new Vector3(1, 0, -2), Vector3.forward, Vector3.zero, Vector3.one * 2, out float EntryDistance, out _), "Boundary-parallel ray missed.");
            Require(Mathf.Abs(EntryDistance - 1) < 0.00001f, "Unexpected slab entry distance.");
            Require(!RSMathUtility.RayIntersectWithAABBBox(new Vector3(1.1f, 0, -2), Vector3.forward, Vector3.zero, Vector3.one * 2, out _, out _), "Outside parallel ray should miss.");
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

        private static void CompareHit(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries, RSBVHData[] Nodes, int[] GeometryIDs, Vector3 RaySource, Vector3 RayDirection)
        {
            float BruteDistance = float.PositiveInfinity;
            foreach (var Geometry in SceneGeometries)
                TestTriangle(SceneVertices, Geometry, RaySource, RayDirection, ref BruteDistance);

            float TreeDistance = float.PositiveInfinity;
            var PendingNodes = new Stack<int>();
            PendingNodes.Push(0);
            while (PendingNodes.Count > 0)
            {
                RSBVHData Node = Nodes[PendingNodes.Pop()];
                Vector3 Size = Node.Maximum - Node.Minimum;
                if (!RSMathUtility.RayIntersectWithAABBBox(RaySource, RayDirection, Node.Minimum + Size * 0.5f, Size, out float EntryDistance, out _) || EntryDistance > TreeDistance)
                    continue;
                if (Node.GeometryCount == 0)
                {
                    PendingNodes.Push(Node.StartID + 1);
                    PendingNodes.Push(Node.StartID);
                }
                else
                    for (int GeometryOffset = 0; GeometryOffset < Node.GeometryCount; ++GeometryOffset)
                        TestTriangle(SceneVertices, SceneGeometries[GeometryIDs[Node.StartID + GeometryOffset]], RaySource, RayDirection, ref TreeDistance);
            }
            bool BruteMissed = float.IsPositiveInfinity(BruteDistance);
            bool TreeMissed = float.IsPositiveInfinity(TreeDistance);
            Require(BruteMissed == TreeMissed && (BruteMissed || Mathf.Abs(BruteDistance - TreeDistance) <= 0.0001f * Mathf.Max(1, BruteDistance)), "BVH and brute force disagree on nearest hit.");
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
            return new Vector3((float)(RandomGenerator.NextDouble() * 2 - 1), (float)(RandomGenerator.NextDouble() * 2 - 1), (float)(RandomGenerator.NextDouble() * 2 - 1)) * Extent;
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
