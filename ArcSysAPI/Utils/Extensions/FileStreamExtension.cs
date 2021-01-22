using System.IO;

namespace ArcSysAPI.Utils.Extensions
{
    public static class FileStreamExtentionss
    {
        public static void Seek(this Stream fs, ulong offset, SeekOrigin origin)
        {
            if (offset > long.MaxValue)
            {
                var halfOffset = (long) (offset / 2);
                var r = (long) (offset % 2);
                fs.Seek(halfOffset, origin);
                fs.Seek(halfOffset, SeekOrigin.Current);
                fs.Seek(r, SeekOrigin.Current);
            }
            else
            {
                fs.Seek((long) offset, origin);
            }
        }
    }
}