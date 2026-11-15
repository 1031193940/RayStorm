using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public class RSRenderTask
    {
        public int FrameID = 0;
        public int SubpixelCountPerPixel = 1;
        public Vector2Int WindowPos = Vector2Int.zero;
        public Vector2Int WindowSize = Vector2Int.one;
        public bool RefreshPreviewAfterTask = false;

        public static RSRenderTask Default
        {
            get
            {
                return new RSRenderTask();
            }
        }
    }
}
