using UnityEngine;

namespace DataCapture.Diagnostics
{
    [CreateAssetMenu(fileName = "QueueDebugStateSO", menuName = "DataCapture/90 Diagnostics/Queue Debug State")]
    public class QueueDebugStateSO : ScriptableObject
    {
        public string queueName;
        public int count;
        public int capacity;
        public long oldestTimestampUnixMs;
        public long latestTimestampUnixMs;
        public long overwriteCount;
        public long lastClearTimestampUnixMs;
        public long generationId;
        public bool isHealthy = true;
        public string statusMessage;

        [Header("Runtime Diagnostics")]
        public bool logHealthChangesToUnity = true;
        public string lastDebugMessage;
        public long lastChangedUnixMs;
        public int healthChangeCount;
        public string[] recentDebugEvents = new string[12];

        public void SetState(
            string queueName,
            int count,
            int capacity,
            long oldestTimestampUnixMs,
            long latestTimestampUnixMs,
            long overwriteCount,
            long lastClearTimestampUnixMs,
            long generationId,
            bool isHealthy,
            string statusMessage)
        {
            bool previousHealthy = this.isHealthy;
            bool overwriteStarted = this.overwriteCount == 0 && overwriteCount > 0;
            string previousMessage = this.statusMessage;

            this.queueName = queueName;
            this.count = count;
            this.capacity = capacity;
            this.oldestTimestampUnixMs = oldestTimestampUnixMs;
            this.latestTimestampUnixMs = latestTimestampUnixMs;
            this.overwriteCount = overwriteCount;
            this.lastClearTimestampUnixMs = lastClearTimestampUnixMs;
            this.generationId = generationId;
            this.isHealthy = isHealthy;
            this.statusMessage = statusMessage;

            if (previousHealthy != isHealthy || overwriteStarted)
            {
                string message =
                    "QueueDebug " + this.queueName +
                    " healthy=" + this.isHealthy +
                    " count=" + this.count +
                    "/" + this.capacity +
                    " overwriteCount=" + this.overwriteCount;

                if (!string.IsNullOrWhiteSpace(this.statusMessage))
                {
                    message += " message=" + this.statusMessage;
                }

                AppendDebugEvent(message, !this.isHealthy);
            }
            else if (!this.isHealthy && previousMessage != this.statusMessage)
            {
                AppendDebugEvent("QueueDebug " + this.queueName + " message=" + this.statusMessage, true);
            }
        }

        [ContextMenu("Log Runtime Diagnostics")]
        public void LogRuntimeDiagnostics()
        {
            Debug.Log(
                "QueueDebug diagnostics: queue=" + queueName +
                " healthy=" + isHealthy +
                " count=" + count +
                "/" + capacity +
                " overwriteCount=" + overwriteCount +
                " status=" + statusMessage +
                " lastDebug=" + lastDebugMessage,
                this);
        }

        private void AppendDebugEvent(string message, bool warning)
        {
            lastDebugMessage = message;
            lastChangedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            healthChangeCount++;

            if (recentDebugEvents == null || recentDebugEvents.Length == 0)
            {
                recentDebugEvents = new string[12];
            }

            for (int i = recentDebugEvents.Length - 1; i > 0; i--)
            {
                recentDebugEvents[i] = recentDebugEvents[i - 1];
            }

            recentDebugEvents[0] = lastChangedUnixMs + " " + message;

            if (!logHealthChangesToUnity)
            {
                return;
            }

            if (warning)
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.Log(message, this);
            }
        }
    }
}
