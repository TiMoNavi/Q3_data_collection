using System;
using System.Text;
using UnityEngine;

namespace DataCapture.Networking
{
    public static class NetworkPacketEnvelope
    {
        public const string BinaryMagic = "Q3DCBIN1";
        private const int MagicByteLength = 8;
        private const int HeaderLengthByteCount = 4;

        public static byte[] PackJsonHeader<THeader>(THeader header, byte[] payload)
        {
            byte[] headerBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(header));
            byte[] payloadBytes = payload ?? Array.Empty<byte>();
            byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);

            int offset = 0;
            byte[] packet = new byte[MagicByteLength + HeaderLengthByteCount + headerBytes.Length + payloadBytes.Length];
            Encoding.ASCII.GetBytes(BinaryMagic, 0, BinaryMagic.Length, packet, offset);
            offset += MagicByteLength;
            Buffer.BlockCopy(headerLengthBytes, 0, packet, offset, HeaderLengthByteCount);
            offset += HeaderLengthByteCount;
            Buffer.BlockCopy(headerBytes, 0, packet, offset, headerBytes.Length);
            offset += headerBytes.Length;
            Buffer.BlockCopy(payloadBytes, 0, packet, offset, payloadBytes.Length);
            return packet;
        }
    }
}
