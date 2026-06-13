using DataCapture.Compositing;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class VideoFrameInputResolver : MonoBehaviour
    {
        [SerializeField] private VideoFrameInputConfigurationSO inputConfiguration;
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;
        [SerializeField] private EncoderConfigurationSO encoderConfiguration;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private MergedFrameSnapshotQueueSO synchronizedFrameQueue;
        [SerializeField] private PassthroughCameraLayerCompositor compositor;
        [SerializeField] private bool resolveOnUpdate = true;
        [SerializeField] private bool updateOnlyForNewSourceFrame = true;
        [SerializeField] private bool allowLegacyCurrentFallback;

        [Header("Runtime Diagnostics")]
        [SerializeField] private ResolvedVideoEncodingParameters resolvedParameters;
        [SerializeField] private string lastStatus;
        [SerializeField] private long lastResolvedSourceFrameId = -1;
        [SerializeField] private int resolvedFrameCount;
        [SerializeField] private int blockedResolveCount;

        private RenderTexture stagingTexture;
        private RenderTexture compositeTexture;
        private Material compositeMaterial;

        private void Update()
        {
            if (resolveOnUpdate)
            {
                ResolveLatestFrame();
            }
        }

        private void OnDestroy()
        {
            ReleaseStagingTexture();
            ReleaseCompositeTexture();
            if (compositeMaterial != null)
            {
                Destroy(compositeMaterial);
                compositeMaterial = null;
            }
        }

        [ContextMenu("Resolve Latest Video Frame Input")]
        public bool ResolveLatestFrame()
        {
            if (currentVideoFrameInput == null)
            {
                return Block("CurrentVideoFrameInputSO is not assigned.");
            }

            if (!TryGetSource(
                    out Texture sourceTexture,
                    out VideoFrameInputSourceKind sourceKind,
                    out CameraImageFrameRecord sourceFrame,
                    out long synchronizedTimestampUnixMs,
                    out long synchronizedSnapshotFrameId,
                    out Vector2Int sourceResolution))
            {
                currentVideoFrameInput.Clear();
                return Block("No valid video frame source is available.");
            }

            if (updateOnlyForNewSourceFrame && sourceFrame.frameId == lastResolvedSourceFrameId)
            {
                return Block("Source frame was already resolved.");
            }

            resolvedParameters = VideoEncodingParameterResolver.Resolve(
                currentStreamState,
                currentCameraImage,
                encoderConfiguration,
                pipelineConfiguration);
            if (!resolvedParameters.isValid)
            {
                currentVideoFrameInput.Clear();
                return Block(resolvedParameters.invalidReason);
            }

            Texture outputTexture = sourceTexture;
            if (inputConfiguration == null || inputConfiguration.useSharedStagingRenderTexture)
            {
                EnsureStagingTexture(resolvedParameters.width, resolvedParameters.height);
                Graphics.Blit(sourceTexture, stagingTexture);
                outputTexture = stagingTexture;
            }

            currentVideoFrameInput.SetFrame(
                outputTexture,
                sourceKind,
                sourceFrame,
                synchronizedTimestampUnixMs,
                synchronizedSnapshotFrameId,
                sourceResolution,
                resolvedParameters);

            lastResolvedSourceFrameId = sourceFrame.frameId;
            resolvedFrameCount++;
            lastStatus = "Resolved video input frame.";
            return true;
        }

        private bool TryGetSource(
            out Texture texture,
            out VideoFrameInputSourceKind sourceKind,
            out CameraImageFrameRecord sourceFrame,
            out long synchronizedTimestampUnixMs,
            out long synchronizedSnapshotFrameId,
            out Vector2Int sourceResolution)
        {
            if (TryGetSynchronizedSource(
                    out texture,
                    out sourceKind,
                    out sourceFrame,
                    out synchronizedTimestampUnixMs,
                    out synchronizedSnapshotFrameId,
                    out sourceResolution))
            {
                return true;
            }

            if (!allowLegacyCurrentFallback)
            {
                texture = null;
                sourceKind = inputConfiguration != null
                    ? inputConfiguration.sourceKind
                    : VideoFrameInputSourceKind.RawCameraImage;
                sourceFrame = default;
                synchronizedTimestampUnixMs = 0;
                synchronizedSnapshotFrameId = -1;
                sourceResolution = default;
                return false;
            }

            var requestedSource = inputConfiguration != null
                ? inputConfiguration.sourceKind
                : VideoFrameInputSourceKind.RawCameraImage;

            if (requestedSource == VideoFrameInputSourceKind.PassthroughUnityComposite &&
                TryGetCompositeSource(out texture, out sourceKind, out sourceFrame, out sourceResolution))
            {
                synchronizedTimestampUnixMs = sourceFrame.timestampUnixMs;
                synchronizedSnapshotFrameId = -1;
                return true;
            }

            bool allowRawFallback = requestedSource == VideoFrameInputSourceKind.RawCameraImage ||
                inputConfiguration == null ||
                inputConfiguration.fallbackToRawCameraImage;
            if (allowRawFallback && TryGetRawCameraSource(out texture, out sourceKind, out sourceFrame, out sourceResolution))
            {
                synchronizedTimestampUnixMs = sourceFrame.timestampUnixMs;
                synchronizedSnapshotFrameId = -1;
                return true;
            }

            texture = null;
            sourceKind = requestedSource;
            sourceFrame = default;
            synchronizedTimestampUnixMs = 0;
            synchronizedSnapshotFrameId = -1;
            sourceResolution = default;
            return false;
        }

        private bool TryGetSynchronizedSource(
            out Texture texture,
            out VideoFrameInputSourceKind sourceKind,
            out CameraImageFrameRecord sourceFrame,
            out long synchronizedTimestampUnixMs,
            out long synchronizedSnapshotFrameId,
            out Vector2Int sourceResolution)
        {
            texture = null;
            sourceKind = inputConfiguration != null
                ? inputConfiguration.sourceKind
                : VideoFrameInputSourceKind.RawCameraImage;
            sourceFrame = default;
            synchronizedTimestampUnixMs = 0;
            synchronizedSnapshotFrameId = -1;
            sourceResolution = default;

            if (synchronizedFrameQueue == null ||
                !synchronizedFrameQueue.TryGetLatestSendable(out MergedFrameSnapshotRecord snapshot) ||
                !snapshot.hasCameraImage ||
                snapshot.timestampUnixMs <= 0)
            {
                return false;
            }

            sourceFrame = snapshot.cameraImage;
            synchronizedTimestampUnixMs = snapshot.timestampUnixMs;
            synchronizedSnapshotFrameId = snapshot.frameId;
            sourceResolution = ResolveResolution(snapshot.cameraImage.resolution, snapshot.cameraMetadata.currentResolution);

            if (sourceKind == VideoFrameInputSourceKind.PassthroughUnityComposite)
            {
                if (!snapshot.hasVirtualLayer ||
                    snapshot.cameraImage.texture == null ||
                    snapshot.virtualLayer.texture == null)
                {
                    return false;
                }

                texture = ComposeSynchronizedFrame(snapshot.cameraImage.texture, snapshot.virtualLayer.texture, sourceResolution);
                return texture != null;
            }

            if (snapshot.cameraImage.texture == null)
            {
                return false;
            }

            sourceKind = VideoFrameInputSourceKind.RawCameraImage;
            texture = snapshot.cameraImage.texture;
            return true;
        }

        private Texture ComposeSynchronizedFrame(Texture cameraTexture, Texture virtualLayerTexture, Vector2Int resolution)
        {
            if (cameraTexture == null || virtualLayerTexture == null)
            {
                return null;
            }

            EnsureCompositeTexture(resolution);
            EnsureCompositeMaterial();
            if (compositeTexture == null || compositeMaterial == null)
            {
                return null;
            }

            compositeMaterial.SetTexture("_OverlayTex", virtualLayerTexture);
            Graphics.Blit(cameraTexture, compositeTexture, compositeMaterial);
            return compositeTexture;
        }

        private void EnsureCompositeTexture(Vector2Int resolution)
        {
            resolution.x = Mathf.Max(16, resolution.x);
            resolution.y = Mathf.Max(16, resolution.y);

            if (compositeTexture != null &&
                compositeTexture.width == resolution.x &&
                compositeTexture.height == resolution.y)
            {
                return;
            }

            ReleaseCompositeTexture();
            compositeTexture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
            {
                name = "Q3DC Synchronized Composite Video Input",
                useMipMap = false,
                autoGenerateMips = false
            };
            compositeTexture.Create();
        }

        private void EnsureCompositeMaterial()
        {
            if (compositeMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/PassthroughLayerCompositor/AlphaComposite");
            if (shader == null)
            {
                return;
            }

            compositeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
        }

        private bool TryGetCompositeSource(
            out Texture texture,
            out VideoFrameInputSourceKind sourceKind,
            out CameraImageFrameRecord sourceFrame,
            out Vector2Int sourceResolution)
        {
            if (compositor != null && compositor.TryGetLatestComposite(out var composite) &&
                composite.compositeTexture != null &&
                TryGetCurrentCameraFrame(out sourceFrame))
            {
                texture = composite.compositeTexture;
                sourceKind = VideoFrameInputSourceKind.PassthroughUnityComposite;
                sourceResolution = composite.resolution.x > 0 && composite.resolution.y > 0
                    ? composite.resolution
                    : new Vector2Int(texture.width, texture.height);
                return true;
            }

            texture = null;
            sourceKind = VideoFrameInputSourceKind.PassthroughUnityComposite;
            sourceFrame = default;
            sourceResolution = default;
            return false;
        }

        private bool TryGetRawCameraSource(
            out Texture texture,
            out VideoFrameInputSourceKind sourceKind,
            out CameraImageFrameRecord sourceFrame,
            out Vector2Int sourceResolution)
        {
            if (currentCameraImage != null &&
                currentCameraImage.isValid &&
                currentCameraImage.currentTexture != null &&
                TryGetCurrentCameraFrame(out sourceFrame))
            {
                texture = currentCameraImage.currentTexture;
                sourceKind = VideoFrameInputSourceKind.RawCameraImage;
                sourceResolution = currentCameraImage.resolution.x > 0 && currentCameraImage.resolution.y > 0
                    ? currentCameraImage.resolution
                    : new Vector2Int(texture.width, texture.height);
                return true;
            }

            texture = null;
            sourceKind = VideoFrameInputSourceKind.RawCameraImage;
            sourceFrame = default;
            sourceResolution = default;
            return false;
        }

        private bool TryGetCurrentCameraFrame(out CameraImageFrameRecord sourceFrame)
        {
            if (currentCameraImage == null || !currentCameraImage.isValid)
            {
                sourceFrame = default;
                return false;
            }

            sourceFrame = currentCameraImage.ToRecord();
            return true;
        }

        private void EnsureStagingTexture(int width, int height)
        {
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            var format = inputConfiguration != null
                ? inputConfiguration.stagingFormat
                : RenderTextureFormat.ARGB32;

            if (stagingTexture != null &&
                stagingTexture.width == width &&
                stagingTexture.height == height &&
                stagingTexture.format == format)
            {
                return;
            }

            ReleaseStagingTexture();
            stagingTexture = new RenderTexture(width, height, 0, format)
            {
                name = "Q3DC Shared Video Frame Input",
                useMipMap = false,
                autoGenerateMips = false
            };
            stagingTexture.Create();
        }

        private void ReleaseStagingTexture()
        {
            if (stagingTexture == null)
            {
                return;
            }

            stagingTexture.Release();
            Destroy(stagingTexture);
            stagingTexture = null;
        }

        private void ReleaseCompositeTexture()
        {
            if (compositeTexture == null)
            {
                return;
            }

            compositeTexture.Release();
            Destroy(compositeTexture);
            compositeTexture = null;
        }

        private static Vector2Int ResolveResolution(Vector2Int preferred, Vector2Int fallback)
        {
            if (preferred.x > 0 && preferred.y > 0)
            {
                return preferred;
            }

            return fallback.x > 0 && fallback.y > 0
                ? fallback
                : new Vector2Int(1280, 960);
        }

        private bool Block(string reason)
        {
            blockedResolveCount++;
            lastStatus = reason;
            return false;
        }
    }
}
