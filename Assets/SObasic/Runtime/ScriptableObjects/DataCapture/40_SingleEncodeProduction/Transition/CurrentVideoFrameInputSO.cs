using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CurrentVideoFrameInputSO", menuName = "DataCapture/50 Encoding Network/Current Video Frame Input")]
    public class CurrentVideoFrameInputSO : ScriptableObject
    {
        [Header("Texture")]
        public Texture inputTexture;
        public VideoFrameInputSourceKind sourceKind;
        public bool isValid;

        [Header("Source Frame")]
        public long sourceCameraFrameId = -1;
        [Tooltip("Original timestamp carried by the matched camera image source before Stage 3 synchronization.")]
        public long sourceTimestampUnixMs;
        [Tooltip("Stage 3 MergedFrameSnapshotRecord.frameId that selected this video input. -1 means legacy current fallback.")]
        public long synchronizedSnapshotFrameId = -1;
        [Tooltip("Synchronized Stage 3 timestamp carried by MergedFrameSnapshotRecord.timestampUnixMs. Local MP4 PTS is derived from this value relative to the first pushed frame.")]
        public long timestampUnixMs;
        public Vector2Int sourceResolution;

        [Header("Resolved Encoding Parameters")]
        public Vector2Int outputResolution;
        public int frameRate;
        public int bitrateKbps;
        public int qualityPercent;
        public string codec;
        public string resolutionSource;
        public string frameRateSource;
        public string bitrateSource;

        public CameraImageFrameRecord ToSourceFrameRecord()
        {
            return new CameraImageFrameRecord
            {
                frameId = sourceCameraFrameId,
                timestampUnixMs = timestampUnixMs,
                resolution = sourceResolution,
                encodedFrameId = -1,
                debugImagePath = string.Empty
            };
        }

        public void SetFrame(
            Texture texture,
            VideoFrameInputSourceKind frameSourceKind,
            CameraImageFrameRecord sourceFrame,
            long synchronizedTimestampUnixMs,
            long synchronizedFrameId,
            Vector2Int sourceFrameResolution,
            ResolvedVideoEncodingParameters parameters)
        {
            inputTexture = texture;
            sourceKind = frameSourceKind;
            sourceCameraFrameId = sourceFrame.frameId;
            sourceTimestampUnixMs = sourceFrame.timestampUnixMs;
            synchronizedSnapshotFrameId = synchronizedFrameId;
            timestampUnixMs = synchronizedTimestampUnixMs;
            sourceResolution = sourceFrameResolution;
            outputResolution = new Vector2Int(parameters.width, parameters.height);
            frameRate = parameters.frameRate;
            bitrateKbps = parameters.bitrateKbps;
            qualityPercent = parameters.qualityPercent;
            codec = parameters.codec;
            resolutionSource = parameters.resolutionSource;
            frameRateSource = parameters.frameRateSource;
            bitrateSource = parameters.bitrateSource;
            isValid = texture != null && sourceCameraFrameId >= 0 && timestampUnixMs > 0;
        }

        public void Clear()
        {
            inputTexture = null;
            sourceKind = VideoFrameInputSourceKind.RawCameraImage;
            isValid = false;
            sourceCameraFrameId = -1;
            sourceTimestampUnixMs = 0;
            synchronizedSnapshotFrameId = -1;
            timestampUnixMs = 0;
            sourceResolution = default;
            outputResolution = default;
            frameRate = 0;
            bitrateKbps = 0;
            qualityPercent = 0;
            codec = string.Empty;
            resolutionSource = string.Empty;
            frameRateSource = string.Empty;
            bitrateSource = string.Empty;
        }
    }
}
