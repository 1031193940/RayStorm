using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RayStorm;

#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

namespace RayStorm
{
    public class RSTexturePathtracerBehaviour : RayStormBehaviour
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// Override Base Class Functions |
        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public override void OnFrameStartup(RSRenderContext Context)
        {
            // !!!TODO
        }

        public override void OnFrameCollectSceneObject(RSRenderContext Context, out RSSceneObject[] OutputSceneObject)
        {
            base.OnFrameCollectSceneObject(Context, out OutputSceneObject);
        }

        public override void OnFrameCollectRenderTask(RSRenderContext Context, out RSRenderTask[] OutputRenderTask)
        {
            int TextureWidth = Context.RenderWidth;
            int TextureHeight = Context.RenderHeight;
            if (TextureWidth <= 0 || TextureHeight <= 0)
                throw new System.InvalidOperationException("The render dimensions must be positive before collecting tasks.");

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
            }

            OutputRenderTask = RenderTaskList.ToArray();
        }

        public override void OnFrameUpdate(RSRenderContext Context)
        {
            // !!!TODO
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

            //Context.MainCamera.transform.position = Context.MainCamera.transform.position + new Vector3(1, 0, 0);
            //Context.MainCamera.transform.LookAt(new Vector3(0, 0.5f, 0), Vector3.up);

            // !!!TODO
            Debug.Log("FrameFinished : " + Context.FrameID);
        }
    }
}
