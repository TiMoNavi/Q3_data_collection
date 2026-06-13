using System;
using System.IO;
using DataCapture.Synchronization;
using InstantReplay;
using SObasic.CurrentQueueBridge;
using UniEnc;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class InstantReplayLocalMp4Recorder : MonoBehaviour
    {
        private const double MinimumInitialPresentationSeconds = 0.000001;
        private const string TimebaseSource = "Stage3MergedFrameSnapshot.timestampUnixMs";
        private const string Mp4PtsTimebase = "secondsSinceTimestampStartUnixMs";

        [Header("SO Inputs")]
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedSnapshotQueue;
        [SerializeField] private EncodedOutputMetadataBinder outputBinder;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;

        [Header("Output")]
        [SerializeField] private string outputDirectoryName = "LocalMp4";
        [SerializeField] private string filePrefix = "q3dc_local_mp4";
        [SerializeField] private bool androidPlayerOnly = true;
        [SerializeField] private bool writeMetadataSidecar = true;
        [SerializeField] private bool publishFileArtifact = true;

        [Header("Memory / Queue Limits")]
        [SerializeField] private bool forceReadback;
        [SerializeField] private int maxRawFrameBuffers = 2;
        [SerializeField] private int videoInputQueueSize = 32;
        [SerializeField] private double audioInputQueueSeconds = 0.25;

        [Header("Timing")]
        [SerializeField] private bool preserveInputFrameTimestamps = true;

        [Header("Automation")]
        [SerializeField] private bool startOnEnable;
        [SerializeField] private bool followRecordingSessionState = true;
        [SerializeField] private float autoStartRetrySeconds = 0.5f;
        [SerializeField] private float autoStopSeconds;

        [Header("Runtime Diagnostics")]
        [SerializeField] private bool isRecording;
        [SerializeField] private int pushedFrameCount;
        [SerializeField] private int blockedFrameCount;
        [SerializeField] private string lastStatus;
        [SerializeField] private string outputPath;
        [SerializeField] private string metadataSidecarPath;
        [SerializeField] private string manifestPath;
        [SerializeField] private long lastPushedSourceFrameId = -1;
        [SerializeField] private long firstPushedSourceFrameId = -1;
        [SerializeField] private long firstPushedTimestampUnixMs;
        [SerializeField] private long lastPushedTimestampUnixMs;

        private ManualTextureFrameProvider frameProvider;
        private UnboundedRecordingSession session;
        private StreamWriter metadataSidecarWriter;
        private double nextPushTime;
        private long firstInputTimestampUnixMs;
        private long lastPresentationTimestampUnixMs;
        private double lastPresentationTimestampSeconds = -1.0;
        private float nextAutoStartAttemptTime;
        private float startedAt;
        private bool stopInProgress;
        private bool startOnEnablePending;
        private bool sessionHasFailure;
        private string sessionFailureReason = string.Empty;

        private void OnEnable()
        {
            startOnEnablePending = startOnEnable && !followRecordingSessionState;
            nextAutoStartAttemptTime = Time.unscaledTime;
        }

        private void Update()
        {
            FollowRecordingState();

            if (!isRecording || stopInProgress)
            {
                return;
            }

            if (autoStopSeconds > 0f && Time.realtimeSinceStartup - startedAt >= autoStopSeconds)
            {
                StopRecording();
                return;
            }

            if (currentVideoFrameInput == null || currentVideoFrameInput.frameRate <= 0)
            {
                BlockFrame("CurrentVideoFrameInputSO is not ready.");
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (now < nextPushTime)
            {
                return;
            }

            nextPushTime = now + 1.0 / Mathf.Max(1, currentVideoFrameInput.frameRate);
            PushCurrentInputFrame();
        }

        private void OnDisable()
        {
            if (isRecording && !stopInProgress)
            {
                StopRecording();
            }
        }

        private void FollowRecordingState()
        {
            if (followRecordingSessionState)
            {
                startOnEnablePending = false;
                if (recordingState == null)
                {
                    return;
                }

                bool shouldRecord =
                    recordingState.IsRecording &&
                    pipelineConfiguration != null &&
                    pipelineConfiguration.AllowsLocalMp4Save;
                if (shouldRecord)
                {
                    if (!isRecording && !stopInProgress && Time.unscaledTime >= nextAutoStartAttemptTime)
                    {
                        nextAutoStartAttemptTime = Time.unscaledTime + Mathf.Max(0.1f, autoStartRetrySeconds);
                        StartRecording();
                    }

                    return;
                }

                if (isRecording && !stopInProgress)
                {
                    StopRecording();
                }

                return;
            }

            if (startOnEnablePending && !isRecording && !stopInProgress &&
                Time.unscaledTime >= nextAutoStartAttemptTime)
            {
                nextAutoStartAttemptTime = Time.unscaledTime + Mathf.Max(0.1f, autoStartRetrySeconds);
                StartRecording();
                if (isRecording)
                {
                    startOnEnablePending = false;
                }
            }
        }

        private void OnDestroy()
        {
            DisposeSession();
        }

        [ContextMenu("Start Local MP4 Recording")]
        public void StartRecording()
        {
            if (isRecording)
            {
                lastStatus = "Already recording.";
                startOnEnablePending = false;
                return;
            }

            outputPath = string.Empty;
            metadataSidecarPath = string.Empty;
            manifestPath = string.Empty;
            pushedFrameCount = 0;
            blockedFrameCount = 0;
            lastPushedSourceFrameId = -1;
            firstPushedSourceFrameId = -1;
            firstPushedTimestampUnixMs = 0;
            lastPushedTimestampUnixMs = 0;

            if (pipelineConfiguration != null && !pipelineConfiguration.AllowsLocalMp4Save)
            {
                lastStatus = "Blocked: outputMode is not LocalMp4Save.";
                MarkMp4StateFailure(lastStatus);
                Debug.LogWarning(lastStatus, this);
                return;
            }

            if (androidPlayerOnly && (Application.platform != RuntimePlatform.Android || Application.isEditor))
            {
                lastStatus = "Blocked: Android player build required.";
                MarkMp4StateFailure(lastStatus);
                Debug.LogWarning(lastStatus, this);
                return;
            }

            if (!TryGetCurrentParameters(out int width, out int height, out int frameRate, out int bitrateKbps))
            {
                string waitingStatus = "Waiting: CurrentVideoFrameInputSO has no valid encoding parameters.";
                bool statusChanged = lastStatus != waitingStatus;
                lastStatus = waitingStatus;
                MarkMp4StateWaiting(lastStatus);
                if (statusChanged)
                {
                    Debug.LogWarning(lastStatus, this);
                }

                return;
            }

            Directory.CreateDirectory(GetOutputDirectory());
            outputPath = Path.Combine(
                GetOutputDirectory(),
                filePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".mp4");
            metadataSidecarPath = Path.ChangeExtension(outputPath, ".metadata.jsonl");
            manifestPath = Path.ChangeExtension(outputPath, ".manifest.json");

            ResetSessionDiagnostics();
            frameProvider = new ManualTextureFrameProvider();
            var options = new RealtimeEncodingOptions
            {
                VideoOptions = new VideoEncoderOptions
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    FpsHint = (uint)frameRate,
                    Bitrate = (uint)(Mathf.Max(128, bitrateKbps) * 1000)
                },
                AudioOptions = new AudioEncoderOptions
                {
                    SampleRate = 44100,
                    Channels = 2,
                    Bitrate = 96000
                },
                FixedFrameRate = preserveInputFrameTimestamps ? null : frameRate,
                ForceReadback = forceReadback,
                MaxNumberOfRawFrameBuffers = Mathf.Max(1, maxRawFrameBuffers),
                VideoInputQueueSize = Mathf.Max(1, videoInputQueueSize),
                AudioInputQueueSizeSeconds = Math.Max(0.0, audioInputQueueSeconds),
                VideoLagAdjustmentThreshold = preserveInputFrameTimestamps ? double.MaxValue : 0.5,
                AudioLagAdjustmentThreshold = preserveInputFrameTimestamps ? double.MaxValue : 0.5
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
                lastStatus = "Failed to start: " + ex.Message;
                Debug.LogException(ex, this);
                DisposeSession();
                return;
            }

            stopInProgress = false;
            isRecording = true;
            startedAt = Time.realtimeSinceStartup;
            nextPushTime = Time.unscaledTimeAsDouble;
            OpenMetadataSidecar();
            lastStatus = "Recording local MP4.";
            MarkMp4StateRecording();
            Debug.Log("Recording local MP4 to " + outputPath, this);
        }

        [ContextMenu("Stop Local MP4 Recording")]
        public async void StopRecording()
        {
            if (!isRecording || session == null || stopInProgress)
            {
                return;
            }

            stopInProgress = true;
            isRecording = false;
            lastStatus = "Finalizing MP4...";
            MarkMp4StateFinalizing();

            try
            {
                await session.CompleteAsync();
                CloseMetadataSidecar();
                WriteManifest();
                PublishFileArtifact();
                lastStatus = sessionHasFailure
                    ? "MP4 finalized after failure: " + sessionFailureReason
                    : "MP4 finalized.";
                MarkMp4StateFinalized();
                Debug.Log("MP4 finalized: " + outputPath, this);
            }
            catch (Exception ex)
            {
                lastStatus = "Failed to finalize MP4: " + ex.Message;
                sessionHasFailure = true;
                sessionFailureReason = lastStatus;
                MarkMp4StateFailure(lastStatus);
                Debug.LogException(ex, this);
            }
            finally
            {
                CloseMetadataSidecar();
                DisposeSession();
                stopInProgress = false;
            }
        }

        private void PushCurrentInputFrame()
        {
            if (currentVideoFrameInput == null ||
                !currentVideoFrameInput.isValid ||
                currentVideoFrameInput.inputTexture == null)
            {
                BlockFrame("CurrentVideoFrameInputSO has no texture.");
                return;
            }

            long inputFrameId = ResolveInputFrameId();
            if (inputFrameId == lastPushedSourceFrameId)
            {
                BlockFrame("Input frame was already pushed.");
                return;
            }

            if (!TryResolvePresentationSeconds(currentVideoFrameInput.timestampUnixMs, out double presentationSeconds))
            {
                return;
            }

            frameProvider?.Push(
                currentVideoFrameInput.inputTexture,
                presentationSeconds,
                SystemInfo.graphicsUVStartsAtTop);

            pushedFrameCount++;
            if (firstPushedSourceFrameId < 0)
            {
                firstPushedSourceFrameId = inputFrameId;
                firstPushedTimestampUnixMs = currentVideoFrameInput.timestampUnixMs;
            }

            lastPushedSourceFrameId = inputFrameId;
            lastPushedTimestampUnixMs = currentVideoFrameInput.timestampUnixMs;
            WriteMetadataSidecarLine(
                currentVideoFrameInput.synchronizedSnapshotFrameId,
                currentVideoFrameInput.sourceCameraFrameId,
                currentVideoFrameInput.timestampUnixMs);
            lastStatus = "Pushed local MP4 frame.";
        }

        private long ResolveInputFrameId()
        {
            if (currentVideoFrameInput == null)
            {
                return -1;
            }

            return currentVideoFrameInput.synchronizedSnapshotFrameId >= 0
                ? currentVideoFrameInput.synchronizedSnapshotFrameId
                : currentVideoFrameInput.sourceCameraFrameId;
        }

        private bool TryResolvePresentationSeconds(long timestampUnixMs, out double presentationSeconds)
        {
            presentationSeconds = 0.0;
            if (timestampUnixMs <= 0)
            {
                FailCurrentSession("CurrentVideoFrameInputSO timestamp is invalid.");
                return false;
            }

            if (firstInputTimestampUnixMs <= 0)
            {
                firstInputTimestampUnixMs = timestampUnixMs;
            }

            if (lastPresentationTimestampUnixMs > 0 && timestampUnixMs <= lastPresentationTimestampUnixMs)
            {
                FailCurrentSession(
                    "CurrentVideoFrameInputSO timestamp is not monotonic. inputFrameId=" +
                    ResolveInputFrameId() +
                    " timestampUnixMs=" + timestampUnixMs +
                    " lastTimestampUnixMs=" + lastPresentationTimestampUnixMs);
                return false;
            }

            presentationSeconds = Math.Max(0L, timestampUnixMs - firstInputTimestampUnixMs) / 1000.0;
            if (presentationSeconds <= 0.0)
            {
                // InstantReplay drops frames whose timestamp delta from its initial zero value is not positive.
                presentationSeconds = MinimumInitialPresentationSeconds;
            }

            if (lastPresentationTimestampSeconds >= 0.0 && presentationSeconds <= lastPresentationTimestampSeconds)
            {
                FailCurrentSession(
                    "Resolved MP4 presentation time is not monotonic. inputFrameId=" +
                    ResolveInputFrameId() +
                    " presentationSeconds=" + presentationSeconds +
                    " lastPresentationSeconds=" + lastPresentationTimestampSeconds);
                return false;
            }

            lastPresentationTimestampUnixMs = timestampUnixMs;
            lastPresentationTimestampSeconds = presentationSeconds;
            return true;
        }

        private bool TryGetCurrentParameters(out int width, out int height, out int frameRate, out int bitrateKbps)
        {
            width = 0;
            height = 0;
            frameRate = 0;
            bitrateKbps = 0;

            if (currentVideoFrameInput == null || !currentVideoFrameInput.isValid)
            {
                return false;
            }

            width = Mathf.Max(16, currentVideoFrameInput.outputResolution.x);
            height = Mathf.Max(16, currentVideoFrameInput.outputResolution.y);
            frameRate = Mathf.Clamp(currentVideoFrameInput.frameRate, 1, 120);
            bitrateKbps = Mathf.Max(128, currentVideoFrameInput.bitrateKbps);
            return true;
        }

        private string GetOutputDirectory()
        {
            return Path.Combine(Application.persistentDataPath, outputDirectoryName);
        }

        private void BlockFrame(string reason)
        {
            blockedFrameCount++;
            lastStatus = reason;
        }

        private void FailCurrentSession(string reason)
        {
            sessionHasFailure = true;
            sessionFailureReason = string.IsNullOrWhiteSpace(reason)
                ? "Local MP4 recording failed."
                : reason;
            lastStatus = sessionFailureReason;
            MarkMp4StateFailure(sessionFailureReason);
            recordingState?.Fail(sessionFailureReason);
            Debug.LogError(sessionFailureReason, this);
        }

        private void ResetSessionDiagnostics()
        {
            pushedFrameCount = 0;
            blockedFrameCount = 0;
            lastPushedSourceFrameId = -1;
            firstPushedSourceFrameId = -1;
            firstPushedTimestampUnixMs = 0;
            lastPushedTimestampUnixMs = 0;
            firstInputTimestampUnixMs = 0;
            lastPresentationTimestampUnixMs = 0;
            lastPresentationTimestampSeconds = -1.0;
            sessionHasFailure = false;
            sessionFailureReason = string.Empty;
        }

        private void DisposeSession()
        {
            CloseMetadataSidecar();
            session?.Dispose();
            session = null;
            frameProvider = null;
        }

        private void OpenMetadataSidecar()
        {
            if (!writeMetadataSidecar)
            {
                return;
            }

            metadataSidecarWriter = new StreamWriter(metadataSidecarPath, false);
        }

        private void CloseMetadataSidecar()
        {
            metadataSidecarWriter?.Flush();
            metadataSidecarWriter?.Dispose();
            metadataSidecarWriter = null;
        }

        private void WriteMetadataSidecarLine(long synchronizedSnapshotFrameId, long sourceFrameId, long timestampUnixMs)
        {
            if (metadataSidecarWriter == null || mergedSnapshotQueue == null)
            {
                return;
            }

            if (!TryFindMergedSnapshot(synchronizedSnapshotFrameId, sourceFrameId, timestampUnixMs, out var snapshot))
            {
                return;
            }

            metadataSidecarWriter.WriteLine(JsonUtility.ToJson(snapshot));
        }

        private bool TryFindMergedSnapshot(
            long synchronizedSnapshotFrameId,
            long sourceFrameId,
            long timestampUnixMs,
            out MergedFrameSnapshotRecord snapshot)
        {
            snapshot = default;
            var records = mergedSnapshotQueue.ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (synchronizedSnapshotFrameId >= 0 && records[i].frameId == synchronizedSnapshotFrameId)
                {
                    snapshot = records[i];
                    return true;
                }
            }

            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (records[i].frameId == sourceFrameId)
                {
                    snapshot = records[i];
                    return true;
                }
            }

            snapshot = mergedSnapshotQueue.GetDataAt(timestampUnixMs, 10);
            return snapshot.timestampUnixMs > 0;
        }

        private void WriteManifest()
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return;
            }

            var manifest = new LocalMp4ArtifactManifest
            {
                outputPath = outputPath,
                metadataSidecarPath = metadataSidecarPath,
                sourceFrameStartId = firstPushedSourceFrameId,
                sourceFrameEndId = lastPushedSourceFrameId,
                timestampStartUnixMs = firstPushedTimestampUnixMs,
                timestampEndUnixMs = lastPushedTimestampUnixMs,
                timebaseSource = TimebaseSource,
                mp4PtsTimebase = Mp4PtsTimebase,
                preserveInputFrameTimestamps = preserveInputFrameTimestamps,
                minimumInitialPresentationSeconds = MinimumInitialPresentationSeconds,
                frameCount = pushedFrameCount,
                width = currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.x : 0,
                height = currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.y : 0,
                frameRate = currentVideoFrameInput != null ? currentVideoFrameInput.frameRate : 0,
                codec = "MP4"
            };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        private void PublishFileArtifact()
        {
            if (!publishFileArtifact || outputBinder == null)
            {
                return;
            }

            outputBinder.PublishFileArtifact(
                outputPath,
                metadataSidecarPath,
                manifestPath,
                firstPushedSourceFrameId,
                lastPushedSourceFrameId,
                firstPushedTimestampUnixMs,
                lastPushedTimestampUnixMs,
                "MP4",
                currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.x : 0,
                currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.y : 0,
                currentVideoFrameInput != null ? currentVideoFrameInput.frameRate : 0);
        }

        private void OnRecordingException(Exception exception)
        {
            lastStatus = "InstantReplay exception: " + exception.Message;
            MarkMp4StateFailure(lastStatus);
            Debug.LogException(exception, this);
        }

        private void MarkMp4StateRecording()
        {
            if (mp4ArtifactWriterState == null)
            {
                return;
            }

            mp4ArtifactWriterState.isRecording = true;
            mp4ArtifactWriterState.finalized = false;
            mp4ArtifactWriterState.hasFailure = false;
            mp4ArtifactWriterState.sessionId = ResolveSessionId();
            mp4ArtifactWriterState.outputPath = outputPath ?? string.Empty;
            mp4ArtifactWriterState.metadataSidecarPath = metadataSidecarPath ?? string.Empty;
            mp4ArtifactWriterState.startedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            mp4ArtifactWriterState.finalizedUnixMs = 0;
            mp4ArtifactWriterState.byteLength = 0;
            mp4ArtifactWriterState.sampleCount = 0;
            mp4ArtifactWriterState.lastStatus = lastStatus;
            mp4ArtifactWriterState.lastFailureReason = string.Empty;
        }

        private void MarkMp4StateFinalizing()
        {
            if (mp4ArtifactWriterState == null)
            {
                return;
            }

            mp4ArtifactWriterState.isRecording = false;
            mp4ArtifactWriterState.lastStatus = lastStatus;
            mp4ArtifactWriterState.sampleCount = pushedFrameCount;
        }

        private void MarkMp4StateFinalized()
        {
            if (mp4ArtifactWriterState == null)
            {
                return;
            }

            mp4ArtifactWriterState.isRecording = false;
            mp4ArtifactWriterState.finalized = true;
            mp4ArtifactWriterState.hasFailure = sessionHasFailure;
            mp4ArtifactWriterState.sessionId = ResolveSessionId();
            mp4ArtifactWriterState.outputPath = outputPath ?? string.Empty;
            mp4ArtifactWriterState.metadataSidecarPath = metadataSidecarPath ?? string.Empty;
            mp4ArtifactWriterState.finalizedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            mp4ArtifactWriterState.byteLength = ResolveOutputByteLength();
            mp4ArtifactWriterState.sampleCount = pushedFrameCount;
            mp4ArtifactWriterState.lastStatus = lastStatus;
            mp4ArtifactWriterState.lastFailureReason = sessionHasFailure ? sessionFailureReason : string.Empty;
        }

        private void MarkMp4StateWaiting(string status)
        {
            if (mp4ArtifactWriterState == null)
            {
                return;
            }

            mp4ArtifactWriterState.isRecording = false;
            mp4ArtifactWriterState.finalized = false;
            mp4ArtifactWriterState.hasFailure = false;
            mp4ArtifactWriterState.sessionId = ResolveSessionId();
            mp4ArtifactWriterState.outputPath = outputPath ?? string.Empty;
            mp4ArtifactWriterState.metadataSidecarPath = metadataSidecarPath ?? string.Empty;
            mp4ArtifactWriterState.finalizedUnixMs = 0;
            mp4ArtifactWriterState.byteLength = ResolveOutputByteLength();
            mp4ArtifactWriterState.sampleCount = pushedFrameCount;
            mp4ArtifactWriterState.lastStatus = status;
            mp4ArtifactWriterState.lastFailureReason = string.Empty;
        }

        private void MarkMp4StateFailure(string reason)
        {
            if (mp4ArtifactWriterState == null)
            {
                return;
            }

            mp4ArtifactWriterState.isRecording = false;
            mp4ArtifactWriterState.finalized = false;
            mp4ArtifactWriterState.hasFailure = true;
            mp4ArtifactWriterState.sessionId = ResolveSessionId();
            mp4ArtifactWriterState.outputPath = outputPath ?? string.Empty;
            mp4ArtifactWriterState.metadataSidecarPath = metadataSidecarPath ?? string.Empty;
            if (mp4ArtifactWriterState.startedUnixMs == 0)
            {
                mp4ArtifactWriterState.startedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            mp4ArtifactWriterState.finalizedUnixMs = 0;
            mp4ArtifactWriterState.byteLength = ResolveOutputByteLength();
            mp4ArtifactWriterState.sampleCount = pushedFrameCount;
            mp4ArtifactWriterState.lastStatus = reason;
            mp4ArtifactWriterState.lastFailureReason = reason;
        }

        private long ResolveOutputByteLength()
        {
            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
            {
                return new FileInfo(outputPath).Length;
            }

            return 0;
        }

        private string ResolveSessionId()
        {
            long startUnixMs = firstPushedTimestampUnixMs > 0
                ? firstPushedTimestampUnixMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return "local_mp4_" + startUnixMs;
        }

        [Serializable]
        private sealed class LocalMp4ArtifactManifest
        {
            public string outputPath;
            public string metadataSidecarPath;
            public long sourceFrameStartId;
            public long sourceFrameEndId;
            public long timestampStartUnixMs;
            public long timestampEndUnixMs;
            public string timebaseSource;
            public string mp4PtsTimebase;
            public bool preserveInputFrameTimestamps;
            public double minimumInitialPresentationSeconds;
            public int frameCount;
            public int width;
            public int height;
            public int frameRate;
            public string codec;
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
