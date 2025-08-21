using UnityEngine;
using ApplicationManagers;

namespace Decals
{
    class DecalCulling : MonoBehaviour
    {
        private float _renderDistance;
        private Renderer _renderer;

        public void Init(float renderDistance)
        {
            _renderDistance = Mathf.Max(0f, renderDistance);
            _renderer = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            if (_renderer == null || SceneLoader.CurrentCamera == null)
                return;
            var cam = SceneLoader.CurrentCamera.Cache.Transform;
            float dist = Vector3.Distance(cam.position, transform.position);
            bool visible = _renderDistance <= 0f || dist <= _renderDistance;
            if (_renderer.enabled != visible)
                _renderer.enabled = visible;
        }
    }
}


