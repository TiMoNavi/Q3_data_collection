using Meta.XR;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public static class PassthroughIntrinsicsUtility
    {
        public static bool IsValid(PassthroughCameraAccess.CameraIntrinsics intrinsics)
        {
            return intrinsics.FocalLength.x > 0f &&
                   intrinsics.FocalLength.y > 0f &&
                   intrinsics.SensorResolution.x > 0 &&
                   intrinsics.SensorResolution.y > 0;
        }

        public static Vector2 ScalePrincipalPoint(Vector2 principalPoint, Vector2Int fromResolution, Vector2Int toResolution)
        {
            if (fromResolution.x <= 0 || fromResolution.y <= 0)
            {
                return principalPoint;
            }

            return new Vector2(
                principalPoint.x * toResolution.x / fromResolution.x,
                principalPoint.y * toResolution.y / fromResolution.y);
        }
    }
}
