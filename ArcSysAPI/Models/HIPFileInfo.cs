using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Utils;
using GeoArcSysCryptTool.Utils.Extensions;

namespace ArcSysAPI.Models
{
    public enum HIPEncoding
    {
        RawRepeat = 0x1,
        Key = 0x2,
        Raw = 0x8,
        RawSignRepeat = 0x10,
        RawCanvas = 0x20
    }

    public enum HIPFormats
    {
        Indexed8 = 0x32,
        Bgra32 = 0x32,
        Gray16 = 0x16,
        Bgr565 = 0x16
    }

    public class HIPFileInfo : VirtualFileInfo
    {
        private Color[] palette;

        public HIPFileInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
            var ext = Path.GetExtension(path).ToLower();
            if (ImageTools.nativeImageExtensions.Contains(ext) || ext == ".dds")
                CreateHIP(path);
            else
                InitGetHeader();
        }

        public HIPFileInfo(string path, HIPEncoding hipEncoding, bool layeredImage = false, int offsetX = 0,
            int offsetY = 0, int canvasWidth = 0, int canvasHeight = 0) : base(path)
        {
            var ext = Path.GetExtension(path).ToLower();
            if (ImageTools.nativeImageExtensions.Contains(ext) || ext == ".dds")
                CreateHIP(path, hipEncoding, layeredImage, offsetX, offsetY, canvasWidth, canvasHeight);
        }

        public HIPFileInfo(string path, HIPEncoding hipEncoding, ref HIPFileInfo refHIPFileInfo) : base(path)
        {
            var ext = Path.GetExtension(path).ToLower();
            if (ImageTools.nativeImageExtensions.Contains(ext) || ext == ".dds")
                CreateHIP(path, hipEncoding, Convert.ToBoolean(refHIPFileInfo.EncodingParams[2]),
                    refHIPFileInfo.OffsetX, refHIPFileInfo.OffsetY, refHIPFileInfo.CanvasWidth,
                    refHIPFileInfo.CanvasHeight);
        }

