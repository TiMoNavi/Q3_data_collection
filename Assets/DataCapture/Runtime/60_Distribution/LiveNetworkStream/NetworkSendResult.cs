namespace DataCapture.Networking
{
    public enum NetworkSendOutcome
    {
        Sent,
        Skipped,
        Failed
    }

    [System.Serializable]
    public struct NetworkSendResult
    {
        public NetworkSendOutcome outcome;
        public string reason;
        public int byteLength;

        public bool Sent => outcome == NetworkSendOutcome.Sent;
        public bool Completed => outcome == NetworkSendOutcome.Sent || outcome == NetworkSendOutcome.Skipped;

        public static NetworkSendResult SentBytes(int byteLength)
        {
            return new NetworkSendResult
            {
                outcome = NetworkSendOutcome.Sent,
                reason = string.Empty,
                byteLength = byteLength
            };
        }

        public static NetworkSendResult Skipped(string reason)
        {
            return new NetworkSendResult
            {
                outcome = NetworkSendOutcome.Skipped,
                reason = reason ?? string.Empty,
                byteLength = 0
            };
        }

        public static NetworkSendResult Failed(string reason)
        {
            return new NetworkSendResult
            {
                outcome = NetworkSendOutcome.Failed,
                reason = reason ?? string.Empty,
                byteLength = 0
            };
        }
    }
}
