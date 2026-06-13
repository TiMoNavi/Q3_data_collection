using System;
using System.IO;
using InstantReplay;
using Meta.XR;
using UniEnc;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class PassthroughCameraDirectMp4Prototype : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private PassthroughCameraTextureReadinessProbe readinessProbe;
        [SerializeField] private bool autoFindCameraAccess = true;
        [SerializeField] private bool autoFindReadinessProbe = true;
        [SerializeField] private bool requireReadyProbe;
        [SerializeField] private bool pushOnlyWhenUpdatedThisFrame = true;

        [Header("Encoding")]
        [SerializeField] private int fallbackWidth = 1280;
        [SerializeField] private int fallbackHeight = 1280;
        [SerializeField] private int fallbackFrameRate = 30;
        [SerializeField] private int bitrateKbps = 8000;
        [SerializeField] private int maxRawFrameBuffers = 2;
        [SerializeField] private int videoInputQueueSize = 2;
        [SerializeField] private bool forceReadback;
        [SerializeField] private bool useStagingRenderTexture = true;
        [SerializeField] private int stagingBufferCount = 2;
        [SerializeField] private bool stagingFrameDataStartsAtTop;

        [Header("Run")]
        [SerializeField] private bool runOnStart;
        [SerializeField] private float startDelaySeconds = 1f;
        [SerializeField] private float autoStopSeconds = 5f;
        [SerializeField] private string outputFolderName = "DataCapture/PassthroughCameraDirectMp4Prototype";
        [SerializeField] private string filePrefix = "pca_direct";

        [Header("Result")]
        [SerializeField] private bool isRecording;
        [SerializeField] private string lastStatus = "Idle.";
        [SerializeField] private string outputPath;
        [SerializeField] private int pushedFrameCount;
        [SerializeField] private int blockedFrameCount;
        [SerializeField] private long outputBytes;

        private ManualTextureFrameProvider frameProvider;
        private UnboundedRecordingSession session;
        private double nextPushTime;
        private float startedAt;
        private float nextAutoStartTime;
        private long lastTimestampUnixMs;
        private bool stopInProgress;
        private bool autoStartConsumed;
        private RenderTexture[] stagingRenderTextures;
        private int stagingRenderTextureIndex;
        private Vector2Int stagingResolution;

        private void Awake()
        {
            ResolveCameraAccess();
            ResolveReadinessProbe();
        }

        private void OnEnable()
        {
            autoStartConsumed = false;
            if (runOnStart)
            {
                nextAutoStartTime = Time.unscaledTime + Mathf.Max(0f, startDelaySeconds);
            }
        }

        private void Update()
        {
            if (runOnStart &&
                !autoStartConsumed &&
                !isRecording &&
                !stopInProgress &&
                Time.unscaledTime >= nextAutoStartTime)
            {
                ResolveCameraAccess();
                ResolveReadinessProbe();
                if (!IsCameraReadyForRecording(out string blockedReason))
                {
                    lastStatus = blockedReason;
                    nextAutoStartTime = Time.unscaledTime + 0.5f;
                    return;
                }

                autoStartConsumed = true;
                StartRecording();
            }

            if (!isRecording || stopInProgress)
            {
                return;
            }

            if (autoStopSeconds > 0f && Time.realtimeSinceStartup - startedAt >= autoStopSeconds)
            {
                StopRecording();
                return;
            }

            PushLatestFrameIfDue();
        }

        private void OnDisable()
        {
            if (isRecording && !stopInProgress)
            {
                StopRecording();
            }
        }

        [ContextMenu("Start Direct PCA MP4 Prototype")]
        public void StartRecording()
        {
            if (isRecording)
            {
                lastStatus = "Already recording.";
                return;
            }

            ResolveCameraAccess();
            ResolveReadinessProbe();
            if (!IsCameraReadyForRecording(out string blockedReason))
            {
                lastStatus = blockedReason;
                Debug.LogWarning(lastStatus, this);
                return;
            }

            Texture texture;
            try
            {
                texture = cameraAccess.GetTexture();
            }
            catch (Exception ex)
            {
                lastStatus = "Blocked: failed to get passthrough camera texture. " + ex.Message;
                Debug.LogWarning(lastStatus, this);
                return;
            }

            if (texture == null)
            {
                lastStatus = "Blocked: passthrough camera has no texture.";
                Debug.LogWarning(lastStatus, this);
                return;
            }

            Vector2Int resolution = ResolveResolution(texture);
            int frameRate = ResolveFrameRate();
            Directory.CreateDirectory(GetOutputDirectory());
            outputPath = Path.Combine(
                GetOutputDirectory(),
                filePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".mp4");

            frameProvider = new ManualTextureFrameProvider();
            var options = new RealtimeEncodingOptions
            {
                VideoOptions = new VideoEncoderOptions
                {
                    Width = (uint)resolution.x,
                    Height = (uint)resolution.y,
                    FpsHint = (uint)frameRate,
                    Bitrate = (uint)(Mathf.Max(128, bitrateKbps) * 1000)
                },
                AudioOptions = new AudioEncoderOptions
                {
                    SampleRate = 44100,
                    Channels = 2,
                    Bitrate = 96000
                },
                FixedFrameRate = frameRate,
                ForceReadback = forceReadback,
                MaxNumberOfRawFrameBuffers = Mathf.Max(1, maxRawFrameBuffers),
                VideoInputQueueSize = Mathf.Max(1, videoInputQueueSize),
                AudioInputQueueSizeSeconds = 0.0,
                VideoLagAdjustmentThreshold = 0.5,
                AudioLagAdjustmentThreshold = 0.5
            };

            try
            {
                session = new UnboundedRecordingSession(
                    outputPath,
                    options,
                    frameProvider,
                    disposeFrameProvider: true,
                    audioSampleProvider: NullAudioSampleProvider.Instance,
                    disposeAudioSampleProvider: false,
                    onException: OnRecordingException);
            }
            catch (Exception ex)
            {
                lastStatus = "Failed to start direct PCA MP4: " + ex.Message;
                Debug.LogException(ex, this);
                DisposeSession();
                return;
            }

            isRecording = true;
            stopInProgress = false;
            startedAt = Time.realtimeSinceStartup;
            nextPushTime = Time.unscaledTimeAsDouble;
            pushedFrameCount = 0;
            blockedFrameCount = 0;
            outputBytes = 0;
            lastTimestampUnixMs = 0;
            lastStatus = "Recording direct PCA MP4.";
            Debug.Log("Recording direct PCA MP4 to " + outputPath, this);
        }

        [ContextMenu("Stop Direct PCA MP4 Prototype")]
        public async void StopRecording()
        {
            if (!isRecording || session == null || stopInProgress)
            {
                return;
            }

            stopInProgress = true;
            isRecording = false;
            lastStatus = "Finalizing direct PCA MP4...";

            try
            {
                await session.CompleteAsync();
                outputBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
                lastStatus = "Direct PCA MP4 finalized. bytes=" + outputBytes;
                Debug.Log(lastStatus + " path=" + outputPath, this);
            }
            catch (Exception ex)
            {
                lastStatus = "Failed to finalize direct PCA MP4: " + ex.Message;
                Debug.LogException(ex, this);
            }
            finally
            {
                DisposeSession();
                stopInProgress = false;
            }
        }

        private void PushLatestFrameIfDue()
        {
            ResolveCameraAccess();
            if (cameraAccess == null || !cameraAccess.IsPlaying)
            {
                BlockFrame("Passthrough camera is not playing.");
                return;
            }

            if (pushOnlyWhenUpdatedThisFrame && !cameraAccess.IsUpdatedThisFrame)
            {
                return;
            }

            int frameRate = ResolveFrameRate();
            double now = Time.unscaledTimeAsDouble;
            if (now < nextPushTime)
            {
                return;
            }

            Texture texture = cameraAccess.GetTexture();
            if (texture == null)
            {
                BlockFrame("Passthrough camera texture is null.");
                return;
            }

            long timestampUnixMs = new DateTimeOffset(cameraAccess.Timestamp).ToUnixTimeMilliseconds();
            if (timestampUnixMs > 0 && timestampUnixMs == lastTimestampUnixMs)
            {
                return;
            }

            Texture frameTexture = useStagingRenderTexture
                ? BlitToNextStagingTexture(texture)
                : texture;
            bool dataStartsAtTop = useStagingRenderTexture
                ? stagingFrameDataStartsAtTop
                : SystemInfo.graphicsUVStartsAtTop;
            frameProvider?.Push(frameTexture, now, dataStartsAtTop);
            pushedFrameCount++;
            lastTimestampUnixMs = timestampUnixMs;
            nextPushTime = now + 1.0 / Mathf.Max(1, frameRate);
            lastStatus = "Pushed PCA texture frame count=" + pushedFrameCount + ".";
        }

        private Texture BlitToNextStagingTexture(Texture source)
        {
            Vector2Int resolution = ResolveResolution(source);
            EnsureStagingRenderTextures(resolution);
            if (stagingRenderTextures == null || stagingRenderTextures.Length == 0)
            {
                return source;
            }

            RenderTexture target = stagingRenderTextures[stagingRenderTextureIndex];
            stagingRenderTextureIndex = (stagingRenderTextureIndex + 1) % stagingRenderTextures.Length;
            Graphics.Blit(source, target);
            return target;
        }

        private void EnsureStagingRenderTextures(Vector2Int resolution)
        {
            int bufferCount = Mathf.Clamp(stagingBufferCount, 1, 4);
            if (stagingRenderTextures != null &&
                stagingRenderTextures.Length == bufferCount &&
                stagingResolution == resolution)
            {
                return;
            }

            ReleaseStagingRenderTextures();
            stagingResolution = resolution;
            stagingRenderTextureIndex = 0;
            stagingRenderTextures = new RenderTexture[bufferCount];
            for (int i = 0; i < stagingRenderTextures.Length; i++)
            {
                var renderTexture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
                {
                    name = "PCA Direct MP4 Staging RT " + i,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                renderTexture.Create();
                stagingRenderTextures[i] = renderTexture;
            }
        }

        private void ResolveCameraAccess()
        {
            if (cameraAccess == null && autoFindCameraAccess)
            {
                cameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
            }
        }

        private void ResolveReadinessProbe()
        {
            if (readinessProbe == null && autoFindReadinessProbe)
            {
                readinessProbe = FindAnyObjectByType<PassthroughCameraTextureReadinessProbe>();
            }
        }

        private bool IsCameraReadyForRecording(out string blockedReason)
        {
            if (cameraAccess == null)
            {
                blockedReason = "Blocked: no PassthroughCameraAccess found.";
                return false;
            }

            if (!PassthroughCameraAccess.IsSupported)
            {
                blockedReason = "Blocked: PassthroughCameraAccess is not supported.";
                return false;
            }

            if (!cameraAccess.enabled)
            {
                readinessProbe?.RequestProbe();
                blockedReason = "Blocked: PassthroughCameraAccess is disabled.";
                return false;
            }

            if (!cameraAccess.IsPlaying)
            {
                readinessProbe?.RequestProbe();
                blockedReason = "Waiting for PassthroughCameraAccess to play.";
                return false;
            }

            Texture texture;
            try
            {
                texture = cameraAccess.GetTexture();
            }
            catch (Exception ex)
            {
                readinessProbe?.RequestProbe();
                blockedReason = "Blocked: failed to get passthrough camera texture. " + ex.Message;
                return false;
            }

            if (texture == null)
            {
                readinessProbe?.RequestProbe();
                blockedReason = "Blocked: passthrough camera has no texture.";
                return false;
            }

            if (requireReadyProbe)
            {
                if (readinessProbe == null)
                {
                    blockedReason = "Blocked: requireReadyProbe is enabled but no readiness probe was found.";
                    return false;
                }

                if (!readinessProbe.IsReady)
                {
                    readinessProbe.RequestProbe();
                    blockedReason = "Waiting for PCA texture readiness probe. status=" + readinessProbe.LastStatus;
                    return false;
                }
            }

            blockedReason = string.Empty;
            return true;
        }

        private Vector2Int ResolveResolution(Texture texture)
        {
            Vector2Int resolution = cameraAccess != null ? cameraAccess.CurrentResolution : default;
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                resolution = texture != null
                    ? new Vector2Int(texture.width, texture.height)
                    : new Vector2Int(fallbackWidth, fallbackHeight);
            }

            return new Vector2Int(MakeEven(Mathf.Max(16, resolution.x)), MakeEven(Mathf.Max(16, resolution.y)));
        }

        private int ResolveFrameRate()
        {
            int cameraFrameRate = cameraAccess != null ? cameraAccess.MaxFramerate : 0;
            return Mathf.Clamp(cameraFrameRate > 0 ? cameraFrameRate : fallbackFrameRate, 1, 120);
        }

        private string GetOutputDirectory()
        {
            return Path.Combine(Application.persistentDataPath, outputFolderName);
        }

        private void BlockFrame(string reason)
        {
            blockedFrameCount++;
            lastStatus = reason;
        }

        private void DisposeSession()
        {
            session?.Dispose();
            session = null;
            frameProvider = null;
            ReleaseStagingRenderTextures();
        }

        private void ReleaseStagingRenderTextures()
        {
            if (stagingRenderTextures == null)
            {
                return;
            }

            for (int i = 0; i < stagingRenderTextures.Length; i++)
            {
                if (stagingRenderTextures[i] == null)
                {
                    continue;
                }

                stagingRenderTextures[i].Release();
                Destroy(stagingRenderTextures[i]);
            }

            stagingRenderTextures = null;
            stagingResolution = default;
        }

        private void OnRecordingException(Exception exception)
        {
            lastStatus = "InstantReplay exception: " + exception.Message;
            Debug.LogException(exception, this);
        }

        private static int MakeEven(int value)
        {
            return value % 2 == 0 ? value : value - 1;
        }

        private sealed class ManualTextureFrameProvider : IFrameProvider
        {
            public event IFrameProvider.ProvideFrame OnFrameProvided;

            public void Push(Texture texture, double timestamp, bool dataStartsAtTop)
            {
                OnFrameProvided?.Invoke(new IFrameProvider.Frame(texture, timestamp, dataStartsAtTop));
            }

            public void Dispose()
            {
                OnFrameProvided = null;
            }
        }
    }
}
