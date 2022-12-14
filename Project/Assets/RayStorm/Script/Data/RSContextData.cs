using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RSRenderContext
{
    public bool ContainsTriangleMesh;
    public bool ContainsCustomizeObject;
    public bool ContainsLightSource;
    public bool ContainsSkybox;

    public int FrameID = 0;
    public Camera MainCamera = null;
    public Queue<RSRenderTask> RenderTaskQueue = null;
    public RenderTexture RaytraceTexture = null;
}
