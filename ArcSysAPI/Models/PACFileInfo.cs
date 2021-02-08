using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ArcSysAPI.Common.Enums;

namespace ArcSysAPI.Models
{
    public class PACFileInfo : VirtualDirectoryInfo
    {
        [Flags]
        public enum PACParameters : uint
        {
            GenerateNameID = 0x80000000,
            GenerateExtendedNameID = 0xA0000000
        }

        public PACFileInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
            if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            {
                if (preCheck)
                    InitGetHeader();
            }
            else
            {
                Parameters = (PACParameters) 0x1;
                CreatePACDataFromFolder();
            }
        }

        public PACFileInfo(string folderPath, PACParameters parameters, ByteOrder endianness = ByteOrder.LittleEndian) :
            base(folderPath)
        {
            if (!File.GetAttributes(folderPath).HasFlag(FileAttributes.Directory))
                throw new Exception("Folder path must be a folder.");

            Parameters = parameters | (PACParameters) 0x1 /*Default*/;
            Endianness = endianness;
            CreatePACDataFromFolder();
        }

        public PACFileInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent, bool preCheck = true) :
            base(path, length,
                offset, parent, preCheck)
        {
            InitGetHeader();
        }

        public PACFileInfo(MemoryStream memstream, bool preCheck = true) : base(memstream, preCheck)
        {
            InitGetHeader();
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
                        MagicBytes = reader.ReadBytes(4, ByteOrder.LittleEndian);
                        if (MagicBytes.SequenceEqual(new byte[] {0x42, 0x43, 0x53, 0x4D}))
                        {
                            reader.BaseStream.Seek(12, SeekOrigin.Current);
                            Offset += 16;
                        }
                        else
                        {
                            reader.BaseStream.Seek(-4, SeekOrigin.Current);
                        }

                        if (Endianness == ByteOrder.LittleEndian && !endiannessChecked)
                        {
                            reader.BaseStream.Seek(4, SeekOrigin.Current);
                            var headersize = reader.ReadUInt32();
                            reader.BaseStream.Seek(4, SeekOrigin.Current);
                            var filecount = reader.ReadUInt32();
                            reader.BaseStream.Seek(4, SeekOrigin.Current);
                            var fileentrysize = reader.ReadUInt32() + 12;
                            var remainder = fileentrysize % 16;
                            fileentrysize = fileentrysize + (remainder == 0 ? remainder : 16 - remainder);
                            var calcheadersize = fileentrysize * filecount + 0x20;
                            var calcheadersize2 = (fileentrysize + 16) * filecount + 0x20;
                            if (calcheadersize != headersize && calcheadersize2 != headersize)
                                Endianness = ByteOrder.BigEndian;

                            stream.Seek(-24, SeekOrigin.Current);

                            endiannessChecked = true;
                        }

                        ReadHeaderInfo(reader);
                    }
                }
                catch
                {
                }
            }
        }

        public uint HeaderSize { get; private set; }

        public uint FileCount { get; private set; }

        public PACParameters Parameters { get; private set; }

        public int FileNameLength { get; private set; }

        public bool IsValidPAC => MagicBytes.SequenceEqual(new byte[] {0x46, 0x50, 0x41, 0x43});

        private void ReadHeaderInfo(EndiannessAwareBinaryReader reader)
        {
            MagicBytes = reader.ReadBytes(4, ByteOrder.LittleEndian);
            if (!IsValidPAC) return;

            HeaderSize = reader.ReadUInt32();
            FileLength = reader.ReadUInt32();
            FileCount = reader.ReadUInt32();

            Parameters = (PACParameters) reader.ReadInt32();

            FileNameLength = reader.ReadInt32();

            reader.BaseStream.Seek(8, SeekOrigin.Current);
        }

        public VirtualFileSystemInfo[] GetFiles(bool recheck = false)
        {
            if (!Initialized)
            {
                Initialize();
                InitGetHeader();
            }

            try
            {
                if (FileCount <= 0)
                    return new VirtualFileSystemInfo[0];

                if (Files != null && !recheck)
                    return Files;

                using (var reader = new EndiannessAwareBinaryReader(GetReadStream(), Endianness))
                {
                    ReadHeaderInfo(reader);

                    var virtualFiles = new VirtualFileSystemInfo[FileCount];

                    for (var i = 0; i < FileCount; i++)
                    {
                        var fileName = Encoding.ASCII
                            .GetString(reader.ReadBytes(FileNameLength, ByteOrder.LittleEndian))
                            .Replace("\0", string.Empty);
                        var fileIndex = reader.ReadUInt32();
                        var fileOffset = reader.ReadUInt32();
                        var fileLength = reader.ReadUInt32();
                        var seeklength = (FileNameLength + 12) % 16;
                        reader.BaseStream.Seek(seeklength == 0 ? seeklength : 16 - seeklength, SeekOrigin.Current);
                        if (reader.ReadByte() == 0x0 && seeklength == 0)
                            reader.BaseStream.Seek(16, SeekOrigin.Current);
                        reader.BaseStream.Seek(-1, SeekOrigin.Current);
                        var ext = Path.GetExtension(fileName);
                        switch (ext)
                        {
                            case ".pac":
                            case ".paccs":
                            case ".pacgz":
                            case ".fontpac":
                                virtualFiles[i] = new PACFileInfo(
                                    FullName + ':' + fileName,
                                    fileLength,
                                    fileOffset + HeaderSize,
                                    this);
                                break;
                            case ".hip":
                                virtualFiles[i] = new HIPFileInfo(
                                    FullName + ':' + fileName,
                                    fileLength,
                                    fileOffset + HeaderSize,
                                    this);
                                break;
                            case ".hpl":
                                virtualFiles[i] = new HPLFileInfo(
                                    FullName + ':' + fileName,
                                    fileLength,
                                    fileOffset + HeaderSize,
                                    this);
                                break;
                            case ".dds":
                                virtualFiles[i] = new DDSFileInfo(
                                    FullName + ':' + fileName,
                                    fileLength,
                                    fileOffset + HeaderSize,
                                    this);
                                break;
                            default:
                                virtualFiles[i] = new VirtualFileSystemInfo(
                                    FullName + ':' + fileName,
                                    fileLength,
                                    fileOffset + HeaderSize,
                                    this);
                                break;
                        }
                    }

                    reader.Close();

                    Files = virtualFiles;
                    return Files;
                }
            }
            catch
            {
                Files = new VirtualFileSystemInfo[0];
                return Files;
            }
        }

        private void CreatePACDataFromFolder()
        {
            using (var memstream = ProcessFolder(FullName))
            {
                if (memstream == null)
                    return;

                VFSIBytes = memstream.ToArray();
            }
        }

        private void RebuildPACData(VirtualFileSystemInfo[] files)
        {
            using (var memstream = CreatePAC(files, files.Select(f => new MemoryStream(f.GetBytes())).ToArray()))
            {
                if (memstream == null)
                    return;

                VFSIBytes = memstream.ToArray();
            }

            var pfi = (PACFileInfo) Parent;
            if (pfi != null)
            {
                pfi.RebuildPACData(GetFiles());
            }
            else
            {
                File.WriteAllBytes(FullName, VFSIBytes);
                GetFiles(true);
            }
        }

        private MemoryStream ProcessFolder(string path)
        {
            var memoryStreamList = new List<MemoryStream>();

            var folders = Directory.GetDirectories(path).Select(d => new DirectoryInfo(d)).ToArray();

            foreach (var folder in folders) memoryStreamList.Add(ProcessFolder(folder.FullName));

            var files = Directory.GetFiles(path).Select(f => new FileInfo(f)).ToArray();

            foreach (var file in files)
            {
                var memstream = new MemoryStream();

                using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.CopyTo(memstream);
                    fs.Close();
                }

                memoryStreamList.Add(memstream);
            }

            var fsia = new FileSystemInfo[files.Length + folders.Length];
            if (folders.Length > 0)
                Array.Copy(folders, 0, fsia, 0, folders.Length);

            if (files.Length > 0)
                Array.Copy(files, 0, fsia, folders.Length, files.Length);

            return CreatePAC(fsia, memoryStreamList.ToArray());
        }

        public void RemoveItem(VirtualFileSystemInfo vfsi)
        {
            var filesList = GetFiles().ToList();
            filesList.Remove(vfsi);
            RebuildPACData(filesList.ToArray());
        }

        private MemoryStream CreatePAC(FileSystemInfo[] fsia, MemoryStream[] memoryStreams)
        {
            var createExtNameID = Parameters.HasFlag(PACParameters.GenerateExtendedNameID);

            var createNameID = Parameters.HasFlag(PACParameters.GenerateNameID) ||
                               createExtNameID;

            var names = fsia.Select(fsi =>
            {
                var vfsi = fsi as VirtualFileSystemInfo;
                if (vfsi == null)
                    if (fsi.Attributes.HasFlag(FileAttributes.Directory))
                        return fsi.Name + ".pac";
                return fsi.Name;
            }).ToArray();

            var longestFileName = GetMaxNameLength(names);

            var fileMemoryStream = new MemoryStream();

            var fileLength = 0;

            using (var writer = new EndiannessAwareBinaryWriter(fileMemoryStream, Encoding.Default, true, Endianness))
            {
                writer.Write(ByteOrder.LittleEndian, Encoding.ASCII.GetBytes("FPAC"));
                writer.Write(0);
                writer.Write(0);
                writer.Write(names.Length);
                writer.Write((uint) Parameters);
                writer.Write(longestFileName);
                writer.Write(0);
                writer.Write(0);
                for (var i = 0; i < names.Length; i++)
                {
                    var bytes = new byte[longestFileName];
                    var fileName = Path.GetFileName(names[i]);
                    if (fileName.Length >= longestFileName)
                    {
                        var fn = Path.GetFileNameWithoutExtension(fileName);
                        var fe = Path.GetExtension(fileName);
                        fn = fn.Remove(longestFileName - fe.Length - 1);
                        fileName = fn + fe;
                    }

                    var nameBytes = Encoding.ASCII.GetBytes(fileName);
                    Buffer.BlockCopy(nameBytes, 0, bytes, 0, nameBytes.Length);
                    writer.Write(ByteOrder.LittleEndian, bytes);
                    writer.Write(i);
                    writer.Write(fileLength);
                    var length = (int) memoryStreams[i].Length % 16;
                    length = length == 0 ? length : 16 - length;
                    length += (int) memoryStreams[i].Length;
                    fileLength += length;
                    writer.Write((int) memoryStreams[i].Length);
                    if (createNameID)
                    {
                        var nameID = 0;
                        foreach (var c in fileName.ToLower())
                        {
                            nameID *= 0x89;
                            nameID += c;
                        }

                        writer.Write(nameID);
                    }

                    var padLength = (longestFileName + 12 + (createNameID ? 4 : 0)) % 16;
                    padLength = padLength == 0 ? padLength : 16 - padLength;
                    writer.Write(new byte[padLength]);
                }

                var headersize = (int) writer.BaseStream.Position;

                foreach (var stream in memoryStreams)
                    using (stream)
                    {
                        writer.Write(stream.ToArray());
                        var padLength = (int) stream.Length % 16;
                        padLength = padLength == 0 ? padLength : 16 - padLength;
                        writer.Write(new byte[padLength]);
                    }

                writer.BaseStream.Position = 4;
                writer.Write(headersize);
                writer.Write((int) writer.BaseStream.Length);
                writer.BaseStream.Position = 0;
                writer.Close();
            }

            return fileMemoryStream;
        }

        private int GetMaxNameLength(string[] fileNames)
        {
            var createExtNameID = Parameters.HasFlag(PACParameters.GenerateExtendedNameID);

            var createNameID = Parameters.HasFlag(PACParameters.GenerateNameID) ||
                               createExtNameID;

            var minNameLength = fileNames.Length > 0 ? createNameID ? createExtNameID ? 64 : 32 : 1 : 0;
            var longestFileName = minNameLength;
            if (fileNames.Length > 0)
            {
                longestFileName = fileNames.OrderByDescending(p => p.Length).FirstOrDefault().Length;
                var namelength = longestFileName % 4;
                longestFileName += namelength == 0 ? namelength : 4 - namelength;

                if (createNameID)
                {
                    if (longestFileName >= minNameLength)
                    {
                        var errmesg = "Due to packing parameters, file name cannot equal or exceed " +
                                      $"{(createExtNameID ? 64 : 32)} characters.";
                        throw new Exception(errmesg);
                    }

                    longestFileName = minNameLength;
                }
                else
                {
                    longestFileName = longestFileName < minNameLength ? minNameLength : longestFileName;
                }
            }

            return longestFileName;
        }
    }
}