using UnityEngine;

namespace Decals
{
    public class DecalProjector : MonoBehaviour
    {
        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        public void Apply(MeshRenderer meshRenderer)
        {
            _meshRenderer = meshRenderer;
            UpdateProjectorMatrix();
        }

        private void Update()
        {
            // Update the projector matrix each frame to handle transform changes
            if (_meshRenderer != null)
            {
                UpdateProjectorMatrix();
            }
        }

        private void UpdateProjectorMatrix()
        {
            if (_meshRenderer == null) return;

            // Calculate the inverse transform matrix for the decal projection
            // This is used by the decal shaders to transform world positions into local decal space
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            
            // Get current property block to preserve other properties
            _meshRenderer.GetPropertyBlock(_propertyBlock);
            
            // Set the inverse transform matrix for the decal shader
            _propertyBlock.SetMatrix("_InverseTransformMatrix", worldToLocal);
            
            // Apply the property block back to the renderer
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void OnValidate()
        {
            // Update matrix when values change in editor
            if (_meshRenderer != null)
            {
                UpdateProjectorMatrix();
            }
        }
    }
}
