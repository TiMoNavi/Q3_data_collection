using DataCapture.Diagnostics;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Diagnostics
{
    public class QueueDebugReporter : MonoBehaviour
    {
        [SerializeField] private ScriptableObject queueAsset;
        [SerializeField] private QueueDebugStateSO debugState;
        [SerializeField] private int expectedCapacity = 0;
        [SerializeField] private bool updateEveryFrame = true;

        private IRecordQueueSink Queue => queueAsset as IRecordQueueSink;

        private void Update()
        {
            if (updateEveryFrame)
            {
                UpdateDebugState();
            }
        }

        [ContextMenu("Update Debug State")]
        public void UpdateDebugState()
        {
            if (debugState == null)
            {
                return;
            }

            IRecordQueueSink queue = Queue;
            if (queue == null)
            {
                debugState.SetState("None", 0, expectedCapacity, 0, 0, 0, 0, 0, false, "Queue asset does not implement IRecordQueueSink.");
                return;
            }

            IQueueHealth health = queueAsset as IQueueHealth;
            int capacity = health != null ? health.Capacity : Mathf.Max(1, expectedCapacity);
            int effectiveExpectedCapacity = expectedCapacity > 0 ? expectedCapacity : capacity;
            bool healthy = queue.Count <= effectiveExpectedCapacity;
            long oldestTimestamp = health != null ? health.OldestTimestamp : 0;
            long newestTimestamp = health != null ? health.NewestTimestamp : 0;
            long overwriteCount = health != null ? health.OverwriteCount : 0;
            long lastClearTimestamp = health != null ? health.LastClearTimestamp : 0;
            long generationId = health != null ? health.GenerationId : 0;
            string message = healthy ? "OK" : "Queue count exceeds expected capacity.";
            if (overwriteCount > 0)
            {
                message = "Queue has overwritten old records. OverwriteCount=" + overwriteCount;
            }

            debugState.SetState(
                queue.QueueName,
                queue.Count,
                capacity,
                oldestTimestamp,
                newestTimestamp,
                overwriteCount,
                lastClearTimestamp,
                generationId,
                healthy,
                message);
        }

        private void OnValidate()
        {
            expectedCapacity = Mathf.Max(0, expectedCapacity);
        }
    }
}
