using System.IO;

namespace ArcSysAPI.Models
{
    public class VirtualFileInfo : VirtualFileSystemInfo
    {
        public VirtualFileInfo(string path, bool preCheck = true) : base(path, preCheck)
        {
        }

        public VirtualFileInfo(string path, ulong length, ulong offset, VirtualDirectoryInfo parent,
            bool preCheck = true) : base(path,
            length, offset, parent, preCheck)
        {
        }

        public VirtualFileInfo(MemoryStream memstream, bool preCheck = true) : base(memstream, preCheck)
        {
        }
    }
}