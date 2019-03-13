using System;
using System.Collections.Generic;
using System.Text;

namespace DownloaderCore
{
    public delegate void DownloadStartedEventHandler(long totalSize);
    public delegate void DataReceivedEventHandler(long fetchedSize, float speed);
    public delegate void DownloadErrorEventHandler(Exception ex);
    public delegate void DownloadFinishedEventHandler(float totalTime, float avgSpeed);
    public delegate void RequestCreatedEventHandler(string url);
    public delegate void ResponseReceivedEventHandler(string url);

    public class NamingConflictEventArgs : EventArgs
    {
        public bool UseServerName { get; set; } = false;
    }
}
