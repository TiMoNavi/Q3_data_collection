using System;
using Meta.XR;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [DefaultExecutionOrder(50)]
    public class PassthroughCameraFrameWriter : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private bool autoFindCameraAccess = true;
        [SerializeField] private bool writeOnlyWhenUpdatedThisFrame = true;
        [SerializeField] private float nearClipPlane = 0.02f;
        [SerializeField] private float farClipPlane = 100f;

        [Header("Texture Snapshot")]
        [SerializeField] private bool snapshotCameraTexture = true;
        [SerializeField] private int snapshotPoolSize = 8;
        [SerializeField] private RenderTextureFormat snapshotFormat = RenderTextureFormat.ARGB32;

        [Header("Outputs")]
        [SerializeField] private CurrentCameraImageSO currentImage;
        [SerializeField] private CurrentCameraFrameTimingSO currentTiming;
        [SerializeField] private CurrentCameraPoseSO currentCameraPose;
        [SerializeField] private CurrentCameraMetadataSO currentMetadata;
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;

        private long nextFrameId = 1;
        private long lastFrameTimestampUnixMs;
        private float measuredFramerate;
        private RenderTexture[] textureSnapshots;
        private int nextTextureSnapshotIndex;

        private void Awake()
        {
            if (cameraAccess == null && autoFindCameraAccess)
            {
                cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            }
        }

        private void Update()
        {
            TryWriteCurrentFrame();
        }

        [ContextMenu("Write Current Frame")]
        public bool TryWriteCurrentFrame()
        {
            if (cameraAccess == null || !cameraAccess.IsPlaying)
            {
                return false;
            }

            if (writeOnlyWhenUpdatedThisFrame && !cameraAccess.IsUpdatedThisFrame)
            {
                return false;
            }

            Texture texture = cameraAccess.GetTexture();
            if (texture == null)
            {
                return false;
            }

            DateTime timestamp = cameraAccess.Timestamp;
            long timestampUnixMs = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds();
            measuredFramerate = EstimateFrameRate(timestampUnixMs);
            Pose pose = cameraAccess.GetCameraPose();
            Vector2Int resolution = cameraAccess.CurrentResolution;
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                resolution = new Vector2Int(texture.width, texture.height);
            }
            Texture stableTexture = snapshotCameraTexture
                ? CopyToSnapshotTexture(texture, resolution)
                : texture;
            if (stableTexture == null)
            {
                return false;
            }

            var intrinsics = cameraAccess.Intrinsics;
            Matrix4x4 localToWorld = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);
            Matrix4x4 worldToLocal = localToWorld.inverse;
            Matrix4x4 projectionMatrix = PassthroughProjectionUtility.BuildProjectionMatrix(
                intrinsics,
                resolution,
                nearClipPlane,
                farClipPlane);
            long frameId = nextFrameId++;
            string timestampUtc = timestamp.ToUniversalTime().ToString("O");

            var imageRecord = new CameraImageFrameRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                texture = stableTexture,
                resolution = resolution,
                encodedFrameId = -1,
                debugImagePath = string.Empty
            };
            currentImage?.SetFrame(stableTexture, imageRecord, timestampUtc);

            currentTiming?.SetTiming(new CameraFrameTimingRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                timestampUtc = timestampUtc,
                unityFrame = Time.frameCount,
                isUpdatedThisFrame = cameraAccess.IsUpdatedThisFrame
            });

            currentCameraPose?.SetPose(new CameraPoseRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                cameraPosition = pose.position,
                cameraRotation = pose.rotation,
                cameraLocalToWorldMatrix = localToWorld,
                cameraWorldToLocalMatrix = worldToLocal
            });

            currentMetadata?.SetMetadata(new CameraMetadataRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                currentResolution = resolution,
                focalLength = intrinsics.FocalLength,
                principalPoint = intrinsics.PrincipalPoint,
                sensorResolution = intrinsics.SensorResolution,
                lensOffset = intrinsics.LensOffset,
                projectionMatrix = projectionMatrix,
                cameraLocalToWorldMatrix = localToWorld,
                cameraWorldToLocalMatrix = worldToLocal,
                hasDistortionData = false,
                distortionCoefficients = default,
                metadataSource = nameof(PassthroughCameraAccess)
            });

            currentStreamState?.SetState(new CameraStreamStateRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                cameraEye = ToCameraEye(cameraAccess.CameraPosition),
                requestedResolution = cameraAccess.RequestedResolution,
                currentResolution = resolution,
                requestedMaxFramerate = cameraAccess.MaxFramerate,
                measuredFramerate = measuredFramerate,
                isPlaying = cameraAccess.IsPlaying,
                isUpdatedThisFrame = cameraAccess.IsUpdatedThisFrame,
                isSupported = PassthroughCameraAccess.IsSupported,
                texturePropertyName = cameraAccess.TexturePropertyName
            });

            return true;
        }

        private void OnDestroy()
        {
            ReleaseTextureSnapshots();
        }

        private float EstimateFrameRate(long timestampUnixMs)
        {
            if (lastFrameTimestampUnixMs <= 0 || timestampUnixMs <= lastFrameTimestampUnixMs)
            {
                lastFrameTimestampUnixMs = timestampUnixMs;
                return measuredFramerate;
            }

            long deltaMs = timestampUnixMs - lastFrameTimestampUnixMs;
            lastFrameTimestampUnixMs = timestampUnixMs;
            float instantRate = deltaMs > 0 ? 1000f / deltaMs : 0f;
            return measuredFramerate <= 0f
                ? instantRate
                : Mathf.Lerp(measuredFramerate, instantRate, 0.1f);
        }

        private static PassthroughCameraEye ToCameraEye(PassthroughCameraAccess.CameraPositionType cameraPosition)
        {
            return cameraPosition == PassthroughCameraAccess.CameraPositionType.Left
                ? PassthroughCameraEye.Left
                : PassthroughCameraEye.Right;
        }

        private Texture CopyToSnapshotTexture(Texture source, Vector2Int resolution)
        {
            if (source == null)
            {
                return null;
            }

            RenderTexture snapshot = GetNextTextureSnapshot(resolution);
            if (snapshot == null)
            {
                return null;
            }

            Graphics.Blit(source, snapshot);
            return snapshot;
        }

        private RenderTexture GetNextTextureSnapshot(Vector2Int resolution)
        {
            resolution.x = Mathf.Max(16, resolution.x);
            resolution.y = Mathf.Max(16, resolution.y);
            int poolSize = Mathf.Max(1, snapshotPoolSize);
            if (!IsTextureSnapshotPoolCompatible(poolSize, resolution))
            {
                ReleaseTextureSnapshots();
                textureSnapshots = new RenderTexture[poolSize];
                for (int i = 0; i < textureSnapshots.Length; i++)
                {
                    textureSnapshots[i] = new RenderTexture(resolution.x, resolution.y, 0, snapshotFormat)
                    {
                        name = "Q3DC Camera Image Snapshot " + i,
                        useMipMap = false,
                        autoGenerateMips = false
                    };
                    textureSnapshots[i].Create();
                }
            }

            RenderTexture snapshot = textureSnapshots[nextTextureSnapshotIndex];
            nextTextureSnapshotIndex = (nextTextureSnapshotIndex + 1) % textureSnapshots.Length;
            return snapshot;
        }

        private bool IsTextureSnapshotPoolCompatible(int poolSize, Vector2Int resolution)
        {
            if (textureSnapshots == null || textureSnapshots.Length != poolSize)
            {
                return false;
            }

            for (int i = 0; i < textureSnapshots.Length; i++)
            {
                RenderTexture snapshot = textureSnapshots[i];
                if (snapshot == null ||
                    snapshot.width != resolution.x ||
                    snapshot.height != resolution.y ||
                    snapshot.format != snapshotFormat)
                {
                    return false;
                }
            }

            return true;
        }

        private void ReleaseTextureSnapshots()
        {
            if (textureSnapshots == null)
            {
                return;
            }

            for (int i = 0; i < textureSnapshots.Length; i++)
            {
                if (textureSnapshots[i] == null)
                {
                    continue;
                }

                textureSnapshots[i].Release();
                Destroy(textureSnapshots[i]);
            }

            textureSnapshots = null;
            nextTextureSnapshotIndex = 0;
        }
    }
}
