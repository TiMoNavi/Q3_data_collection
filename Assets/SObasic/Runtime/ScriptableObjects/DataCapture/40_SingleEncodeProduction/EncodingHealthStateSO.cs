using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "EncodingHealthStateSO", menuName = "DataCapture/40 Single Encode Production/Encoding Health State")]
    public class EncodingHealthStateSO : ScriptableObject, SObasic.IActiveState
    {
        public bool encoderInitialized;
        public bool inputTextureReady;
        public bool latestAccessUnitReady;
        public bool hasFailure;
        public string activeBlocker = "Encoder has not produced an access unit.";
        public long latestFrameId = -1;
        public long latestSourceTimestampUnixMs;
        public long latestEncodedPtsUs;
        public int encodedAccessUnitCount;
        public int droppedFrameCount;
        public string lastFailureReason;
        public long lastUpdatedUnixMs;

        public bool Active => encoderInitialized && inputTextureReady && latestAccessUnitReady && !hasFailure;

        public void MarkAccessUnit(long frameId, long sourceTimestampUnixMs, long encodedPtsUs)
        {
            encoderInitialized = true;
            inputTextureReady = true;
            latestAccessUnitReady = true;
            hasFailure = false;
            activeBlocker = string.Empty;
            latestFrameId = frameId;
            latestSourceTimestampUnixMs = sourceTimestampUnixMs;
            latestEncodedPtsUs = encodedPtsUs;
            encodedAccessUnitCount++;
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void MarkFailure(string reason)
        {
            hasFailure = true;
            latestAccessUnitReady = false;
            lastFailureReason = string.IsNullOrWhiteSpace(reason) ? "Encoding failed." : reason;
            activeBlocker = lastFailureReason;
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
