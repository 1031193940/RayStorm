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
            // This exact input used to lose the middle triangle at the first split.
            foreach (float centerHeight in new[] { 100.0f, 2048.0f, 262144.0f })
            {
                var vertices = new List<RSVertexData>();
                var geometries = new List<RSGeometryData>();
                for (int triangleIndex = 0; triangleIndex < 3; ++triangleIndex)
                {
                    float height = centerHeight + triangleIndex - 1;
                    AddTriangle(vertices, geometries, new Vector3(-1, height, -1),
                        new Vector3(1, height, -1), new Vector3(0, height, 1));
                }
                RSSceneUtility.BuildSceneOctree(vertices.ToArray(), geometries.ToArray(), 7,
                    out var nodes, out var geometryIDs);
                var representedGeometries = new HashSet<int>(geometryIDs);
                Require(representedGeometries.Count == 3, "Octree dropped a triangle on a split plane.");
                foreach (var node in nodes)
                {
                    Vector3 minimum = node.Center - node.Size * 0.5f;
                    Vector3 maximum = node.Center + node.Size * 0.5f;
                    Require(minimum.x < maximum.x && minimum.y < maximum.y && minimum.z < maximum.z,
                        "Octree bounds collapsed at large world coordinates.");
                }
                CompareHit(vertices.ToArray(), geometries.ToArray(), nodes, geometryIDs,
                    new Vector3(0, centerHeight + 0.5f, 0), Vector3.down);
            }
        }

        private static void ValidateNearestHits()
        {
            var random = new System.Random(89173);
            var vertices = new List<RSVertexData>();
            var geometries = new List<RSGeometryData>();
            for (int triangleIndex = 0; triangleIndex < 48; ++triangleIndex)
            {
                Vector3 center = RandomVector(random, 5.0f);
                AddTriangle(vertices, geometries, center + RandomVector(random, 1.0f),
                    center + RandomVector(random, 1.0f), center + RandomVector(random, 1.0f));
            }
            RSVertexData[] vertexArray = vertices.ToArray();
            RSGeometryData[] geometryArray = geometries.ToArray();
            RSSceneUtility.BuildSceneOctree(vertexArray, geometryArray, 7, out var nodes, out var geometryIDs);
            for (int rayIndex = 0; rayIndex < 512; ++rayIndex)
            {
                Vector3 origin = RandomVector(random, 10.0f);
                RSGeometryData targetTriangle = geometryArray[rayIndex % geometryArray.Length];
                Vector3 target = (vertexArray[targetTriangle.Indice0].Position +
                    vertexArray[targetTriangle.Indice1].Position + vertexArray[targetTriangle.Indice2].Position) / 3.0f;
                CompareHit(vertexArray, geometryArray, nodes, geometryIDs, origin, (target - origin).normalized);
            }
            RSSceneUtility.BuildSceneOctree(Array.Empty<RSVertexData>(), Array.Empty<RSGeometryData>(),
                7, out var emptyNodes, out var emptyGeometryIDs);
            Require(emptyNodes.Length == 1 && emptyNodes[0].GeometryCount == 0 && emptyGeometryIDs.Length == 0,
                "Empty scene should produce one empty root.");
        }

        private static void ValidateParallelSlabs()
        {
            Require(RSMathUtility.RayIntersectWithAABBBox(new Vector3(1, 0, -2), Vector3.forward,
                Vector3.zero, Vector3.one * 2, out float entryDistance, out _), "Boundary-parallel ray missed.");
            Require(Mathf.Abs(entryDistance - 1) < 0.00001f, "Unexpected slab entry distance.");
            Require(!RSMathUtility.RayIntersectWithAABBBox(new Vector3(1.1f, 0, -2), Vector3.forward,
                Vector3.zero, Vector3.one * 2, out _, out _), "Outside parallel ray should miss.");
        }

        private static void ValidateSceneCollection()
        {
            var testObjects = new List<GameObject>();
            var sourceMesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                triangles = new[] { 0, 1, 2 }
            };
            RSSceneObject[] collectedObjects = null;
            try
            {
                var cameraObject = NewTestObject(testObjects, "Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.cullingMask = 1 << 31;
                var visibleObject = NewTestObject(testObjects, "Visible");
                visibleObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
                MeshRenderer visibleRenderer = visibleObject.AddComponent<MeshRenderer>();
                var disabledObject = NewTestObject(testObjects, "Disabled");
                disabledObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
                disabledObject.AddComponent<MeshRenderer>().enabled = false;
                var maskedObject = NewTestObject(testObjects, "Masked");
                maskedObject.layer = 30;
                maskedObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
                maskedObject.AddComponent<MeshRenderer>();
                NewTestObject(testObjects, "Line").AddComponent<LineRenderer>();
                NewTestObject(testObjects, "Empty").AddComponent<MeshRenderer>();

                collectedObjects = RSSceneUtility.CollectSceneObject(camera);
                RSSceneObject importedObject = Array.Find(collectedObjects, item => item.Renderer == visibleRenderer);
                Require(importedObject != null && importedObject.OwnsMesh, "Missing normals require an owned snapshot mesh.");
                Require(importedObject.Mesh.normals.Length == sourceMesh.vertexCount, "Snapshot normals were not generated.");
                Require(sourceMesh.normals.Length == 0, "Scene collection mutated the source mesh.");
                foreach (var item in collectedObjects)
                {
                    if (testObjects.Contains(item.Renderer.gameObject))
                        Require(item.Renderer == visibleRenderer, "Disabled, masked or unsupported renderer was collected.");
                }
                Mesh ownedSnapshot = importedObject.Mesh;
                RSSceneUtility.ReleaseSceneObjects(collectedObjects);
                Require(ownedSnapshot == null, "Owned snapshot mesh was not destroyed in edit mode.");
                collectedObjects = null;
            }
            finally
            {
                RSSceneUtility.ReleaseSceneObjects(collectedObjects);
                foreach (var testObject in testObjects)
                    UnityEngine.Object.DestroyImmediate(testObject);
                UnityEngine.Object.DestroyImmediate(sourceMesh);
            }
        }

        private static GameObject NewTestObject(List<GameObject> objects, string name)
        {
            var testObject = new GameObject("RayStormValidation_" + name) { layer = 31 };
            objects.Add(testObject);
            return testObject;
        }

        private static void CompareHit(RSVertexData[] vertices, RSGeometryData[] geometries,
            RSOctreeData[] nodes, int[] geometryIDs, Vector3 origin, Vector3 direction)
        {
            float bruteDistance = float.PositiveInfinity;
            foreach (var geometry in geometries)
                TestTriangle(vertices, geometry, origin, direction, ref bruteDistance);

            float treeDistance = float.PositiveInfinity;
            var pendingNodes = new Stack<int>();
            pendingNodes.Push(0);
            int visitedCount = 0;
            while (pendingNodes.Count > 0)
            {
                Require(++visitedCount <= nodes.Length, "Octree contains a cycle or shared child.");
                RSOctreeData node = nodes[pendingNodes.Pop()];
                if (!RSMathUtility.RayIntersectWithAABBBox(origin, direction, node.Center, node.Size,
                    out float entryDistance, out _) || entryDistance > treeDistance)
                    continue;
                for (int geometryOffset = 0; geometryOffset < node.GeometryCount; ++geometryOffset)
                    TestTriangle(vertices, geometries[geometryIDs[node.GeometryStartID + geometryOffset]],
                        origin, direction, ref treeDistance);
                for (int childOffset = 0; childOffset < node.SubtreeCount; ++childOffset)
                    pendingNodes.Push(node.SubtreeStartID + childOffset);
            }
            bool bruteMissed = float.IsPositiveInfinity(bruteDistance);
            bool treeMissed = float.IsPositiveInfinity(treeDistance);
            Require(bruteMissed == treeMissed && (bruteMissed ||
                Mathf.Abs(bruteDistance - treeDistance) <= 0.0001f * Mathf.Max(1, bruteDistance)),
                "Octree and brute force disagree on nearest hit.");
        }

        private static void TestTriangle(RSVertexData[] vertices, RSGeometryData geometry,
            Vector3 origin, Vector3 direction, ref float closestDistance)
        {
            Vector3 first = vertices[geometry.Indice0].Position;
            Vector3 second = vertices[geometry.Indice1].Position;
            Vector3 third = vertices[geometry.Indice2].Position;
            if (RSMathUtility.RayIntersectWithTriangle(origin, direction, first, second, third, out var weights))
            {
                Vector3 position = RSMathUtility.TriangularInterpolate(first, second, third, weights);
                closestDistance = Mathf.Min(closestDistance, Vector3.Distance(origin, position));
            }
        }

        private static void AddTriangle(List<RSVertexData> vertices, List<RSGeometryData> geometries,
            Vector3 first, Vector3 second, Vector3 third)
        {
            int vertexStart = vertices.Count;
            vertices.Add(new RSVertexData { Position = first });
            vertices.Add(new RSVertexData { Position = second });
            vertices.Add(new RSVertexData { Position = third });
            geometries.Add(new RSGeometryData { Indice0 = vertexStart, Indice1 = vertexStart + 1, Indice2 = vertexStart + 2 });
        }

        private static Vector3 RandomVector(System.Random random, float extent)
        {
            return new Vector3((float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1)) * extent;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
