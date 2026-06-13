using UnityEngine;

namespace DataCapture.Networking
{
    public enum DataCaptureSessionMode
    {
        LocalOnly,
        NetworkOrHybrid
    }

    [CreateAssetMenu(fileName = "SessionModeSO", menuName = "DataCapture/00 Session Control/Session Mode")]
    public class SessionModeSO : ScriptableObject
    {
        public DataCaptureSessionMode mode = DataCaptureSessionMode.LocalOnly;
        public string modeLabel = "LocalOnly";
        public string lastBlocker;
        public long lastUpdatedUnixMs;

        public bool UsesNetwork => mode == DataCaptureSessionMode.NetworkOrHybrid;

        public void SetMode(DataCaptureSessionMode nextMode, string label = null)
        {
            mode = nextMode;
            modeLabel = string.IsNullOrWhiteSpace(label) ? nextMode.ToString() : label;
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
