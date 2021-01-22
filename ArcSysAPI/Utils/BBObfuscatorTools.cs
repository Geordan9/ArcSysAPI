using System;
using System.IO;
using System.IO.Compression;

namespace ArcSysAPI.Utils
{
    public static class BBObfuscatorTools
    {
        public static MemoryStream FPACDecryptStream(Stream stream, string path, bool onlyHeader = false)
        {
            stream.Position = 0;
            var ms = new MemoryStream();

            var fileName = Path.GetFileName(path).ToUpperInvariant();
            var fileSize = stream.Length;

            uint decryptInitKey = 0x0;
            foreach (var c in fileName)
            {
                decryptInitKey *= 0x89;
                decryptInitKey += Convert.ToByte(c);
            }

            var decryptKey = new uint[0x270];
            decryptKey[0] = decryptInitKey;

            for (uint i = 1; i < decryptKey.Length; i++)
            {
                var val = decryptKey[i - 1];
                var val2 = val >> 0x1E;
                val2 ^= val;
                val2 *= 0x6C078965;
                val2 += i;
                decryptKey[i] = val2;
            }

            uint repeatVal = 1;
            uint xorVal = 0x43415046;

            uint value;

            uint decryptIndex = 0;

            var size = onlyHeader && fileSize >= 40 ? 40 : fileSize;

            for (var byteIndex = 0; byteIndex < size; byteIndex += 4)
            {
                --repeatVal;
                if (repeatVal == 0)
                {
                    decryptIndex = 0;

                    uint index = 0;

                    repeatVal = 0x270;

                    for (uint i = 0xE3; i > 0; i--)
                    {
                        var val = decryptKey[index + 1];
                        var value2 = decryptKey[index];
                        value2 ^= val;
                        value2 &= 0x7FFFFFFE;
                        value2 ^= decryptKey[index];
                        val &= 1;
                        value2 >>= 1;
                        val = (uint) (val * -1);
                        val &= 0x9908B0DF;
                        value2 ^= val;
                        value2 ^= decryptKey[index + 0x18D];
                        decryptKey[index] = value2;
                        index++;
                    }

                    for (uint i = 0x18C; i > 0; i--)
                    {
                        var val = decryptKey[index + 1];
                        var value2 = decryptKey[index];
                        value2 ^= val;
                        value2 &= 0x7FFFFFFE;
                        value2 ^= decryptKey[index];
                        val &= 1;
                        value2 >>= 1;
                        val = (uint) (val * -1);
                        val &= 0x9908B0DF;
                        value2 ^= val;
                        value2 ^= decryptKey[index - 0xE3];
                        decryptKey[index] = value2;
                        index++;
                    }

                    var valOne = decryptKey[0];
                    var valLast = decryptKey[index];
                    valLast ^= valOne;
                    valLast &= 0x7FFFFFFE;
                    valLast ^= decryptKey[index];
                    valOne &= 1;
                    valLast >>= 1;
                    valOne = (uint) (valOne * -1);
                    valOne &= 0x9908B0DF;
                    valLast ^= valOne;
                    valLast ^= decryptKey[index - 0xE3];
                    decryptKey[index] = valLast;
                }

                value = decryptKey[decryptIndex];
                decryptIndex++;
                var val2 = value;
                val2 >>= 0x0B;
                value ^= val2;
                val2 = value;
                val2 &= 0xFF3A58AD;
                val2 <<= 0x07;
                value ^= val2;
                val2 = value;
                val2 &= 0xFFFFDF8C;
                val2 <<= 0x0F;
                value ^= val2;
                val2 = value;
                val2 >>= 0x12;
                value ^= val2;

                var tmpBytes = new byte[4];
                stream.Read(tmpBytes, 0, 4);
                value ^= BitConverter.ToUInt32(tmpBytes, 0);

                value ^= xorVal;
                var k = 0;
                foreach (var b in BitConverter.GetBytes(value))
                {
                    ms.WriteByte(b);
                    k++;
                }

                xorVal = value;

                if (byteIndex == 0)
                {
                    if (xorVal != 0x43415046 && xorVal != 0x53414644)
                    {
                        ms.Close();
                        ms.Dispose();
                        ms = new MemoryStream();
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        return ms;
                    }

                    if (xorVal == 0x53414644)
                        size = fileSize;
                }
            }

            ms.Position = 0;
            return ms;
        }

        public static MemoryStream DFASFPACInflateStream(Stream ms)
        {
            ms.Seek(12, SeekOrigin.Current);
            var bytes = new byte[4];
            ms.Read(bytes, 0, 4);
            var compressByteSize = BitConverter.ToInt32(bytes, 0);
            var fileBytes = new byte[compressByteSize];
            ms.Seek(2, SeekOrigin.Current);
            ms.Read(fileBytes, 0, compressByteSize);
            ms.Position = 0;
            return Inflate(new MemoryStream(fileBytes));
        }

        private static MemoryStream Inflate(MemoryStream ms)
        {
            using (Stream input = new DeflateStream(ms,
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