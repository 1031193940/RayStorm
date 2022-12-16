using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RSDefaultBehaviour : RayStormBehaviour
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Override Base Class Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public override void OnStartup(RSRenderContext Context)
    {
        FrameCount = 1;
    }
}
