using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RayStorm
{
    public static class RayStormRuntimeValidation
    {
        [MenuItem("RayStorm/Validate Task Scheduling")]
        public static void Run()
        {
            RSDefaultBehaviour DefaultBehaviour = ScriptableObject.CreateInstance<RSDefaultBehaviour>();
            RSTexturePathtracerBehaviour TextureBehaviour = ScriptableObject.CreateInstance<RSTexturePathtracerBehaviour>();
            try
            {
                Require(DefaultBehaviour.FrameCount == 1 && TextureBehaviour.FrameCount == 1, "New behaviours must default to one output frame.");
                DefaultBehaviour.FrameCount = 3;
                TextureBehaviour.FrameCount = 0;
                RSRenderContext Context = new RSRenderContext { RenderWidth = 129, RenderHeight = 67 };
                DefaultBehaviour.OnStartup(Context);
                TextureBehaviour.OnStartup(Context);
                Require(DefaultBehaviour.FrameCount == 3 && TextureBehaviour.FrameCount == 0, "Startup must preserve configured frame limits.");

                (int TileSize, int Samples)[] DefaultPasses = { (128, 1), (128, 4), (32, 64) };
                for (int FrameIndex = 0; FrameIndex < DefaultPasses.Length; ++FrameIndex)
                {
                    Context.FrameID = FrameIndex;
                    ValidateSchedule(DefaultBehaviour, Context, DefaultPasses[FrameIndex].Samples, false, DefaultPasses[FrameIndex]);
                }

                Context.FrameID = 0;
                Context.RenderWidth = 1;
                Context.RenderHeight = 1;
                ValidateSchedule(DefaultBehaviour, Context, 1, false, (128, 1));
                foreach (Vector2Int Size in new[] { Vector2Int.one, new Vector2Int(256, 256), new Vector2Int(257, 129), new Vector2Int(1920, 1080) })
                {
                    Context.RenderWidth = Size.x;
                    Context.RenderHeight = Size.y;
                    ValidateSchedule(TextureBehaviour, Context, 816, true,
                        (128, 1), (64, 15), (64, 32), (16, 256), (16, 256), (16, 256));
                }

                Debug.Log("RayStorm task validation passed: center-first order, edge coverage, first-pass preview and 816 spp.");
            }
            finally
            {
                Object.DestroyImmediate(DefaultBehaviour);
                Object.DestroyImmediate(TextureBehaviour);
            }
        }

        private static void ValidateSchedule(RayStormBehaviour Behaviour, RSRenderContext Context, int ExpectedSamples,
            bool RefreshAfterFirstPass, params (int TileSize, int Samples)[] Passes)
        {
            Behaviour.OnFrameCollectRenderTask(Context, out RSRenderTask[] Tasks);
            int[] PixelVisits = new int[Context.RenderWidth * Context.RenderHeight];
            int[] PixelSamples = new int[PixelVisits.Length];
            Vector2 ImageCenter = new Vector2(Context.RenderWidth, Context.RenderHeight) * 0.5f;
            int TaskIndex = 0;
            for (int Pass = 0; Pass < Passes.Length; ++Pass)
            {
                (int TileSize, int Samples) = Passes[Pass];
                int TilesX = (Context.RenderWidth - 1) / TileSize + 1;
                int TilesY = (Context.RenderHeight - 1) / TileSize + 1;
                int PassEnd = TaskIndex + TilesX * TilesY;
                Require(PassEnd <= Tasks.Length, "The schedule is missing tiles for a sampling pass.");
                float PreviousDistance = -1.0f;
                Vector2Int PreviousPosition = Vector2Int.zero;
                for (; TaskIndex < PassEnd; ++TaskIndex)
                {
                    RSRenderTask Task = Tasks[TaskIndex];
                    Require(Task.FrameID == Context.FrameID, "Task frame ID must match the render snapshot.");
                    Require(Task.SubpixelCountPerPixel == Samples, "Unexpected sample count or sampling pass order.");
                    Require(Task.RefreshPreviewAfterTask == (RefreshAfterFirstPass && Pass == 0 && TaskIndex == PassEnd - 1),
                        "Only the last tile of the first preview pass may request an immediate refresh.");
                    Require(Task.WindowPos.x >= 0 && Task.WindowPos.x < Context.RenderWidth && Task.WindowPos.x % TileSize == 0 &&
                        Task.WindowPos.y >= 0 && Task.WindowPos.y < Context.RenderHeight && Task.WindowPos.y % TileSize == 0,
                        "Tile origin must lie on the pass grid within the output texture.");
                    Require(Task.WindowSize.x == Mathf.Min(TileSize, Context.RenderWidth - Task.WindowPos.x) &&
                        Task.WindowSize.y == Mathf.Min(TileSize, Context.RenderHeight - Task.WindowPos.y),
                        "Tile size must match the pass and be clipped to the output bounds.");

                    Vector2 TileCenter = (Vector2)Task.WindowPos + (Vector2)Task.WindowSize * 0.5f;
                    float Distance = (TileCenter - ImageCenter).sqrMagnitude;
                    Require(Distance > PreviousDistance || (Distance == PreviousDistance &&
                        (Task.WindowPos.y > PreviousPosition.y || (Task.WindowPos.y == PreviousPosition.y && Task.WindowPos.x > PreviousPosition.x))),
                        "Tiles must be center-first with ties ordered by Y then X.");
                    PreviousDistance = Distance;
                    PreviousPosition = Task.WindowPos;

                    for (int PixelY = Task.WindowPos.y; PixelY < Task.WindowPos.y + Task.WindowSize.y; ++PixelY)
                    {
                        for (int PixelX = Task.WindowPos.x; PixelX < Task.WindowPos.x + Task.WindowSize.x; ++PixelX)
                        {
                            int PixelIndex = PixelY * Context.RenderWidth + PixelX;
                            Require(PixelVisits[PixelIndex] == Pass, "Pixel coverage has a gap or overlap within a sampling pass.");
                            PixelVisits[PixelIndex]++;
                            PixelSamples[PixelIndex] += Task.SubpixelCountPerPixel;
                        }
                    }
                }
            }

            Require(TaskIndex == Tasks.Length, "The schedule has extra tiles after its final pass.");
            for (int PixelIndex = 0; PixelIndex < PixelVisits.Length; ++PixelIndex)
            {
                Require(PixelVisits[PixelIndex] == Passes.Length, "Pixel coverage has a gap or overlap within a sampling pass.");
                Require(PixelSamples[PixelIndex] == ExpectedSamples, "Pixel sample totals differ from the configured passes.");
            }
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