        public HIPFileInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent, bool preCheck = true) :
            base(path, length,
                offset, parent, preCheck)
        {
            InitGetHeader();
        }

        // Properties

        private uint HeaderSize { get; set; }

        private uint ColorRange { get; set; }

        public int CanvasWidth { get; private set; }

        public int CanvasHeight { get; private set; }

        public byte[] EncodingParams { get; set; }

        public int ImageWidth { get; private set; }

        public int ImageHeight { get; private set; }

        public Color[] Palette
        {
            get
            {
                if (palette != null)
                    return palette;

                return GetPalette();
            }
            private set => palette = value;
        }

        public int OffsetX { get; private set; }

        public int OffsetY { get; private set; }

        public PixelFormat PixelFormat { get; private set; } = PixelFormat.Format32bppArgb;

        public HIPEncoding Encoding { get; private set; } = HIPEncoding.Raw;

        public bool MissingPalette { get; private set; }

        public bool IsValidHIP => MagicBytes.SequenceEqual(new byte[] {0x48, 0x49, 0x50, 0x00});

        // Methods

        private void CheckEndianness(byte[] bytes)
        {
            if (Endianness == ByteOrder.LittleEndian)
                if (bytes[0] == 0x0)
                    Endianness = ByteOrder.BigEndian;
            endiannessChecked = true;
        }

        private void InitGetHeader()
        {
            var stream = GetReadStream(true);
            if (stream == null)
                return;
            using (stream)
            {
                if (FileLength < 32)
                    return;

                try
                {
                    using (var reader = new EndiannessAwareBinaryReader(stream, Endianness))
                    {
                        reader.BaseStream.Seek(4, SeekOrigin.Current);

                        if (!endiannessChecked)
                        {
                            CheckEndianness(reader.ReadBytes(4));
                            reader.ChangeEndianness(Endianness);
                        }

                        reader.BaseStream.Seek(-8, SeekOrigin.Current);

                        ReadHeaderInfo(reader);
                    }
                }
                catch
                {
                }
            }
        }

        private void ReadHeaderInfo(EndiannessAwareBinaryReader reader)
        {
            MagicBytes = reader.ReadBytes(4, ByteOrder.LittleEndian);
            if (!IsValidHIP) return;

            reader.BaseStream.Seek(4, SeekOrigin.Current);

            var pacDefinedLength = FileLength;
            FileLength = reader.ReadUInt32();
            if (FileLength > pacDefinedLength)
                FileLength = pacDefinedLength;
            ColorRange = reader.ReadUInt32();
            CanvasWidth = reader.ReadInt32();
            CanvasHeight = reader.ReadInt32();
            EncodingParams = reader.ReadBytes(4);

            switch (EncodingParams[0])
            {
                case 0x1:
                    PixelFormat = PixelFormat.Format8bppIndexed;
                    break;
                case 0x4:
                    PixelFormat = PixelFormat.Format16bppGrayScale;
                    break;
                case 0x40:
                    PixelFormat = PixelFormat.Format16bppRgb565;
                    break;
            }

            Encoding = (HIPEncoding) EncodingParams[1];
            var layeredImage = EncodingParams[2];

            var layerHeaderSize = reader.ReadUInt32();
            HeaderSize = 0x20 + layerHeaderSize;

            var isPaletteImage = PixelFormat == PixelFormat.Format8bppIndexed;

            MissingPalette = (ColorRange == 0) & isPaletteImage;

            if (layeredImage != 0)
            {
                ImageWidth = reader.ReadInt32();
                ImageHeight = reader.ReadInt32();
                OffsetX = reader.ReadInt32();
                OffsetY = reader.ReadInt32();
                layerHeaderSize -= 0x10;
            }
            else
            {
                ImageWidth = CanvasWidth;
                ImageHeight = CanvasHeight;
                OffsetX = OffsetY = 0;
            }

            reader.BaseStream.Seek(layerHeaderSize, SeekOrigin.Current);
        }

        public Bitmap GetImage(Color[] importedPalette = null)
        {
            if (NoAccess || !IsValidHIP)
                return null;

            var readStream = GetReadStream();
            var reader = new EndiannessAwareBinaryReader(readStream, System.Text.Encoding.Default, true, Endianness);
            try
            {
                reader.BaseStream.Seek(HeaderSize, SeekOrigin.Current);
                var isPaletteImage = PixelFormat == PixelFormat.Format8bppIndexed;
                if (isPaletteImage)
                {
                    if (importedPalette == null)
                    {
                        var colors = new Color[ColorRange];

                        if (ColorRange > 0)
                        {
                            for (var i = 0; i < ColorRange; i++)
                            {
                                var bytes = reader.ReadBytes(4);
                                colors[i] = Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
                            }
                        }
                        else if (MissingPalette)
                        {
                            colors = new Color[256];
                            colors[0] = Color.Transparent;
                            for (var i = 1; i < colors.Length; i++)
                                colors[i] = Color.Black;
                        }

                        if (colors.Length > 256)
                            Array.Resize(ref colors, 256);
                        Palette = colors;
                    }
                    else
                    {
                        reader.BaseStream.Seek(ColorRange * 4, SeekOrigin.Current);
                        Palette = importedPalette;
                    }
                }

                var savpos = reader.BaseStream.Position;

                if (reader.ReadInt32(ByteOrder.LittleEndian) == 0x73676573)
                {
                    Endianness = ByteOrder.BigEndian;
                    reader.BaseStream.Seek(-4, SeekOrigin.Current);
                    reader.Close();
                    reader.Dispose();
                    reader = new EndiannessAwareBinaryReader(
                        SEGSCompression.DecompressStream(readStream, Endianness), Endianness);
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
                                SEGSCompression.DecompressStream(readStream, Endianness), Endianness);
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

                byte[] pixels = null;

                switch (Encoding)
                {
                    case HIPEncoding.Key:
                        pixels = GetPixelsWithKey(reader);
                        break;
                    case HIPEncoding.RawSignRepeat:
                        pixels = GetPixelsRawSignRepeat(reader);
                        break;
                    default:
                        pixels = GetPixelsFromRawColors(reader);
                        break;
                }

                var bitmapWidth = ImageWidth;
                var bitmapHeight = ImageHeight;

                if (Encoding == HIPEncoding.RawCanvas)
                {
                    bitmapWidth = CanvasWidth;
                    bitmapHeight = CanvasHeight;
                }

                if (isPaletteImage)
                    if (EncodingParams[2] != 0)
                        RoundCanvasSize();

                return CreateBitmap(bitmapWidth, bitmapHeight, ImageWidth, ImageHeight, pixels, PixelFormat, Palette);
            }
            catch
            {
                NoAccess = true;
                return null;
            }
            finally
            {
                reader.Close();
                reader.Dispose();
                readStream.Close();
                readStream.Dispose();
            }
        }

        private byte[] GetPixelsWithKey(EndiannessAwareBinaryReader reader)
        {
            reader.ChangeEndianness(ByteOrder.LittleEndian);
            var Bpp = Image.GetPixelFormatSize(PixelFormat) >> 3;
            var imageColorBytes = new byte[ImageWidth * ImageHeight * Bpp];
            var position = (ulong) reader.BaseStream.Position;
            var key = reader.ReadByte();
            var colorSize = (int) reader.ReadByte();
            colorSize = Bpp;
            var bytesRead = new byte[colorSize];
            var pos = 0;
            while ((ulong) reader.BaseStream.Position - position < FileLength - HeaderSize
                   && reader.BaseStream.Position != reader.BaseStream.Length
                   && pos < imageColorBytes.Length)
                try
                {
                    bytesRead = reader.ReadBytes(colorSize);
                    if (bytesRead[0] == key)
                    {
                        if (bytesRead[0] != bytesRead[1])
                        {
                            bytesRead[1] = bytesRead[1] == 0xFF ? key : bytesRead[1];
                            bytesRead[1]++;
                            for (var i = 0; i < bytesRead[2]; i++)
                            {
                                Buffer.BlockCopy(imageColorBytes, pos - bytesRead[1] * colorSize, imageColorBytes, pos,
                                    colorSize);
                                pos += colorSize;
                            }

                            reader.BaseStream.Seek(-1, SeekOrigin.Current);
                        }
                        else
                        {
                            reader.BaseStream.Seek(-3, SeekOrigin.Current);
                            bytesRead = reader.ReadBytes(colorSize);
                            if (Endianness == ByteOrder.BigEndian)
                                Array.Reverse(bytesRead);
                            Buffer.BlockCopy(bytesRead, 0, imageColorBytes, pos, colorSize);
                            pos += colorSize;
                        }
                    }
                    else
                    {
                        if (Endianness == ByteOrder.BigEndian)
                            Array.Reverse(bytesRead);
                        Buffer.BlockCopy(bytesRead, 0, imageColorBytes, pos, colorSize);
                        pos += colorSize;
                    }
                }
                catch
                {
                }

            return imageColorBytes;
        }

        private byte[] GetPixelsFromRawColors(EndiannessAwareBinaryReader reader)
        {
            var renderCanvas = EncodingParams[3] == 0 || Encoding == HIPEncoding.RawCanvas;
            var width = renderCanvas ? CanvasWidth : ImageWidth;
            var height = renderCanvas ? CanvasHeight : ImageHeight;
            var Bpp = Image.GetPixelFormatSize(PixelFormat) >> 3;
            var imageColorBytes = new byte[width * height * Bpp];
            var index = 0;
            var position = (ulong) reader.BaseStream.Position;
            var repeat = Encoding == HIPEncoding.RawRepeat;
            while ((ulong) reader.BaseStream.Position - position < (ulong) imageColorBytes.Length &&
                   reader.BaseStream.Position != reader.BaseStream.Length &&
                   index < imageColorBytes.Length)
            {
                var bytes = reader.ReadBytes(Bpp);
                var repeatCount = repeat ? reader.ReadByte() : 1;
                for (var i = 0; i < repeatCount && index < imageColorBytes.Length; i++)
                {
                    Buffer.BlockCopy(bytes, 0, imageColorBytes, index, Bpp);
                    index += Bpp;
                }
            }

            /*if (Encoding == HIPEncoding.RawCanvas && PixelFormat == PixelFormat.Format8bppIndexed)
                OffsetX = OffsetY = 0;*/

            return imageColorBytes;
        }

        private byte[] GetPixelsRawSignRepeat(BinaryReader reader)
        {
            var Bpp = Image.GetPixelFormatSize(PixelFormat) >> 3;
            var imageColorBytes = new byte[ImageWidth * ImageHeight * Bpp];
            var position = (ulong) reader.BaseStream.Position;
            var pos = 0;
            while ((ulong) reader.BaseStream.Position - position < FileLength - HeaderSize &&
                   reader.BaseStream.Position != reader.BaseStream.Length)
            {
                var val = reader.ReadInt32();
                if (val < 0)
                {
                    val = val & 0x7FFFFFFF;
                    for (var i = 0; i < val; i++)
                    {
                        Buffer.BlockCopy(reader.ReadBytes(Bpp), 0, imageColorBytes, pos, Bpp);
                        pos += Bpp;
                    }
                }
                else
                {
                    var bytes = reader.ReadBytes(Bpp);
                    for (var i = 0; i < val; i++)
                    {
                        Buffer.BlockCopy(bytes, 0, imageColorBytes, pos, Bpp);
                        pos += Bpp;
                    }
                }
            }

            return imageColorBytes;
        }

        /*private byte[] GetPixelsQuadBlock(BinaryReader reader)
        {
            var Bpp = Image.GetPixelFormatSize(PixelFormat) >> 3;
            var imageColorBytes = new byte[ImageWidth * ImageHeight * Bpp];
            var position = (ulong) reader.BaseStream.Position;
            var pos = 0;
            while ((ulong) reader.BaseStream.Position - position < FileLength - HeaderSize &&
                   reader.BaseStream.Position != reader.BaseStream.Length)
            {
            }

            return imageColorBytes;
        }*/

        private static Bitmap CreateBitmap(int width, int height, byte[] rawData, PixelFormat format,
            Color[] palette = null)
        {
            return CreateBitmap(width, height, width, height, rawData, format, palette);
        }

        private static Bitmap CreateBitmap(int width, int height, int oWidth, int oHeight, byte[] rawData,
            PixelFormat format,
            Color[] palette = null)
        {
            var isGrayScale = format == PixelFormat.Format16bppGrayScale;
            var pixelFormat = isGrayScale ? PixelFormat.Format48bppRgb : format;

            var bitmap = new Bitmap(width, height, pixelFormat);

            if (isGrayScale)
                rawData = Convert16BitGrayScaleToRgb48(rawData, width, height);

            if (pixelFormat == PixelFormat.Format8bppIndexed && palette != null)
            {
                var cp = bitmap.Palette;
                for (var i = 0; i < palette.Length; i++) cp.Entries[i] = palette[i];

                bitmap.Palette = cp;
            }

            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, pixelFormat);
            var scan = data.Scan0;

            var Bpp = Image.GetPixelFormatSize(pixelFormat) >> 3;

            for (var y = 0; y < height; y++)
                Marshal.Copy(rawData, y * width * Bpp, scan + data.Stride * y, width * Bpp);

            bitmap.UnlockBits(data);

            if (width != oWidth || height != oHeight)
            {
                var newBitmap = bitmap.Clone(new Rectangle(0, 0, oWidth, oHeight), format);
                bitmap.Dispose();
                bitmap = newBitmap;
            }

            return bitmap;
        }

        private static byte[] Convert16BitGrayScaleToRgb48(byte[] inBuffer, int width, int height)
        {
            var inBytesPerPixel = 2;
            var outBytesPerPixel = 6;

            var outBuffer = new byte[width * height * outBytesPerPixel];
            var inStride = width * inBytesPerPixel;
            var outStride = width * outBytesPerPixel;

            // Step through the image by row  
            for (var y = 0; y < height; y++)
                // Step through the image by column  
            for (var x = 0; x < width; x++)
            {
                // Get inbuffer index and outbuffer index 
                var inIndex = y * inStride + x * inBytesPerPixel;
                var outIndex = y * outStride + x * outBytesPerPixel;

                var hibyte = inBuffer[inIndex + 1];
                var lobyte = inBuffer[inIndex];

                //R
                outBuffer[outIndex] = lobyte;
                outBuffer[outIndex + 1] = hibyte;

                //G
                outBuffer[outIndex + 2] = lobyte;
                outBuffer[outIndex + 3] = hibyte;

                //B
                outBuffer[outIndex + 4] = lobyte;
                outBuffer[outIndex + 5] = hibyte;
            }

            return outBuffer;
        }

        public Color[] GetPalette()
        {
            if (NoAccess)
                return null;

            using (var reader = new EndiannessAwareBinaryReader(GetReadStream(), Endianness))
            {
                reader.BaseStream.Seek(4, SeekOrigin.Current);
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
                CanvasWidth = reader.ReadInt32();
                CanvasHeight = reader.ReadInt32();
                EncodingParams = reader.ReadBytes(4);

                switch (EncodingParams[0])
                {
                    case 0x1:
                        PixelFormat = PixelFormat.Format8bppIndexed;
                        break;
                    case 0x4:
                        PixelFormat = PixelFormat.Format16bppGrayScale;
                        break;
                    case 0x40:
                        PixelFormat = PixelFormat.Format16bppRgb565;
                        break;
                }

                var layeredImage = EncodingParams[2];

                var layerHeaderSize = reader.ReadUInt32();

                HeaderSize = 0x20 + layerHeaderSize;

                var isPaletteImage = PixelFormat == PixelFormat.Format8bppIndexed;

                MissingPalette = (ColorRange == 0) & isPaletteImage;

                if (!isPaletteImage || MissingPalette)
                    return null;

                if (layeredImage != 0)
                {
                    ImageWidth = reader.ReadInt32();
                    ImageHeight = reader.ReadInt32();
                    OffsetX = reader.ReadInt32();
                    OffsetY = reader.ReadInt32();
                    layerHeaderSize -= 0x10;
                }
                else
                {
                    ImageWidth = CanvasWidth;
                    ImageHeight = CanvasHeight;
                    OffsetX = OffsetY = 0;
                }

                reader.BaseStream.Seek(layerHeaderSize, SeekOrigin.Current);

                var colors = new Color[ColorRange];

                for (var i = 0; i < colors.Length; i++)
                {
                    var bytes = reader.ReadBytes(4);
                    colors[i] = Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
                }

                return colors;
            }
        }

        private void RoundCanvasSize()
        {
            var tmp = CanvasWidth % 1024;
            if (tmp > 0)
                CanvasWidth = CanvasWidth + 1024 - tmp;

            tmp = CanvasHeight % 1024;
            if (tmp > 0)
                CanvasHeight = CanvasHeight + 1024 - tmp;


            var w = OffsetX + ImageWidth - CanvasWidth;
            var h = OffsetY + ImageHeight - CanvasHeight;

            tmp = w % 1024;
            if (tmp > 0)
                CanvasWidth += w + 1024 - tmp;

            tmp = h % 1024;
            if (tmp > 0)
                CanvasHeight += h + 1024 - tmp;
        }

        private void CreateHIP(string path, HIPEncoding hipEncoding = HIPEncoding.Raw,
            bool layeredImage = false, int offsetX = 0, int offsetY = 0, int canvasWidth = 0, int canvasHeight = 0)
        {
            Bitmap bmp = null;
            var ext = Path.GetExtension(path).ToLower();
            var native = ImageTools.nativeImageExtensions.Contains(ext);
            if (native)
                bmp = (Bitmap) Image.FromFile(path, true);
            else if (ext == ".dds") bmp = new DDSFileInfo(path).GetImage();

            if (bmp == null)
                return;

            var pixels = new byte[0];

            using (bmp)
            {
                pixels = bmp.GetPixels();
                ImageWidth = bmp.Width;
                ImageHeight = bmp.Height;
                CanvasWidth = canvasWidth == 0 ? ImageWidth : canvasWidth;
                CanvasHeight = canvasHeight == 0 ? ImageHeight : canvasHeight;
                OffsetX = offsetX;
                OffsetY = offsetY;
                PixelFormat = bmp.PixelFormat;
                Palette = bmp.Palette.Entries;
                ColorRange = (uint) Palette.Length;
                Encoding = hipEncoding;
            }

            var Bpp = Image.GetPixelFormatSize(PixelFormat) >> 3;

            if (layeredImage && (canvasWidth == 0 || canvasHeight == 0)) RoundCanvasSize();

            EncodingParams = new byte[4]
            {
                (byte) (
                    PixelFormat == PixelFormat.Format8bppIndexed ? 0x1 :
                    PixelFormat == PixelFormat.Format16bppGrayScale ? 0x4 :
                    PixelFormat == PixelFormat.Format16bppRgb565 ? 0x40 :
                    0x10),
                (byte) Encoding,
                Convert.ToByte(layeredImage),
                (byte) (layeredImage ? 0x20 : 0x0)
            };

            using (var fileMemoryStream = new MemoryStream())
            {
                using (var writer =
                    new EndiannessAwareBinaryWriter(fileMemoryStream, System.Text.Encoding.Default, true, Endianness))
                {
                    writer.Write(ByteOrder.LittleEndian, System.Text.Encoding.ASCII.GetBytes("HIP\0"));
                    writer.Write(0x125);
                    writer.Write(0);
                    writer.Write(ColorRange);
                    writer.Write(CanvasWidth);
                    writer.Write(CanvasHeight);
                    writer.Write(EncodingParams);
                    if (layeredImage)
                    {
                        writer.Write(0x20);
                        writer.Write(ImageWidth);
                        writer.Write(ImageHeight);
                        writer.Write(OffsetX);
                        writer.Write(OffsetY);
                        writer.Write(new byte[16]);
                    }
                    else
                    {
                        writer.Write(0);
                    }

                    if (Palette.Length > 0)
                        foreach (var color in Palette)
                            writer.Write(color.ToArgb());
                    switch (Encoding)
                    {
                        default: // Raw
                            var repeat = Encoding == HIPEncoding.RawRepeat;
                            var repeatCount = 1;
                            for (var i = 0; i < pixels.Length; i += Bpp)
                            {
                                var colorBytes = new byte[Bpp];
                                Buffer.BlockCopy(pixels, i, colorBytes, 0, Bpp);
                                if (repeat)
                                {
                                    var same = false;
                                    if (i < pixels.Length - Bpp)
                                    {
                                        var nextColorBytes = new byte[Bpp];
                                        Buffer.BlockCopy(pixels, i + Bpp, nextColorBytes, 0, Bpp);
                                        same = nextColorBytes.SequenceEqual(colorBytes);
                                        if (same)
                                        {
                                            repeatCount++;
                                            if (repeatCount == 256)
                                            {
                                                repeatCount--;
                                                same = false;
                                            }
                                        }
                                    }

                                    if (!same)
                                    {
                                        writer.Write(colorBytes);
                                        writer.Write((byte) repeatCount);
                                        repeatCount = 1;
                                    }
                                }
                                else
                                {
                                    writer.Write(colorBytes);
                                }
                            }

                            break;
                    }

                    writer.BaseStream.Position = 8;
                    writer.Write((int) writer.BaseStream.Length);
                    writer.BaseStream.Position = 0;
                    writer.Close();
                }

                VFSIBytes = fileMemoryStream.ToArray();
                FileLength = (ulong) VFSIBytes.Length;
            }
        }
    }
}