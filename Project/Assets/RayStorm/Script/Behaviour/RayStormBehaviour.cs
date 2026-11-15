using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public abstract class RayStormBehaviour : ScriptableObject
    {
        [Min(0)] public int FrameCount = 1; // 0 means no frame count limit

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// Virtual Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public virtual void OnLoad(RSRenderContext Context)
        {
        }

        public virtual void OnStartup(RSRenderContext Context)
        {
        }

        public virtual void OnFinished(RSRenderContext Context)
        {
        }

        public virtual void OnUnload(RSRenderContext Context)
        {
        }

        public virtual void OnFrameStartup(RSRenderContext Context)
        {
        }

        public virtual void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneObject[] OutputSceneObject)
        {
            OutputSceneObject = RSSceneUtility.CollectSceneObject(Context.MainCamera);
        }

        public virtual void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            int RenderWindowSize;
            int SubpixelCountPerPixel;
            switch (Context.FrameID)
            {
                case 0:
                    RenderWindowSize = 128;
                    SubpixelCountPerPixel = 1;
                    break;
                case 1:
                    RenderWindowSize = 128;
                    SubpixelCountPerPixel = 4;
                    break;
                default:
                    RenderWindowSize = 32;
                    SubpixelCountPerPixel = 64;
                    break;
            }

            List<RSRenderTask> RenderTaskList = new List<RSRenderTask>();
            AddRenderTasks(Context, RenderTaskList, RenderWindowSize, SubpixelCountPerPixel);
            OutputRenderTask = RenderTaskList.ToArray();
        }

        public virtual void OnFrameUpdate(RSRenderContext Context)
        {
        }

        public virtual void OnFramePostProcess(RSRenderContext Context)
        {
        }

        public virtual void OnFrameFinished(RSRenderContext Context)
        {
        }

        public virtual void OnInspectorGUI()
        {
        }

        public virtual void OnDrawGizmos()
        {
        }

        protected static void AddRenderTasks(RSRenderContext Context, List<RSRenderTask> RenderTaskList,
            int RenderWindowSize, int SubpixelCountPerPixel)
        {
            int PassStart = RenderTaskList.Count;
            for (int WindowY = 0; WindowY < Context.RenderHeight; WindowY += RenderWindowSize)
            {
                for (int WindowX = 0; WindowX < Context.RenderWidth; WindowX += RenderWindowSize)
                {
                    RenderTaskList.Add(new RSRenderTask
                    {
                        FrameID = Context.FrameID,
                        WindowPos = new Vector2Int(WindowX, WindowY),
                        WindowSize = new Vector2Int(
                            Mathf.Min(RenderWindowSize, Context.RenderWidth - WindowX),
                            Mathf.Min(RenderWindowSize, Context.RenderHeight - WindowY)),
                        SubpixelCountPerPixel = SubpixelCountPerPixel
                    });
                }
            }

            // Sort only the new pass so the whole image is sampled before the next pass starts.
            var CenterFirst = Comparer<RSRenderTask>.Create((First, Second) =>
            {
                int Order = GetCenterDistanceSquared(First, Context).CompareTo(GetCenterDistanceSquared(Second, Context));
                if (Order != 0)
                    return Order;
                Order = First.WindowPos.y.CompareTo(Second.WindowPos.y);
                return Order != 0 ? Order : First.WindowPos.x.CompareTo(Second.WindowPos.x);
            });
            RenderTaskList.Sort(PassStart, RenderTaskList.Count - PassStart, CenterFirst);
        }

        private static long GetCenterDistanceSquared(RSRenderTask Task, RSRenderContext Context)
        {
            // Double the coordinates to keep half-pixel centers exact, including clipped tiles.
            long X = 2L * Task.WindowPos.x + Task.WindowSize.x - Context.RenderWidth;
            long Y = 2L * Task.WindowPos.y + Task.WindowSize.y - Context.RenderHeight;
            return X * X + Y * Y;
        }
    }
}
