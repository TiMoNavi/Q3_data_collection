using Meta.XR;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public static class PassthroughProjectionUtility
    {
        public static Matrix4x4 BuildProjectionMatrix(
            PassthroughCameraAccess.CameraIntrinsics intrinsics,
            Vector2Int currentResolution,
            float near,
            float far)
        {
            near = Mathf.Max(0.001f, near);
            far = Mathf.Max(near + 0.001f, far);

            Rect crop = CalculateSensorCropRegion(intrinsics.SensorResolution, currentResolution);
            Vector2 focalLength = intrinsics.FocalLength;
            Vector2 principalPoint = intrinsics.PrincipalPoint;

            float left = (crop.xMin - principalPoint.x) / focalLength.x * near;
            float right = (crop.xMax - principalPoint.x) / focalLength.x * near;
            float bottom = (crop.yMin - principalPoint.y) / focalLength.y * near;
            float top = (crop.yMax - principalPoint.y) / focalLength.y * near;

            return Matrix4x4.Frustum(left, right, bottom, top, near, far);
        }

        public static Rect CalculateSensorCropRegion(Vector2Int sensorResolution, Vector2Int currentResolution)
        {
            Vector2 sensor = sensorResolution;
            Vector2 current = currentResolution;
            Vector2 scaleFactor = current / sensor;
            scaleFactor /= Mathf.Max(scaleFactor.x, scaleFactor.y);

            return new Rect(
                sensor.x * (1f - scaleFactor.x) * 0.5f,
                sensor.y * (1f - scaleFactor.y) * 0.5f,
                sensor.x * scaleFactor.x,
                sensor.y * scaleFactor.y);
        }
    }
}
