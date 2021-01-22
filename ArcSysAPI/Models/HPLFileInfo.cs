using System.Drawing;
using System.IO;
using System.Linq;
using ArcSysAPI.Common.Enums;

namespace ArcSysAPI.Models
{
    public class HPLFileInfo : VirtualFileInfo
    {
        public HPLFileInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
        }

        public HPLFileInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent, bool preCheck = true) :
            base(path, length,
                offset, parent, preCheck)
        {
        }

        public uint ColorRange { get; set; }

        public Color[] Palette => GetPalette();

        public bool IsValidHPL => MagicBytes.SequenceEqual(new byte[] {0x48, 0x50, 0x41, 0x4C});

        private void CheckEndianness(byte[] bytes)
        {
            if (Endianness == ByteOrder.LittleEndian)
                if (bytes[0] == 0x0)
                    Endianness = ByteOrder.BigEndian;
            endiannessChecked = true;
        }

        private Color[] GetPalette()
        {
            using (var reader = new EndiannessAwareBinaryReader(GetReadStream(), Endianness))
            {
                MagicBytes = reader.ReadBytes(4, ByteOrder.LittleEndian);

                if (!IsValidHPL)
                    return null;

                if (!endiannessChecked)
                {
                    CheckEndianness(reader.ReadBytes(4));
                    reader.ChangeEndianness(Endianness);
                }
                else
                {
                    reader.BaseStream.Seek(4, SeekOrigin.Current);
                }

                FileLength = reader.ReadUInt32();
                ColorRange = reader.ReadUInt32();
                reader.BaseStream.Seek(16, SeekOrigin.Current);

                var colors = new Color[ColorRange];

                for (var i = 0; i < colors.Length; i++)
                {
                    var bytes = reader.ReadBytes(4);
                    colors[i] = Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
                }

                return colors;
            }
        }
    }
}