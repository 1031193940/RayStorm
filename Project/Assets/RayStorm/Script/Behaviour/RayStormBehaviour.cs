using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class RayStormBehaviour : ScriptableObject
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Abstract Functions |
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    public abstract void OnLoad(RSRenderContext Context);

    public abstract void OnCollectRenderTask(RSRenderContext Context);

    public abstract void OnUpdate(RSRenderContext Context);

    public abstract void OnPostProcess(RSRenderContext Context);

    public abstract void OnUnload(RSRenderContext Context);

    public virtual void OnInspectorGUI() { }

    public virtual void OnDrawGizmos() { }
}
