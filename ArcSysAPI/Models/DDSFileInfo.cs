using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Components;
using ArcSysAPI.Utils;

namespace ArcSysAPI.Models
{
    public class DDSFileInfo : VirtualFileSystemInfo
    {
        public DDSFileInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
        }

        public DDSFileInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent, bool preCheck = true) :
            base(path, length,
                offset, parent, preCheck)
        {
        }

        public bool IsValidDDS => MagicBytes.Take(3).SequenceEqual(new byte[] {0x44, 0x44, 0x53});

        private void CheckEndianness(byte[] bytes)
        {
            if (Endianness == ByteOrder.LittleEndian)
                if (bytes[0] == 0x1)
                    Endianness = ByteOrder.BigEndian;
            endiannessChecked = true;
        }

        public Bitmap GetImage()
        {
            var initstream = GetReadStream();
            var reader =
                new EndiannessAwareBinaryReader(initstream, Encoding.Default, true, Endianness);
            try
            {
                var savpos = reader.BaseStream.Position;
                if (reader.ReadInt32(ByteOrder.LittleEndian) == 0x73676573)
                {
                    Endianness = ByteOrder.BigEndian;
                    reader.BaseStream.Seek(-4, SeekOrigin.Current);
                    reader.Close();
                    reader.Dispose();
                    reader = new EndiannessAwareBinaryReader(
                        SEGSCompression.DecompressStream(initstream, Endianness), Endianness);
                }
                else
                {
                    reader.BaseStream.Seek(12, SeekOrigin.Current);
                    if (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        if (reader.ReadInt32(ByteOrder.LittleEndian) == 0x73676573)
                        {
                            Endianness = ByteOrder.BigEndian;
                            reader.BaseStream.Seek(-4, SeekOrigin.Current);
                            reader.Close();
                            reader.Dispose();
                            reader = new EndiannessAwareBinaryReader(
                                SEGSCompression.DecompressStream(initstream, Endianness), Endianness);
                        }
                        else
                        {
                            reader.BaseStream.Seek(-20, SeekOrigin.Current);
                        }
                    }
                    else
                    {
                        reader.BaseStream.Position = savpos;
                    }
                }

                if (!endiannessChecked)
                {
                    CheckEndianness(reader.ReadBytes(4));
                    reader.ChangeEndianness(Endianness);
                    reader.BaseStream.Seek(-4, SeekOrigin.Current);
                }

                using (var memStream = new MemoryStream())
                {
                    reader.BaseStream.CopyTo(memStream);

                    var ddsImage = new DDSImage(memStream.ToArray(), Endianness);
                    if (ddsImage.BitmapImage == null)
                        return null;
                    return ddsImage.BitmapImage;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                reader.Close();
                reader.Dispose();
                initstream.Close();
                initstream.Dispose();
            }
        }
    }
}