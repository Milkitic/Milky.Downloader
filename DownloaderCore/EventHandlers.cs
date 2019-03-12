using System;
using System.Collections.Generic;
using System.Text;

namespace DownloaderCore
{
    public delegate void DownloadStartedEventHandler(long totalSize);
    public delegate void DataReceivedEventHandler(long fetchedSize, float speed);
    public delegate void DownloadErrorEventHandler(Exception ex);
    public delegate void DownloadFinishedEventHandler();
    public delegate void RequestCreatedEventHandler();
    public delegate void ResponseReceivedEventHandler();
}
