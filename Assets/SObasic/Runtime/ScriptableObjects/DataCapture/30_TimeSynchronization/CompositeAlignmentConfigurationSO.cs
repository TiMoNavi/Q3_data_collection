using System;
using System.Collections.Generic;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CompositeAlignmentConfigurationSO", menuName = "DataCapture/40 Merged Synchronization/Composite Alignment Configuration")]
    public class CompositeAlignmentConfigurationSO : ScriptableObject
    {
        [Serializable]
        public class StreamEntry
        {
            public string streamName;
            public ScriptableObject queueAsset;
            public bool required = true;
        }

        [SerializeField] private List<StreamEntry> alignmentStreams = new List<StreamEntry>();

        public IReadOnlyList<StreamEntry> AlignmentStreams => alignmentStreams;

        public bool IsConfigured(ScriptableObject queueAsset)
        {
            return TryGetEntry(queueAsset, out _);
        }

        public bool IsRequired(ScriptableObject queueAsset, bool fallbackRequired)
        {
            return TryGetEntry(queueAsset, out StreamEntry entry)
                ? entry.required
                : fallbackRequired;
        }

        public bool HasAnyStreams => alignmentStreams != null && alignmentStreams.Count > 0;

        private bool TryGetEntry(ScriptableObject queueAsset, out StreamEntry entry)
        {
            entry = null;
            if (queueAsset == null || alignmentStreams == null)
            {
                return false;
            }

            foreach (StreamEntry candidate in alignmentStreams)
            {
                if (candidate != null && candidate.queueAsset == queueAsset)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
