using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RSDefaultBehaviour : RayStormBehaviour
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
        RSRenderTask RenderTask = RSRenderTask.Default;
        RenderTask.FrameID = 0;
        RenderTask.WindowPos = new Vector2Int(0, 0);
        RenderTask.WindowSize = new Vector2Int(Context.MainCamera.pixelWidth, Context.MainCamera.pixelHeight);
        RenderTask.SubpixelCountPerPixel = 1;
        Context.RenderTaskQueue.Enqueue(RenderTask);
    }

    public override void OnUpdate(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnPostProcess(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnUnload(RSRenderContext Context)
    {
        // !!!TODO
    }

    public override void OnInspectorGUI() 
    {
        // !!!TODO
    }

    public override void OnDrawGizmos() 
    {
        // !!!TODO
    }
}
