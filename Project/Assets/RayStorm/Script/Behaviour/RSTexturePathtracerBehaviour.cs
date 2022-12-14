using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

public class RSTexturePathtracerBehaviour : RayStormBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Implement Base Class Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public override void OnLoad(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnCollectRenderTask(RSRenderContext Context)
    {
        int TextureWidth = Context.MainCamera.pixelWidth;
        int TextureHeight = Context.MainCamera.pixelHeight;

        // Create render tasks and add to render task list
        for (int FrameID = 0; FrameID < 5; ++FrameID)
        {
            int RenderWindowSize;
            int SubpixelCountPerPixel;
            switch (FrameID)
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

            int RenderWindowCountRow = Mathf.CeilToInt(TextureHeight / (float)RenderWindowSize);
            int RenderWindowCountCol = Mathf.CeilToInt(TextureWidth  / (float)RenderWindowSize);
            for (int RowID = 0; RowID < RenderWindowCountRow; ++RowID)
            {
                for (int ColID = 0; ColID < RenderWindowCountCol; ++ColID)
                {
                    RSRenderTask NewRenderTask = RSRenderTask.Default;
                    NewRenderTask.FrameID = FrameID;
                    NewRenderTask.WindowPos = new Vector2Int(ColID, RowID) * RenderWindowSize;
                    NewRenderTask.WindowSize = Vector2Int.one * RenderWindowSize;
                    NewRenderTask.SubpixelCountPerPixel = SubpixelCountPerPixel;
                    Context.RenderTaskQueue.Enqueue(NewRenderTask);
                }
            }
        }
    }

    public override void OnPostProcess(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnUnload(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnUpdate(RSRenderContext Context)
    {
        // !!!TODO
    }
}
