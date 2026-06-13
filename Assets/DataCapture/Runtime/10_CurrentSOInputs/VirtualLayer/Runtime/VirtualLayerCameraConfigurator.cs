using UnityEngine;

namespace DataCapture.Synchronization
{
    public class VirtualLayerCameraConfigurator : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private LayerMask capturedLayers;
        [SerializeField] private RenderTexture targetTexture;
        [SerializeField] private Color clearColor = Color.clear;
        [SerializeField] private float nearClipPlane = 0.02f;
        [SerializeField] private float farClipPlane = 100f;

        [ContextMenu("Apply Configuration")]
        public void ApplyConfiguration()
        {
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.enabled = false;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = clearColor;
            targetCamera.cullingMask = capturedLayers;
            targetCamera.nearClipPlane = Mathf.Max(0.001f, nearClipPlane);
            targetCamera.farClipPlane = Mathf.Max(targetCamera.nearClipPlane + 0.001f, farClipPlane);
            targetCamera.targetTexture = targetTexture;
            targetCamera.stereoTargetEye = StereoTargetEyeMask.None;
        }
    }
}
