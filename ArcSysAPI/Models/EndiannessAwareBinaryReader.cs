using System;
using System.IO;
using System.Text;
using ArcSysAPI.Common.Enums;

namespace ArcSysAPI.Models
{
    public class EndiannessAwareBinaryReader : BinaryReader
    {
        public EndiannessAwareBinaryReader(Stream input) : base(input)
        {
        }

        public EndiannessAwareBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        public EndiannessAwareBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding,
            leaveOpen)
        {
        }

        public EndiannessAwareBinaryReader(Stream input, ByteOrder endianness) : base(input)
        {
            Endianness = endianness;
        }

        public EndiannessAwareBinaryReader(Stream input, Encoding encoding, ByteOrder endianness) : base(
            input, encoding)
        {
            Endianness = endianness;
        }

        public EndiannessAwareBinaryReader(Stream input, Encoding encoding, bool leaveOpen,
            ByteOrder endianness) : base(input, encoding, leaveOpen)
        {
            Endianness = endianness;
        }

        public ByteOrder Endianness { get; private set; } = ByteOrder.LittleEndian;

        public override byte[] ReadBytes(int count)
        {
            return ReadBytes(count, Endianness);
        }

        public override short ReadInt16()
        {
            return ReadInt16(Endianness);
        }

        public override int ReadInt32()
        {
            return ReadInt32(Endianness);
        }

        public override long ReadInt64()
        {
            return ReadInt64(Endianness);
        }

        public override ushort ReadUInt16()
        {
            return ReadUInt16(Endianness);
        }

        public override uint ReadUInt32()
        {
            return ReadUInt32(Endianness);
        }

        public override ulong ReadUInt64()
        {
            return ReadUInt64(Endianness);
        }

        public byte[] ReadBytes(int count, ByteOrder endianness)
        {
            return ReadForEndianness(count, endianness);
        }

        public short ReadInt16(ByteOrder endianness)
        {
            return BitConverter.ToInt16(ReadForEndianness(sizeof(short), endianness), 0);
        }

        public int ReadInt32(ByteOrder endianness)
        {
            return BitConverter.ToInt32(ReadForEndianness(sizeof(int), endianness), 0);
        }

        public long ReadInt64(ByteOrder endianness)
        {
            return BitConverter.ToInt64(ReadForEndianness(sizeof(long), endianness), 0);
        }

        public ushort ReadUInt16(ByteOrder endianness)
        {
            return BitConverter.ToUInt16(ReadForEndianness(sizeof(ushort), endianness), 0);
        }

        public uint ReadUInt32(ByteOrder endianness)
        {
            return BitConverter.ToUInt32(ReadForEndianness(sizeof(uint), endianness), 0);
        }

        public ulong ReadUInt64(ByteOrder endianness)
        {
            return BitConverter.ToUInt64(ReadForEndianness(sizeof(ulong), endianness), 0);
        }

        public void ChangeEndianness(ByteOrder endianness)
        {
            Endianness = endianness;
        }

        private byte[] ReadForEndianness(int bytesToRead, ByteOrder endianness)
        {
            var bytesRead = base.ReadBytes(bytesToRead);

            switch (endianness)
            {
                case ByteOrder.LittleEndian:
                    if (!BitConverter.IsLittleEndian) Array.Reverse(bytesRead);
                    break;

                case ByteOrder.BigEndian:
                    if (BitConverter.IsLittleEndian) Array.Reverse(bytesRead);
                    break;
            }

            return bytesRead;
        }
    }
}