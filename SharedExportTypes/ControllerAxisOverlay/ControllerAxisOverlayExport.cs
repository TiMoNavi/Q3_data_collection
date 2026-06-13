using System;
using System.Collections.Generic;

namespace Q3DataCollection.ExportTypes.ControllerAxisOverlay
{
    [Serializable]
    public sealed class ControllerAxisOverlayExport
    {
        public string schemaVersion = "controller-axis-overlay/v1";
        public string coordinateSystem = "unity-world-meters";
        public string cameraModel = "quest-pca-pinhole";
        public float axisLengthMeters;
        public ControllerAxisOverlaySource source = new ControllerAxisOverlaySource();
        public List<ControllerAxisOverlayFrame> frames = new List<ControllerAxisOverlayFrame>();
    }

    [Serializable]
    public sealed class ControllerAxisOverlaySource
    {
        public string sessionDir = string.Empty;
        public string metadataPath = string.Empty;
        public string videoHeadersPath = string.Empty;
        public long generatedAtUnixMs;
        public string toolName = string.Empty;
    }

    [Serializable]
    public sealed class ControllerAxisOverlayFrame
    {
        public long sourceCameraFrameId;
        public long encodedFrameId;
        public long sequenceId;
        public long timestampUnixMs;
        public string imagePath = string.Empty;
        public string overlayImagePath = string.Empty;
        public Size2i imageSize;
        public OverlayCameraFrame camera = new OverlayCameraFrame();
        public OverlayControllers controllers = new OverlayControllers();
    }

    [Serializable]
    public sealed class OverlayControllers
    {
        public OverlayController left = new OverlayController { hand = "left" };
        public OverlayController right = new OverlayController { hand = "right" };
    }

    [Serializable]
    public sealed class OverlayCameraFrame
    {
        public long frameId;
        public long timestampUnixMs;
        public Vector3Dto positionWorld;
        public QuaternionDto rotationWorld;
        public Size2i resolution;
        public Size2i sensorResolution;
        public Vector2Dto focalLength;
        public Vector2Dto principalPoint;
        public float[][] cameraLocalToWorldMatrix = EmptyMatrix();
        public float[][] cameraWorldToLocalMatrix = EmptyMatrix();

        private static float[][] EmptyMatrix()
        {
            return new[]
            {
                new[] { 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f }
            };
        }
    }

    [Serializable]
    public sealed class OverlayController
    {
        public string hand = string.Empty;
        public bool isValid;
        public string skipReason = string.Empty;
        public long timestampUnixMs;
        public Vector3Dto positionWorld;
        public QuaternionDto rotationWorld;
        public Vector2Dto pivotPixel;
        public AxisProjectionSet axes = new AxisProjectionSet();
    }

    [Serializable]
    public sealed class AxisProjectionSet
    {
        public AxisProjection x = new AxisProjection { colorRgb = new[] { 255, 48, 48 } };
        public AxisProjection y = new AxisProjection { colorRgb = new[] { 48, 220, 80 } };
        public AxisProjection z = new AxisProjection { colorRgb = new[] { 64, 128, 255 } };
    }

    [Serializable]
    public sealed class AxisProjection
    {
        public bool isVisible;
        public string skipReason = string.Empty;
        public Vector3Dto endpointWorld;
        public Vector2Dto startPixel;
        public Vector2Dto endPixel;
        public int[] colorRgb = new int[3];
    }

    [Serializable]
    public struct Vector2Dto
    {
        public float x;
        public float y;
    }

    [Serializable]
    public struct Vector3Dto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public struct QuaternionDto
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    public struct Size2i
    {
        public int width;
        public int height;
    }
}
