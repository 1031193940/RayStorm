using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public class RSRenderContext
    {
        public Camera MainCamera = null;
        public RenderTexture RaytraceTexture = null;
        public int RenderWidth = 0;
        public int RenderHeight = 0;

        public int FrameID = 0;
        public bool IsRunning = true;
        public Queue<RSRenderTask> RenderTaskQueue = null;
    }
}
