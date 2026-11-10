using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RayStorm
{
    public enum ESceneObjectType
    {
        Unknown = 0,
        StaticMesh,
        SkeletalMesh,
    }

    public class RSSceneObject
    {
        public ESceneObjectType Type = ESceneObjectType.Unknown;
        public Mesh Mesh = null;
        public Renderer Renderer = null;
        // Only baked/cloned meshes belong to the render snapshot; imported assets do not.
        public bool OwnsMesh = false;

        public static RSSceneObject Default
        {
            get { return new RSSceneObject(); }
        }
    }
}
