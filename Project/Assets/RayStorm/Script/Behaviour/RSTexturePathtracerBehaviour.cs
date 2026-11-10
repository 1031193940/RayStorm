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
            for (int IterID = 0; IterID < 5; ++IterID)
            {
                int RenderWindowSize;
                int SubpixelCountPerPixel;
                if (IterID == 0)
                {
                    RenderWindowSize = 64;
                    SubpixelCountPerPixel = 16;
                }
                else if (IterID == 1)
                {
                    RenderWindowSize = 64;
                    SubpixelCountPerPixel = 32;
                }
                else
                {
                    RenderWindowSize = 16;
                    SubpixelCountPerPixel = 256;
                }

                AddRenderTasks(Context, RenderTaskList, RenderWindowSize, SubpixelCountPerPixel);
            }

            OutputRenderTask = RenderTaskList.ToArray();
        }

        public override void OnFrameFinished(RSRenderContext Context)
        {
            Texture2D RaytraceTexture2D = RSDataUtility.RenderTextureToTexture2D(Context.RaytraceTexture, TextureFormat.ARGB32);
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
