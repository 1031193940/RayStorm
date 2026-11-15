using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public class RSTexturePathtracerBehaviour : RayStormBehaviour
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// Override Base Class Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public override void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            List<RSRenderTask> RenderTaskList = new List<RSRenderTask>();
            AddRenderTasks(Context, RenderTaskList, 128, 1);
            if (RenderTaskList.Count > 0)
                RenderTaskList[RenderTaskList.Count - 1].RefreshPreviewAfterTask = true;
            AddRenderTasks(Context, RenderTaskList, 64, 15);
            AddRenderTasks(Context, RenderTaskList, 64, 32);
            for (int Pass = 0; Pass < 3; ++Pass)
                AddRenderTasks(Context, RenderTaskList, 16, 256);

            OutputRenderTask = RenderTaskList.ToArray();
        }

        public override void OnFrameFinished(RSRenderContext Context)
        {
            Texture2D RaytraceTexture2D = RSDataUtility.RenderTextureToTexture2D(Context.OutputTexture, TextureFormat.ARGB32);
            try
            {
                RSAssetUtility.SaveTexture2D(RaytraceTexture2D, RSAssetUtility.GetOutputPath("RaytraceOutput" + Context.FrameID + ".jpg"));
            }
            finally
            {
                Destroy(RaytraceTexture2D);
            }

            Debug.Log("FrameFinished : " + Context.FrameID);
        }
    }
}
