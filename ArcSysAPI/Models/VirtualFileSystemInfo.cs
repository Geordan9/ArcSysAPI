using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Utils;
using ArcSysAPI.Utils.Extensions;

namespace ArcSysAPI.Models
{
    public class VirtualFileSystemInfo : FileSystemInfo, INotifyPropertyChanged
    {
        [Flags]
        public enum FileObfuscation
        {
            None = 0x0,
            FPACEncryption = 0x1,
            FPACDeflation = 0x2,
            BBTAGEncryption = 0x4,
            SwitchCompression = 0x8
        }

        public VirtualFileSystemInfo(string path, bool preCheck = true) : this(new FileInfo(path), preCheck)
        {
        }

        private VirtualFileSystemInfo(FileSystemInfo fi, bool preCheck = true) : this(fi.FullName, 0, 0, null, preCheck)
        {
        }

        public VirtualFileSystemInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent,
            bool preCheck = true)
        {
            var isDirectory = false;
            FullName = path;
            if (length == 0)
            {
                if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                    length = (ulong) new FileInfo(path).Length;
                else
                    isDirectory = true;
            }

            FileLength = length;
            Offset = offset;
            Parent = parent;
            if (Parent != null)
            {
                Initialized = Parent.Initialized;
                Obfuscation = Parent.Obfuscation;
                VFSIBytes = Parent.VFSIBytes;
            }
            else if (!isDirectory && preCheck)
            {
                Initialize();
            }
        }

        public VirtualFileSystemInfo(MemoryStream memstream, bool preCheck = true)
        {
            VFSIBytes = memstream.ToArray();
            FullName = "Memory";
            FileLength = (ulong) VFSIBytes.Length;
            Offset = 0;
            Parent = null;
            if (preCheck)
                Initialize();
        }

        // Properties

        public override string Name
        {
            get
            {
                var name = FullName;
                var extPaths = GetExtendedPaths(FullName);
                if (extPaths.Length > 0)
                    name = extPaths.Last();
                return Path.GetFileName(name);
            }
        }

        public new string Extension => Path.GetExtension(Name);

        public override bool Exists => GetExistence();

        public new string FullName { get; }

        protected ulong Offset { get; set; }

        protected byte[] MagicBytes { get; set; } = new byte[4];

        public FileObfuscation Obfuscation { get; set; }

        protected byte[] VFSIBytes { get; set; }

        public ByteOrder Endianness { get; set; }

        protected bool endiannessChecked { get; set; } = false;

        public bool NoAccess { get; protected set; }

        public ulong FileLength { get; protected set; }

        public VirtualDirectoryInfo Parent { get; }

        public VirtualFileSystemInfo VirtualRoot
        {
            get
            {
                if (Parent != null)
                    return Parent.VirtualRoot;

                return this;
            }
        }

        private bool active;

        public bool Active
        {
            get => active;
            set
            {
                active = value;
                if (!value)
                {
                    if (VFSIBytes != null)
                        VFSIBytes = null;
                }
                else if (VFSIBytes == null)
                {
                    GetReadStream();
                }
            }
        }

        protected bool Initialized { get; set; }

        // Methods

        private bool GetExistence()
        {
            if (GetExtendedPaths(FullName).Length < 1)
                return File.Exists(FullName);

            var mainFile = new FileInfo(GetPrimaryPath(FullName));
            if (!mainFile.Exists)
                return false;

            using (var fs = GetReadStream())
            {
                return GetExistence(Parent, fs);
            }
        }

        private bool GetExistence(VirtualFileSystemInfo vfi, Stream fs)
        {
            if (vfi.Parent != null)
                if (!GetExistence(vfi.Parent, fs))
                    return false;


            fs.Seek(vfi.Offset, SeekOrigin.Current);
            var mb = new byte[4];
            fs.Read(mb, 0, 4);

            return vfi.MagicBytes.SequenceEqual(mb) && !mb.SequenceEqual(new byte[4]);
        }

        protected void OffsetFileStream(VirtualFileSystemInfo vfi, Stream stream)
        {
            if (vfi.Parent != null)
                OffsetFileStream(vfi.Parent, stream);

            stream.Seek(vfi.Offset, SeekOrigin.Current);
        }

        public string GetPrimaryPath()
        {
            return new Regex(@":(?!\\)").Split(FullName)[0];
        }

        public string GetPrimaryPath(string extPath)
        {
            return new Regex(@":(?!\\)").Split(extPath)[0];
        }

        public string[] GetExtendedPaths(string extPath)
        {
            return new Regex(@":(?!\\)").Split(extPath).Skip(1).ToArray();
        }

        public string[] GetExtendedPaths()
        {
            return GetExtendedPaths(FullName);
        }

        public byte[] GetBytes()
        {
            try
            {
                if (VFSIBytes != null)
                    if ((ulong) VFSIBytes.Length == FileLength || FileLength == 0)
                        return VFSIBytes;
                using (var reader = new BinaryReader(GetReadStream()))
                {
                    var bytes = new byte[FileLength];
                    for (ulong i = 0; i < FileLength; i++)
                        bytes[i] = reader.ReadByte();
                    reader.Close();
                    return bytes;
                }
            }
            catch
            {
                return null;
            }
        }

