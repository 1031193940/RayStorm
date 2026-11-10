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

                int[] ExpectedSamples = { 1, 4, 64 };
                for (int FrameIndex = 0; FrameIndex < ExpectedSamples.Length; ++FrameIndex)
                {
                    Context.FrameID = FrameIndex;
                    ValidateCoverage(DefaultBehaviour, Context, 1, ExpectedSamples[FrameIndex]);
                }

                Context.FrameID = 0;
                ValidateCoverage(TextureBehaviour, Context, 5, 816);
                Context.RenderWidth = 1;
                Context.RenderHeight = 1;
                ValidateCoverage(DefaultBehaviour, Context, 1, 1);
                ValidateCoverage(TextureBehaviour, Context, 5, 816);

                Debug.Log("RayStorm task validation passed.");
            }
            finally
            {
                Object.DestroyImmediate(DefaultBehaviour);
                Object.DestroyImmediate(TextureBehaviour);
            }
        }

        private static void ValidateCoverage(RayStormBehaviour Behaviour, RSRenderContext Context, int ExpectedVisits, int ExpectedSamples)
        {
            Behaviour.OnFrameCollectRenderTask(Context, out RSRenderTask[] Tasks);
            int[] PixelVisits = new int[Context.RenderWidth * Context.RenderHeight];
            int[] PixelSamples = new int[PixelVisits.Length];
            foreach (RSRenderTask Task in Tasks)
            {
                Require(Task.FrameID == Context.FrameID, "Task frame ID must match the render snapshot.");
                Require(Task.WindowSize.x > 0 && Task.WindowSize.y > 0 && Task.SubpixelCountPerPixel > 0, "Tasks must contain positive dimensions and samples.");
                Require(Task.WindowSize.x <= 128 && Task.WindowSize.y <= 128, "Preview tasks must be bounded, including the first output frame.");
                Require(Task.WindowPos.x >= 0 && Task.WindowPos.y >= 0 &&
                    Task.WindowPos.x + Task.WindowSize.x <= Context.RenderWidth &&
                    Task.WindowPos.y + Task.WindowSize.y <= Context.RenderHeight, "A task extends beyond the output texture.");

                for (int PixelY = Task.WindowPos.y; PixelY < Task.WindowPos.y + Task.WindowSize.y; ++PixelY)
                {
                    for (int PixelX = Task.WindowPos.x; PixelX < Task.WindowPos.x + Task.WindowSize.x; ++PixelX)
                    {
                        int PixelIndex = PixelY * Context.RenderWidth + PixelX;
                        PixelVisits[PixelIndex]++;
                        PixelSamples[PixelIndex] += Task.SubpixelCountPerPixel;
                    }
                }
            }

            for (int PixelIndex = 0; PixelIndex < PixelVisits.Length; ++PixelIndex)
            {
                Require(PixelVisits[PixelIndex] == ExpectedVisits, "Pixel coverage has a gap or overlap within a sampling pass.");
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
