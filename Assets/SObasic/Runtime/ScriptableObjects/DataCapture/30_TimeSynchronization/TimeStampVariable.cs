using UnityEngine;

namespace DataCapture.Synchronization
{
    /// <summary>
    /// ScriptableObject holding current timestamp.
    /// Updated by TimeStampService, read by all data sources.
    /// Decouples components from TimeStampService singleton.
    /// </summary>
    [CreateAssetMenu(fileName = "TimeStampVariable", menuName = "DataCapture/00 Global/Time Stamp Variable")]
    public class TimeStampVariable : ScriptableObject
    {
        [Header("Current Timestamp")]
        [Tooltip("Unix timestamp in milliseconds")]
        public long currentTimestamp;

        [Tooltip("Elapsed seconds since service started")]
        public double elapsedSeconds;
    }
}