        protected FileObfuscation GetFileObfuscation()
        {
            if (MagicBytes.Take(3).SequenceEqual(new byte[] {0x1F, 0x8B, 0x08}))
                return FileObfuscation.SwitchCompression;

            if (Extension.Contains("pac") && !MagicBytes.SequenceEqual(new byte[] {0x42, 0x43, 0x53, 0x4D}))
            {
                if (MagicBytes.SequenceEqual(new byte[] {0x44, 0x46, 0x41, 0x53}))
                    return Obfuscation | FileObfuscation.FPACDeflation;
                if (!MagicBytes.SequenceEqual(new byte[] {0x46, 0x50, 0x41, 0x43}))
                    return Obfuscation | FileObfuscation.FPACEncryption;
            }

            if (string.IsNullOrEmpty(Extension) && MD5Tools.IsMD5(Name))
                return FileObfuscation.BBTAGEncryption;


            return FileObfuscation.None;
        }

        protected void UpdateMagicAndObfuscation(MemoryStream ms)
        {
            ms.Read(MagicBytes, 0, MagicBytes.Length);
            var obf = GetFileObfuscation();
            if (obf != FileObfuscation.None) Obfuscation = obf;
            ms.Position = 0;
        }

        protected void Initialize(bool force = false)
        {
            if (Initialized && !force)
                return;
            Initialized = true;
            using (var s = GetReadStream())
            {
                if (s == null)
                    return;
                OffsetFileStream(this, s);
                s.Read(MagicBytes, 0, 4);
                Obfuscation = GetFileObfuscation();
                s.Close();
            }
        }

        protected Stream GetReadStream()
        {
            try
            {
                if (VFSIBytes == null && VirtualRoot.VFSIBytes == null)
                {
                    var fs = new FileStream(GetPrimaryPath(FullName), FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    try
                    {
                        if (Obfuscation != FileObfuscation.None)
                        {
                            var stream = new MemoryStream();
                            try
                            {
                                var path = fs.Name;
                                fs.CopyTo(stream);
                                fs.Close();
                                fs.Dispose();

                                stream.Position = 0;

                                if (Obfuscation.HasFlag(FileObfuscation.FPACEncryption))
                                {
                                    var output = BBObfuscatorTools.FPACDecryptStream(stream, path, !Active);
                                    stream.Close();
                                    stream.Dispose();
                                    stream = output;

                                    UpdateMagicAndObfuscation(stream);
                                }

                                if (Obfuscation.HasFlag(FileObfuscation.FPACDeflation))
                                {
                                    var output = BBObfuscatorTools.DFASFPACInflateStream(stream);
                                    stream.Close();
                                    stream.Dispose();
                                    stream = output;

                                    UpdateMagicAndObfuscation(stream);
                                }

                                if (Obfuscation.HasFlag(FileObfuscation.SwitchCompression))
                                {
                                    using (Stream input = new GZipStream(new MemoryStream(stream.GetBuffer()),
                                        CompressionMode.Decompress, true))
                                    {
                                        using (var output = new MemoryStream())
                                        {
                                            input.CopyTo(output);
                                            stream = new MemoryStream(output.ToArray());
                                        }
                                    }

                                    UpdateMagicAndObfuscation(stream);
                                }

                                if (Obfuscation.HasFlag(FileObfuscation.BBTAGEncryption))
                                {
                                    var output = BBTAGMD5CryptTools.BBTAGMD5CryptStream(
                                        stream, path,
                                        BBTAGMD5CryptTools.CryptMode.Decrypt, true);
                                    stream.Close();
                                    stream.Dispose();
                                    stream = output;

                                    UpdateMagicAndObfuscation(stream);
                                }

                                if (VirtualRoot.Active)
                                    VFSIBytes = stream.ToArray();

                                stream.Position = 0;
                                if (Offset > 0) OffsetFileStream(this, stream);
                                return stream;
                            }
                            catch
                            {
                                stream.Close();
                                stream.Dispose();
                                fs.Close();
                                fs.Dispose();
                                return null;
                            }
                        }

                        if (Offset > 0) OffsetFileStream(this, fs);
                        return fs;
                    }
                    catch
                    {
                        fs.Close();
                        fs.Dispose();
                        return null;
                    }
                }

                var memStream = new MemoryStream(VFSIBytes != null ? VFSIBytes : VirtualRoot.VFSIBytes);
                if (Offset > 0) OffsetFileStream(this, memStream);
                return memStream;
            }
            catch
            {
                NoAccess = true;
                return null;
            }
        }

        public override void Delete()
        {
            var pfi = (PACFileInfo) Parent;
            if (pfi != null)
            {
                pfi.RemoveItem(this);
            }
            else if (Parent == null)
            {
                var fsi = (FileSystemInfo) this;
                var fi = (FileInfo) fsi;
                if (fi != null)
                {
                    fi.Delete();
                    return;
                }

                var di = (DirectoryInfo) fsi;
                if (di != null) di.Delete();
            }
        }

        // INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}