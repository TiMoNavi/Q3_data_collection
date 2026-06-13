using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "EncodedOutputBindingConfigurationSO", menuName = "DataCapture/50 Encoding Network/Encoded Output Binding Configuration")]
    public class EncodedOutputBindingConfigurationSO : ScriptableObject
    {
        [Header("Metadata Matching")]
        public bool matchByFrameIdFirst = true;
        public long maxTimestampDeltaMs = 10;
        public bool requireSendableMergedSnapshot = true;
        public bool allowPartialDebugSnapshot;

        [Header("Failure Policy")]
        public bool publishFailedRecordsForDiagnostics = true;
        public bool failRecordingOnMissingRequiredMetadata = true;

        [Header("Payload")]
        public bool allowMemoryPayloads = true;
        public int maxMemoryPayloadBytes = 2 * 1024 * 1024;

        private void OnValidate()
        {
            if (maxTimestampDeltaMs < 0)
            {
                maxTimestampDeltaMs = 0;
            }

            maxMemoryPayloadBytes = Mathf.Max(0, maxMemoryPayloadBytes);
        }
    }
}
