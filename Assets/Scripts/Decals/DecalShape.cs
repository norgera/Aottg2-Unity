using UnityEngine;

namespace Decals
{
    public enum DecalGeometry
    {
        CubeProjector,
        Quad
    }

    class DecalShape : MonoBehaviour
    {
        public DecalGeometry Geometry;
        public float BaseSize;
    }
}


