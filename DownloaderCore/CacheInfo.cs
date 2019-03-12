using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace DownloaderCore
{
    [Serializable]
    public class CacheInfo : ISerializable
    {
        public CacheInfo()
        {
        }

        public CacheInfo(SerializationInfo info, StreamingContext context) : this()
        {
        }

        public static List<DownloadingFileInfo> DownloadingFiles { get; } = new List<DownloadingFileInfo>();
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            foreach (var file in DownloadingFiles)
            {
                info.AddValue("path", file.FilePath, typeof(string));
                info.AddValue("trans", file.TransferredByte, typeof(long));
            }
        }
    }

    public class DownloadingFileInfo
    {
        public string FilePath { get; set; }
        public long TransferredByte { get; set; }
    }
}
