using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public class RSRenderContext
    {
        public Camera MainCamera = null;
        public RSSceneData SceneData;
        public RenderTexture RaytraceTexture = null;

        public int FrameID = 0;
        public bool IsRunning = true;
        public Queue<RSRenderTask> RenderTaskQueue = null;
    }
}