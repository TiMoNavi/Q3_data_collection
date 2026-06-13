using System;
using System.Collections.Generic;

namespace DataCapture.Synchronization
{
    /// <summary>
    /// Generic ring buffer for timestamped data.
    /// Fixed-size circular buffer with time-based queries.
    /// </summary>
    public class RingBuffer<T> where T : ITimestampedData
    {
        private readonly T[] buffer;
        private readonly int capacity;
        private int head;
        private int count;
        private long overwriteCount;
        private long lastClearTimestamp;
        private long generationId;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Ring buffer capacity must be greater than zero.");
            }

            this.capacity = capacity;
            buffer = new T[capacity];
            head = 0;
            count = 0;
            overwriteCount = 0;
            lastClearTimestamp = 0;
            generationId = 0;
        }

        public void Add(T data)
        {
            if (count == capacity)
            {
                overwriteCount++;
            }

            buffer[head] = data;
            head = (head + 1) % capacity;
            if (count < capacity) count++;
        }

        public T GetNearest(long timestamp, long tolerance)
        {
            T nearest = default;
            long minDiff = long.MaxValue;

            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity) % capacity;
                T data = buffer[index];
                long diff = Math.Abs(data.Timestamp - timestamp);

                if (diff <= tolerance && diff < minDiff)
                {
                    minDiff = diff;
                    nearest = data;
                }
            }

            return nearest;
        }

        public List<T> GetInRange(long startTime, long endTime)
        {
            var result = new List<T>();

            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity) % capacity;
                T data = buffer[index];

                if (data.Timestamp >= startTime && data.Timestamp <= endTime)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        public T[] ToArray()
        {
            var result = new T[count];

            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity) % capacity;
                result[i] = buffer[index];
            }

            return result;
        }

        public void Clear()
        {
            Array.Clear(buffer, 0, buffer.Length);
            head = 0;
            count = 0;
            overwriteCount = 0;
            lastClearTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            generationId++;
        }

        public int Capacity => capacity;
        public int Count => count;
        public bool IsFull => count == capacity;
        public long OverwriteCount => overwriteCount;
        public long LastClearTimestamp => lastClearTimestamp;
        public long GenerationId => generationId;
        public long OldestTimestamp => count > 0 ? buffer[(head - count + capacity) % capacity].Timestamp : 0;
        public long NewestTimestamp => count > 0 ? buffer[(head - 1 + capacity) % capacity].Timestamp : 0;
    }
}
