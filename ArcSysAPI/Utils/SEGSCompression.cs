using System.IO;
using System.IO.Compression;
using System.Text;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Models;

namespace ArcSysAPI.Utils
{
    public static class SEGSCompression
    {
        public static Stream DecompressStream(Stream stream, ByteOrder endianness)
        {
            using (var reader = new EndiannessAwareBinaryReader(stream, Encoding.Default, true, endianness))
            {
                var beginPos = reader.BaseStream.Position;
                var idstring = reader.ReadChars(4);
                var flags = reader.ReadInt16();
                var chunks = reader.ReadInt16();
                var fullSize = reader.ReadUInt32();
                var fullCompressedSize = reader.ReadUInt32();

                var pos = beginPos + chunks * (2 + 2 + 4);
                var workAround = 0;

                var decompressStream = new MemoryStream(new byte[fullSize]);

                for (var i = 0; i < chunks; i++)
                {
                    var zsize = (int) reader.ReadUInt16();
                    var size = (int) reader.ReadUInt16();
                    var offset = (long) reader.ReadUInt32() - 1;

                    if (i == 0)
                        if (offset == 0)
                            workAround = 1;

                    if (workAround != 0) offset += pos;

                    if (size == 0) size = 0x00010000;

                    if (size == zsize)
                    {
                        var savPos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                        decompressStream.Write(reader.ReadBytes(size), 0, size);
                        reader.BaseStream.Seek(savPos, SeekOrigin.Begin);
                    }
                    else
                    {
                        var savPos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(beginPos + offset, SeekOrigin.Begin);
                        using (var decodeStream =
                            Decompress(new MemoryStream(reader.ReadBytes(zsize, ByteOrder.LittleEndian)), flags, zsize,
                                size))
                        {
                            decodeStream.CopyTo(decompressStream);
                        }

                        reader.BaseStream.Seek(savPos, SeekOrigin.Begin);
                    }
                }

                decompressStream.Position = 0;

                return decompressStream;
            }
        }

        private static Stream Decompress(MemoryStream stream, short flags, int zsize, int size)
        {
            /*if (BinaryTools.IsBitSet((byte) (flags >> 8), 0))
            {
                // LZMA Possibly
            }*/

            using (Stream input = new DeflateStream(stream,
                CompressionMode.Decompress))
            {
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return new MemoryStream(output.ToArray());
                }
            }
        }
    }
}