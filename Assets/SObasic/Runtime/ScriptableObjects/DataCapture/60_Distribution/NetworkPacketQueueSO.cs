using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "NetworkPacketQueueSO", menuName = "DataCapture/50 Encoding Network/Network Packet Queue")]
    public class NetworkPacketQueueSO : ScriptableObject, IRecordQueueSink
    {
        [SerializeField] private int capacity = 300;
        [SerializeField] private PacketTimestampHeader[] debugSnapshot = new PacketTimestampHeader[0];

        private PacketTimestampHeader[] buffer;
        private int head;
        private int count;

        public int Count => count;
        public PacketTimestampHeader[] DebugSnapshot => debugSnapshot;
        public string QueueName => name;
        public System.Type RecordType => typeof(NetworkPacketRecord);

        public void Record(PacketTimestampHeader header)
        {
            EnsureBuffer();
            buffer[head] = header;
            head = (head + 1) % buffer.Length;
            if (count < buffer.Length)
            {
                count++;
            }

            RefreshDebugSnapshot();
        }

        public void Clear()
        {
            EnsureBuffer();
            System.Array.Clear(buffer, 0, buffer.Length);
            head = 0;
            count = 0;
            RefreshDebugSnapshot();
        }

        public bool TryRecord(DataCapture.Synchronization.ITimestampedData record)
        {
            if (record is NetworkPacketRecord packetRecord)
            {
                Record(packetRecord.header);
                return true;
            }

            return false;
        }

        public void ClearQueue()
        {
            Clear();
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }

        private void EnsureBuffer()
        {
            if (buffer == null || buffer.Length != Mathf.Max(1, capacity))
            {
                buffer = new PacketTimestampHeader[Mathf.Max(1, capacity)];
                head = 0;
                count = 0;
            }
        }

        private void RefreshDebugSnapshot()
        {
            EnsureBuffer();
            debugSnapshot = new PacketTimestampHeader[count];
            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + buffer.Length) % buffer.Length;
                debugSnapshot[i] = buffer[index];
            }
        }
    }

    [System.Serializable]
    public struct NetworkPacketRecord : DataCapture.Synchronization.ITimestampedData
    {
        public PacketTimestampHeader header;

        public long Timestamp => header.timestampUnixMs;
    }
}
