using System.IO;

namespace ArcSysAPI.Models
{
    public class VirtualDirectoryInfo : VirtualFileSystemInfo
    {
        public VirtualDirectoryInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
        }

        public VirtualDirectoryInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent,
            bool preCheck = true) : base(path,
            length, offset, parent, preCheck)
        {
        }

        public VirtualDirectoryInfo(MemoryStream memstream, bool preCheck = true) : base(memstream, preCheck)
        {
        }

        public VirtualFileSystemInfo[] Files { get; protected set; } = null;
    }
}