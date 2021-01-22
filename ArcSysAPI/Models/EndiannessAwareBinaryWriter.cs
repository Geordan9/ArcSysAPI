using System;
using System.IO;
using System.Text;
using ArcSysAPI.Common.Enums;

namespace ArcSysAPI.Models
{
    public class EndiannessAwareBinaryWriter : BinaryWriter
    {
        public ByteOrder Endianness { get; private set; } = ByteOrder.LittleEndian;

        public EndiannessAwareBinaryWriter(Stream input) : base(input)
        {
        }

        public EndiannessAwareBinaryWriter(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        public EndiannessAwareBinaryWriter(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding,
            leaveOpen)
        {
        }

        public EndiannessAwareBinaryWriter(Stream input, ByteOrder endianness) : base(input)
        {
            Endianness = endianness;
        }

        public EndiannessAwareBinaryWriter(Stream input, Encoding encoding, ByteOrder endianness) : base(
            input, encoding)
        {
            Endianness = endianness;
        }

        public EndiannessAwareBinaryWriter(Stream input, Encoding encoding, bool leaveOpen,
            ByteOrder endianness) : base(input, encoding, leaveOpen)
        {
            Endianness = endianness;
        }

        public override void Write(byte[] buffer)
        {
            Write(Endianness, buffer);
        }

        public override void Write(short value)
        {
            Write(Endianness, value);
        }

        public override void Write(int value)
        {
            Write(Endianness, value);
        }

        public override void Write(long value)
        {
            Write(Endianness, value);
        }

        public override void Write(ushort value)
        {
            Write(Endianness, value);
        }

        public override void Write(uint value)
        {
            Write(Endianness, value);
        }

        public override void Write(ulong value)
        {
            Write(Endianness, value);
        }

        public void Write(ByteOrder endianness, byte[] buffer)
        {
            WriteWithEndianness(buffer, endianness);
        }

        public void Write(ByteOrder endianness, short value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void Write(ByteOrder endianness, int value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void Write(ByteOrder endianness, long value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void Write(ByteOrder endianness, ushort value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void Write(ByteOrder endianness, uint value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void Write(ByteOrder endianness, ulong value)
        {
            WriteWithEndianness(BitConverter.GetBytes(value), endianness);
        }

        public void ChangeEndianness(ByteOrder endianness)
        {
            Endianness = endianness;
        }

        private void WriteWithEndianness(byte[] buffer, ByteOrder endianness)
        {
            switch (endianness)
            {
                case ByteOrder.LittleEndian:
                    if (!BitConverter.IsLittleEndian) Array.Reverse(buffer);
                    break;

                case ByteOrder.BigEndian:
                    if (BitConverter.IsLittleEndian) Array.Reverse(buffer);
                    break;
            }

            base.Write(buffer);
        }
    }
}