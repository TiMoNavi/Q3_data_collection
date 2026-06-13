using System;
using System.Diagnostics;
using UnityEngine;

namespace DataCapture.Synchronization
{
    /// <summary>
    /// Centralized high-precision timestamp service.
    /// Writes to TimeStampVariable SO for decoupled access.
    /// </summary>
    public class TimeStampService : MonoBehaviour
    {
        [SerializeField] private TimeStampVariable timestampVariable;

        private static TimeStampService _instance;
        public static TimeStampService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("TimeStampService");
                    _instance = go.AddComponent<TimeStampService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private Stopwatch stopwatch;
        private long startTimestamp;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            stopwatch = Stopwatch.StartNew();
            startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void Update()
        {
            if (timestampVariable != null)
            {
                timestampVariable.currentTimestamp = GetTimestamp();
                timestampVariable.elapsedSeconds = GetElapsedSeconds();
            }
        }

        /// <summary>
        /// Get current timestamp in milliseconds (Unix time).
        /// </summary>
        public long GetTimestamp()
        {
            return startTimestamp + stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// Get high-precision elapsed time since service started (seconds).
        /// </summary>
        public double GetElapsedSeconds()
        {
            return stopwatch.Elapsed.TotalSeconds;
        }
    }
}
