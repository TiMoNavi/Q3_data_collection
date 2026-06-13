using System;
using UnityEngine;

namespace DataCapture.Testing
{
    [CreateAssetMenu(fileName = "SORegistryListRequestSO", menuName = "DataCapture/90 Diagnostics/SO Registry List Request")]
    public class SORegistryListRequestSO : ScriptableObject
    {
        [Header("Request")]
        public bool requested;
        public int requestRevision;
        public long requestedAtUnixMs;
        public string requestSource = string.Empty;
        public bool includeTypeNames = true;
        public bool includeAssetPaths = true;

        [Header("Result")]
        public bool lastSucceeded;
        public int lastListCount;
        public long lastListedAtUnixMs;
        public string lastStatusMessage = string.Empty;
        [TextArea(8, 30)] public string lastListText = string.Empty;
        public string[] recentEntries = new string[64];

        public void Request(string source)
        {
            requested = true;
            requestRevision++;
            requestedAtUnixMs = Now();
            requestSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            lastSucceeded = false;
            lastStatusMessage = "Pending";
        }

        public void Clear()
        {
            requested = false;
        }

        public void MarkResult(bool succeeded, int count, string listText, string message)
        {
            requested = false;
            lastSucceeded = succeeded;
            lastListCount = count;
            lastListedAtUnixMs = Now();
            lastListText = listText ?? string.Empty;
            lastStatusMessage = message ?? string.Empty;
            UpdateRecentEntries(lastListText);
        }

        private void UpdateRecentEntries(string listText)
        {
            if (recentEntries == null || recentEntries.Length == 0)
            {
                recentEntries = new string[64];
            }

            Array.Clear(recentEntries, 0, recentEntries.Length);
            if (string.IsNullOrWhiteSpace(listText))
            {
                return;
            }

            string[] lines = listText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int count = Mathf.Min(recentEntries.Length, lines.Length);
            for (int i = 0; i < count; i++)
            {
                recentEntries[i] = lines[i];
            }
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
