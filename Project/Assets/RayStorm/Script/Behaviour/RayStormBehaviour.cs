using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
            // !!!TODO : Override in child class
        }

        public virtual void OnStartup(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnFinished(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnUnload(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnFrameStartup(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneObject[] OutputSceneObject)
        {
            OutputSceneObject = RSSceneUtility.CollectSceneObject(Context.MainCamera);
        }

        public virtual void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            int TextureWidth = Context.RenderWidth;
            int TextureHeight = Context.RenderHeight;
            if (TextureWidth <= 0 || TextureHeight <= 0)
                throw new System.InvalidOperationException("The render dimensions must be positive before collecting tasks.");

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
            int RenderWindowCountRow = Mathf.CeilToInt(TextureHeight / (float)RenderWindowSize);
            int RenderWindowCountCol = Mathf.CeilToInt(TextureWidth / (float)RenderWindowSize);
            for (int RowID = 0; RowID < RenderWindowCountRow; ++RowID)
            {
                for (int ColID = 0; ColID < RenderWindowCountCol; ++ColID)
                {
                    RSRenderTask NewRenderTask = RSRenderTask.Default;
                    NewRenderTask.FrameID = Context.FrameID;
                    NewRenderTask.WindowPos = new Vector2Int(ColID, RowID) * RenderWindowSize;
                    NewRenderTask.WindowSize = new Vector2Int(
                        Mathf.Min(RenderWindowSize, TextureWidth - NewRenderTask.WindowPos.x),
                        Mathf.Min(RenderWindowSize, TextureHeight - NewRenderTask.WindowPos.y));
                    NewRenderTask.SubpixelCountPerPixel = SubpixelCountPerPixel;
                    RenderTaskList.Add(NewRenderTask);
                }
            }

            OutputRenderTask = RenderTaskList.ToArray();
        }

        public virtual void OnFrameUpdate(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnFramePostProcess(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnFrameFinished(RSRenderContext Context)
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnInspectorGUI()
        {
            // !!!TODO : Override in child class
        }

        public virtual void OnDrawGizmos()
        {
            // !!!TODO : Override in child class
        }
    }
}
