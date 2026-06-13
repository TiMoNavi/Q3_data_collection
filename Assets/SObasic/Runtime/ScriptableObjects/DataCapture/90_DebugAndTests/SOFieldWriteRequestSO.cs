using UnityEngine;

namespace DataCapture.Testing
{
    public enum SOFieldWriteValueType
    {
        String,
        Bool,
        Int,
        Long,
        Float,
        Vector2,
        Vector3,
        Enum
    }

    [CreateAssetMenu(fileName = "SOFieldWriteRequestSO", menuName = "DataCapture/90 Diagnostics/SO Field Write Request")]
    public class SOFieldWriteRequestSO : ScriptableObject
    {
        public bool requested;
        public int requestRevision;
        public long requestedAtUnixMs;
        public string requestSource = string.Empty;

        [Header("Target")]
        public ScriptableObject target;
        public string targetName = string.Empty;
        public string fieldPath = string.Empty;

        [Header("Value")]
        public SOFieldWriteValueType valueType;
        public string stringValue = string.Empty;
        public bool boolValue;
        public int intValue;
        public long longValue;
        public float floatValue;
        public Vector2 vector2Value;
        public Vector3 vector3Value;

        [Header("Result")]
        public bool lastApplySucceeded;
        public long lastAppliedAtUnixMs;
        public string lastStatusMessage = string.Empty;

        public void Request(string source)
        {
            requested = true;
            requestRevision++;
            requestedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            requestSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            lastApplySucceeded = false;
            lastStatusMessage = "Pending";
        }

        public void Clear()
        {
            requested = false;
        }

        public void MarkApplied(string message)
        {
            requested = false;
            lastApplySucceeded = true;
            lastAppliedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastStatusMessage = message ?? string.Empty;
        }

        public void MarkFailed(string message)
        {
            lastApplySucceeded = false;
            lastAppliedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastStatusMessage = message ?? string.Empty;
        }
    }
}
