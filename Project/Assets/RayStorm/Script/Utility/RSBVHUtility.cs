using System;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public static class RSBVHUtility
    {
        public const int MaximumDepth = 31;
        private const int BucketCount = 12;
        private const int LeafSize = 8;

        public static void BuildSceneBVH(RSVertexData[] SceneVertices, RSGeometryData[] SceneGeometries, out RSBVHData[] Nodes, out int[] GeometryIDs)
        {
            GeometryIDs = new int[SceneGeometries.Length];
            if (SceneGeometries.Length == 0)
            {
                Nodes = Array.Empty<RSBVHData>();
                return;
            }

            var GeometryBounds = new GeometryBox[SceneGeometries.Length];
            for (int GeometryID = 0; GeometryID < SceneGeometries.Length; ++GeometryID)
            {
                var Geometry = SceneGeometries[GeometryID];
                Vector3 First = SceneVertices[Geometry.Indice0].Position;
                Vector3 Second = SceneVertices[Geometry.Indice1].Position;
                Vector3 Third = SceneVertices[Geometry.Indice2].Position;
                Vector3 Minimum = Vector3.Min(First, Vector3.Min(Second, Third));
                Vector3 Maximum = Vector3.Max(First, Vector3.Max(Second, Third));
                GeometryBounds[GeometryID] = new GeometryBox { Minimum = Minimum, Maximum = Maximum, Center = Minimum * 0.5f + Maximum * 0.5f };
                GeometryIDs[GeometryID] = GeometryID;
            }

            var Builder = new BVHBuilder(GeometryBounds, GeometryIDs);
            Nodes = Builder.Build();
        }

        private struct GeometryBox
        {
            public Vector3 Minimum;
            public Vector3 Maximum;
            public Vector3 Center;
        }

        private struct Bucket
        {
            public Vector3 Minimum;
            public Vector3 Maximum;
            public int Count;

            public void Add(Vector3 First, Vector3 Last, int GeometryCount)
            {
                if (GeometryCount == 0)
                    return;
                Minimum = Count == 0 ? First : Vector3.Min(Minimum, First);
                Maximum = Count == 0 ? Last : Vector3.Max(Maximum, Last);
                Count += GeometryCount;
            }

            public float Cost()
            {
                Vector3 Size = Maximum - Minimum;
                return Count * (Size.x * Size.y + Size.x * Size.z + Size.y * Size.z);
            }
        }

        private sealed class BVHBuilder
        {
            private readonly GeometryBox[] GeometryBounds;
            private readonly int[] GeometryIDs;
            private readonly List<RSBVHData> Nodes = new List<RSBVHData>();
            private readonly Bucket[] Buckets = new Bucket[BucketCount];
            private readonly float[] SplitCosts = new float[BucketCount - 1];
            private readonly int[] LeftCounts = new int[BucketCount - 1];

            public BVHBuilder(GeometryBox[] GeometryBounds, int[] GeometryIDs)
            {
                this.GeometryBounds = GeometryBounds;
                this.GeometryIDs = GeometryIDs;
            }

            public RSBVHData[] Build()
            {
                Nodes.Add(default);
                BuildNode(0, 0, GeometryIDs.Length, 0);
                return Nodes.ToArray();
            }

            private void BuildNode(int NodeID, int Start, int End, int Depth)
            {
                GeometryBox First = GeometryBounds[GeometryIDs[Start]];
                Vector3 Minimum = First.Minimum;
                Vector3 Maximum = First.Maximum;
                Vector3 CenterMinimum = First.Center;
                Vector3 CenterMaximum = First.Center;
                for (int Index = Start + 1; Index < End; ++Index)
                {
                    GeometryBox Geometry = GeometryBounds[GeometryIDs[Index]];
                    Minimum = Vector3.Min(Minimum, Geometry.Minimum);
                    Maximum = Vector3.Max(Maximum, Geometry.Maximum);
                    CenterMinimum = Vector3.Min(CenterMinimum, Geometry.Center);
                    CenterMaximum = Vector3.Max(CenterMaximum, Geometry.Center);
                }

                const float RelativePadding = 8.0f * 1.192092896e-7f;
                Vector3 Padding = new Vector3(Mathf.Max(1.0f, Mathf.Max(Mathf.Abs(Minimum.x), Mathf.Abs(Maximum.x))), Mathf.Max(1.0f, Mathf.Max(Mathf.Abs(Minimum.y), Mathf.Abs(Maximum.y))), Mathf.Max(1.0f, Mathf.Max(Mathf.Abs(Minimum.z), Mathf.Abs(Maximum.z)))) * RelativePadding;
                var Node = new RSBVHData { Minimum = Minimum - Padding, Maximum = Maximum + Padding, StartID = Start, GeometryCount = End - Start };
                if (Node.GeometryCount > LeafSize && Depth < MaximumDepth)
                {
                    int Middle = FindSplit(Start, End, CenterMinimum, CenterMaximum - CenterMinimum);
                    Node.StartID = Nodes.Count;
                    Node.GeometryCount = 0;
                    Nodes.Add(default);
                    Nodes.Add(default);
                    BuildNode(Node.StartID, Start, Middle, Depth + 1);
                    BuildNode(Node.StartID + 1, Middle, End, Depth + 1);
                }
                Nodes[NodeID] = Node;
            }

            private int FindSplit(int Start, int End, Vector3 CenterMinimum, Vector3 CenterSize)
            {
                int SplitAxis = -1;
                int SplitBucket = -1;
                float BestCost = float.PositiveInfinity;
                for (int Axis = 0; Axis < 3; ++Axis)
                {
                    float Extent = CenterSize[Axis];
                    if (Extent <= 0.0f)
                        continue;
                    Array.Clear(Buckets, 0, BucketCount);
                    for (int Index = Start; Index < End; ++Index)
                    {
                        GeometryBox Geometry = GeometryBounds[GeometryIDs[Index]];
                        int BucketID = GetBucket(Geometry.Center[Axis], CenterMinimum[Axis], Extent);
                        Buckets[BucketID].Add(Geometry.Minimum, Geometry.Maximum, 1);
                    }

                    Bucket Left = default;
                    for (int Index = 0; Index < BucketCount - 1; ++Index)
                    {
                        Bucket Current = Buckets[Index];
                        Left.Add(Current.Minimum, Current.Maximum, Current.Count);
                        LeftCounts[Index] = Left.Count;
                        SplitCosts[Index] = Left.Cost();
                    }

                    Bucket Right = default;
                    for (int Index = BucketCount - 2; Index >= 0; --Index)
                    {
                        Bucket Current = Buckets[Index + 1];
                        Right.Add(Current.Minimum, Current.Maximum, Current.Count);
                        float Cost = SplitCosts[Index] + Right.Cost();
                        if (LeftCounts[Index] == 0 || Right.Count == 0 || Cost >= BestCost)
                            continue;
                        BestCost = Cost;
                        SplitAxis = Axis;
                        SplitBucket = Index;
                    }
                }

                if (SplitAxis < 0)
                    return Start + (End - Start) / 2;

                int Middle = Start;
                for (int Index = Start; Index < End; ++Index)
                {
                    int GeometryID = GeometryIDs[Index];
                    int BucketID = GetBucket(GeometryBounds[GeometryID].Center[SplitAxis], CenterMinimum[SplitAxis], CenterSize[SplitAxis]);
                    if (BucketID > SplitBucket)
                        continue;
                    GeometryIDs[Index] = GeometryIDs[Middle];
                    GeometryIDs[Middle++] = GeometryID;
                }
                return Middle;
            }

            private static int GetBucket(float Coordinate, float Minimum, float Extent)
            {
                return Mathf.Min((int)((Coordinate - Minimum) / Extent * BucketCount), BucketCount - 1);
            }
        }
    }
}
