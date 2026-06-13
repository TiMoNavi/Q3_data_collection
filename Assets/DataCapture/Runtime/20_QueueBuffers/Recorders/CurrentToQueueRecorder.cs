using DataCapture.Synchronization;
using UnityEngine;

namespace SObasic.CurrentQueueBridge
{
    [DefaultExecutionOrder(90)]
    public class CurrentToQueueRecorder : MonoBehaviour
    {
        [Header("SO Bridge")]
        [SerializeField] private ScriptableObject currentSourceAsset;
        [SerializeField] private ScriptableObject queueSinkAsset;

        [Header("Recording")]
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private bool recordOnLateUpdate = true;
        [SerializeField] private bool recordOnlyNew = true;
        [SerializeField] private bool warnOnTypeMismatch = true;

        private long lastRecordedTimestampUnixMs = -1;
        private long lastRecordedSequence = long.MinValue;
        private bool hasWarnedTypeMismatch;

        private ICurrentRecordSource CurrentSource => currentSourceAsset as ICurrentRecordSource;
        private IRecordQueueSink QueueSink => queueSinkAsset as IRecordQueueSink;

        private void LateUpdate()
        {
            if (recordOnLateUpdate)
            {
                RecordCurrent();
            }
        }

        [ContextMenu("Record Current")]
        public bool RecordCurrent()
        {
            if (!ShouldWriteQueue())
            {
                return false;
            }

            ICurrentRecordSource source = CurrentSource;
            IRecordQueueSink sink = QueueSink;

            if (source == null || sink == null || !source.IsRecordValid)
            {
                return false;
            }

            if (!IsTypeCompatible(source, sink))
            {
                WarnTypeMismatchOnce(source, sink);
                FailRecordingSession(
                    "CurrentToQueueRecorder type mismatch. Source " + source.SourceName +
                    " produces " + source.RecordType.Name +
                    ", but queue " + sink.QueueName +
                    " accepts " + sink.RecordType.Name + ".");
                return false;
            }

            if (recordOnlyNew &&
                source.CurrentTimestampUnixMs == lastRecordedTimestampUnixMs &&
                source.RecordSequence == lastRecordedSequence)
            {
                return false;
            }

            if (!source.TryGetRecord(out ITimestampedData record) || record == null)
            {
                return false;
            }

            if (!sink.TryRecord(record))
            {
                return false;
            }

            lastRecordedTimestampUnixMs = source.CurrentTimestampUnixMs;
            lastRecordedSequence = source.RecordSequence;
            return true;
        }

        public void ResetRecordGuard()
        {
            lastRecordedTimestampUnixMs = -1;
            lastRecordedSequence = long.MinValue;
            hasWarnedTypeMismatch = false;
        }

        public void ClearQueue()
        {
            QueueSink?.ClearQueue();
            ResetRecordGuard();
        }

        private bool ShouldWriteQueue()
        {
            return recordingState != null && recordingState.ShouldWriteQueues;
        }

        private void FailRecordingSession(string reason)
        {
            if (recordingState != null && !recordingState.IsNotStarted)
            {
                recordingState.Fail(reason);
            }
        }

        private static bool IsTypeCompatible(ICurrentRecordSource source, IRecordQueueSink sink)
        {
            return source.RecordType == sink.RecordType;
        }

        private void WarnTypeMismatchOnce(ICurrentRecordSource source, IRecordQueueSink sink)
        {
            if (!warnOnTypeMismatch || hasWarnedTypeMismatch)
            {
                return;
            }

            hasWarnedTypeMismatch = true;
            Debug.LogWarning(
                "CurrentToQueueRecorder type mismatch. Source " + source.SourceName +
                " produces " + source.RecordType.Name +
                ", but queue " + sink.QueueName +
                " accepts " + sink.RecordType.Name + ".",
                this);
        }
    }
}
