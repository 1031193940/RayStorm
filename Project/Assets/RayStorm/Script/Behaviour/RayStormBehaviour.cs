using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RayStorm
{
    public abstract class RayStormBehaviour : ScriptableObject
    {
        public int FrameCount = 0; // 0 means no frame count limit

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
            OutputSceneObject = RSSceneUtility.CollectSceneObject();
        }

        public virtual RSRenderTask[] OnFrameCollectRenderTask(RSRenderContext Context)
        {
            int TextureWidth = Context.MainCamera.pixelWidth;
            int TextureHeight = Context.MainCamera.pixelHeight;

            int RenderWindowSize;
            int SubpixelCountPerPixel;
            switch (Context.FrameID)
            {
                case 0:
                    RenderWindowSize = Mathf.Max(TextureWidth, TextureHeight);
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
                    NewRenderTask.WindowSize = Vector2Int.one * RenderWindowSize;
                    NewRenderTask.SubpixelCountPerPixel = SubpixelCountPerPixel;
                    RenderTaskList.Add(NewRenderTask);
                }
            }

            return RenderTaskList.ToArray();
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