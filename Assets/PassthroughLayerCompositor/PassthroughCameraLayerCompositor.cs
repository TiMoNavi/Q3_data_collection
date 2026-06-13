using System;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DataCapture.Compositing
{
    /// <summary>
    /// Composites the Quest passthrough camera frame with a Unity layer in an off-screen render path.
    /// The passthrough camera frame is the master clock: rendering happens only when PCA publishes a new frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PassthroughCameraLayerCompositor : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;
        [SerializeField] private LayerMask compositedLayers;

        [Header("Off-screen Camera")]
        [SerializeField] private Camera layerCamera;
        [SerializeField] private float nearClipPlane = 0.02f;
        [SerializeField] private float farClipPlane = 100f;
        [SerializeField] private bool renderOnlyWhenPassthroughUpdates = true;

        [Header("Composite")]
        [SerializeField] private Shader compositeShader;
        [SerializeField] private RenderTexture overlayRenderTexture;
        [SerializeField] private RenderTexture compositeRenderTexture;

        private Material compositeMaterial;
        private bool ownsLayerCamera;
        private bool ownsOverlayRenderTexture;
        private bool ownsCompositeRenderTexture;
        private Vector2Int allocatedResolution;

        public event Action<CompositeFrame> FrameComposited;

        public RenderTexture OverlayRenderTexture => overlayRenderTexture;
        public RenderTexture CompositeRenderTexture => compositeRenderTexture;
        public DateTime LatestTimestamp { get; private set; }
        public long LatestTimestampUnixMilliseconds { get; private set; }
        public Pose LatestCameraPose { get; private set; }
        public Vector2Int LatestResolution { get; private set; }

        private void Awake()
        {
            if (passthroughCameraAccess == null)
            {
                passthroughCameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            }

            EnsureLayerCamera();
            EnsureCompositeMaterial();
        }

        private void Update()
        {
            if (passthroughCameraAccess == null || !passthroughCameraAccess.IsPlaying)
            {
                return;
            }

            if (renderOnlyWhenPassthroughUpdates && !passthroughCameraAccess.IsUpdatedThisFrame)
            {
                return;
            }

            RenderCompositeForCurrentPassthroughFrame();
        }

        public bool TryGetLatestComposite(out CompositeFrame frame)
        {
            if (compositeRenderTexture == null || LatestTimestamp == default)
            {
                frame = default;
                return false;
            }

            frame = BuildFrame(null);
            return true;
        }

        public bool RenderCompositeForCurrentPassthroughFrame()
        {
            var passthroughTexture = passthroughCameraAccess.GetTexture();
            if (passthroughTexture == null)
            {
                return false;
            }

            var resolution = passthroughCameraAccess.CurrentResolution;
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                resolution = new Vector2Int(passthroughTexture.width, passthroughTexture.height);
            }

            EnsureRenderTextures(resolution);
            EnsureLayerCamera();
            EnsureCompositeMaterial();
            if (compositeMaterial == null)
            {
                return false;
            }

            LatestTimestamp = passthroughCameraAccess.Timestamp;
            LatestTimestampUnixMilliseconds = new DateTimeOffset(LatestTimestamp).ToUnixTimeMilliseconds();
            LatestCameraPose = passthroughCameraAccess.GetCameraPose();
            LatestResolution = resolution;

            ConfigureLayerCameraForPassthroughFrame(resolution, LatestCameraPose);
            layerCamera.Render();

            compositeMaterial.SetTexture("_OverlayTex", overlayRenderTexture);
            Graphics.Blit(passthroughTexture, compositeRenderTexture, compositeMaterial);

            FrameComposited?.Invoke(BuildFrame(passthroughTexture));
            return true;
        }

        private CompositeFrame BuildFrame(Texture passthroughTexture)
        {
            return new CompositeFrame
            {
                passthroughTexture = passthroughTexture,
                overlayTexture = overlayRenderTexture,
                compositeTexture = compositeRenderTexture,
                timestamp = LatestTimestamp,
                timestampUnixMilliseconds = LatestTimestampUnixMilliseconds,
                cameraPose = LatestCameraPose,
                resolution = LatestResolution,
                unityFrame = Time.frameCount
            };
        }

        private void EnsureLayerCamera()
        {
            if (layerCamera != null)
            {
                layerCamera.enabled = false;
                return;
            }

            var cameraObject = new GameObject("Passthrough Composite Layer Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.hideFlags = HideFlags.DontSave;

            layerCamera = cameraObject.AddComponent<Camera>();
            layerCamera.enabled = false;
            ownsLayerCamera = true;
        }

        private void EnsureCompositeMaterial()
        {
            if (compositeMaterial != null)
            {
                return;
            }

            if (compositeShader == null)
            {
                compositeShader = Shader.Find("Hidden/PassthroughLayerCompositor/AlphaComposite");
            }

            if (compositeShader == null)
            {
                Debug.LogError("Composite shader not found. Assign Hidden/PassthroughLayerCompositor/AlphaComposite.", this);
                return;
            }

            compositeMaterial = new Material(compositeShader)
            {
                hideFlags = HideFlags.DontSave
            };
        }

        private void EnsureRenderTextures(Vector2Int resolution)
        {
            if (allocatedResolution == resolution && overlayRenderTexture != null && compositeRenderTexture != null)
            {
                return;
            }

            ReleaseOwnedRenderTextures();

            overlayRenderTexture = CreateRenderTexture(resolution, "PCA Layer Overlay");
            compositeRenderTexture = CreateRenderTexture(resolution, "PCA Layer Composite");
            ownsOverlayRenderTexture = true;
            ownsCompositeRenderTexture = true;
            allocatedResolution = resolution;
        }

        private static RenderTexture CreateRenderTexture(Vector2Int resolution, string name)
        {
            var texture = new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32)
            {
                name = name,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private void ConfigureLayerCameraForPassthroughFrame(Vector2Int resolution, Pose cameraPose)
        {
            layerCamera.enabled = false;
            var additionalCameraData = layerCamera.GetComponent<UniversalAdditionalCameraData>();
            if (additionalCameraData != null)
            {
                additionalCameraData.allowXRRendering = false;
            }
            else if (GraphicsSettings.currentRenderPipeline == null)
            {
                layerCamera.stereoTargetEye = StereoTargetEyeMask.None;
            }

            layerCamera.clearFlags = CameraClearFlags.SolidColor;
            layerCamera.backgroundColor = Color.clear;
            layerCamera.cullingMask = compositedLayers;
            layerCamera.nearClipPlane = Mathf.Max(0.001f, nearClipPlane);
            layerCamera.farClipPlane = Mathf.Max(layerCamera.nearClipPlane + 0.001f, farClipPlane);
            layerCamera.aspect = resolution.x / (float)resolution.y;
            layerCamera.targetTexture = overlayRenderTexture;
            layerCamera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
            layerCamera.projectionMatrix = BuildProjectionMatrixFromPassthroughIntrinsics(
                passthroughCameraAccess.Intrinsics,
                resolution,
                layerCamera.nearClipPlane,
                layerCamera.farClipPlane);
        }

        private static Matrix4x4 BuildProjectionMatrixFromPassthroughIntrinsics(
            PassthroughCameraAccess.CameraIntrinsics intrinsics,
            Vector2Int currentResolution,
            float near,
            float far)
        {
            var crop = CalculateSensorCropRegion(intrinsics.SensorResolution, currentResolution);
            var focalLength = intrinsics.FocalLength;
            var principalPoint = intrinsics.PrincipalPoint;

            var left = (crop.xMin - principalPoint.x) / focalLength.x * near;
            var right = (crop.xMax - principalPoint.x) / focalLength.x * near;
            var bottom = (crop.yMin - principalPoint.y) / focalLength.y * near;
            var top = (crop.yMax - principalPoint.y) / focalLength.y * near;

            return Matrix4x4.Frustum(left, right, bottom, top, near, far);
        }

        private static Rect CalculateSensorCropRegion(Vector2Int sensorResolution, Vector2Int currentResolution)
        {
            var sensor = (Vector2)sensorResolution;
            var current = (Vector2)currentResolution;
            var scaleFactor = current / sensor;
            scaleFactor /= Mathf.Max(scaleFactor.x, scaleFactor.y);

            return new Rect(
                sensor.x * (1f - scaleFactor.x) * 0.5f,
                sensor.y * (1f - scaleFactor.y) * 0.5f,
                sensor.x * scaleFactor.x,
                sensor.y * scaleFactor.y);
        }

        private void OnDestroy()
        {
            if (compositeMaterial != null)
            {
                Destroy(compositeMaterial);
            }

            ReleaseOwnedRenderTextures();

            if (ownsLayerCamera && layerCamera != null)
            {
                Destroy(layerCamera.gameObject);
            }
        }

        private void ReleaseOwnedRenderTextures()
        {
            if (ownsOverlayRenderTexture && overlayRenderTexture != null)
            {
                overlayRenderTexture.Release();
                Destroy(overlayRenderTexture);
            }

            if (ownsCompositeRenderTexture && compositeRenderTexture != null)
            {
                compositeRenderTexture.Release();
                Destroy(compositeRenderTexture);
            }

            overlayRenderTexture = null;
            compositeRenderTexture = null;
            ownsOverlayRenderTexture = false;
            ownsCompositeRenderTexture = false;
        }

        public struct CompositeFrame
        {
            public Texture passthroughTexture;
            public RenderTexture overlayTexture;
            public RenderTexture compositeTexture;
            public DateTime timestamp;
            public long timestampUnixMilliseconds;
            public Pose cameraPose;
            public Vector2Int resolution;
            public int unityFrame;
        }
    }
}
