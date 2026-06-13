using UnityEngine;

namespace DataCapture.Synchronization
{
    public static class VirtualLayerProjectionUtility
    {
        public static void CopyCameraPoseAndProjection(Camera source, Camera target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            target.projectionMatrix = source.projectionMatrix;
            target.fieldOfView = source.fieldOfView;
            target.aspect = source.aspect;
            target.nearClipPlane = source.nearClipPlane;
            target.farClipPlane = source.farClipPlane;
        }
    }
}
