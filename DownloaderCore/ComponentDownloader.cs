using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DownloaderCore
{
    public class ComponentDownloader
    {
        static ComponentDownloader()
        {
            if (ServicePointManager.SecurityProtocol != SecurityProtocolType.Tls12)
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; // for some reason
        }

        public string Url { get; set; }
        public string SavePath { get; set; }

        public string SaveDirectory => Path.GetDirectoryName(SavePath);

        public event DownloadStartedEventHandler DownloadStarted;
        public event DataReceivedEventHandler ProgressChanged;
        public event DownloadFinishedEventHandler DownloadFinished;
        public event DownloadErrorEventHandler ErrorOccured;
        public event RequestCreatedEventHandler RequestCreated;
        public event ResponseReceivedEventHandler ResponseReceived;

        private event Action<long> DataReceived;

        private Task _downloadTask;
        private Task _progressTask;
        private readonly CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
        private readonly object _downloadObject = new object();
        private bool _isDownloading;

        private bool _useMemoryCache = false;

        private string _tempSavePath;

        public bool UseMemoryCache
        {
            get => _useMemoryCache;
            set
            {
                lock (_downloadObject)
                {
                    if (_isDownloading)
                        throw new InvalidOperationException();
                }

                _useMemoryCache = value;
            }
        }

        public ComponentDownloader(string url, string savePath)
        {
            Url = url;
            SavePath = savePath;
            _tempSavePath = savePath + ".milkydownload";
        }

        public async Task DownloadAsync()
        {
            lock (_downloadObject)
            {
                if (_isDownloading)
                    throw new InvalidOperationException();
                _isDownloading = true;
            }

            StartSynchronousProgressTask();

            HttpWebRequest request = CreateRequestObject();
            RequestCreated?.Invoke();

            var response = await request.GetResponseAsync() as HttpWebResponse;
            ResponseReceived?.Invoke();

            if (response is null)
            {
                ErrorOccured?.Invoke(new NotImplementedException());
                return;
            }

            _downloadTask = Task.Run(() =>
            {
                try
                {
                    var fileSize = response.ContentLength;
                    DownloadStarted?.Invoke(fileSize);

                    if (!Directory.Exists(SaveDirectory))
                        Directory.CreateDirectory(SaveDirectory);

                    using (var responseStream = response.GetResponseStream())
                    {
                        if (responseStream == null)
                        {
                            ErrorOccured?.Invoke(new NotImplementedException());
                            return;
                        }

                        if (UseMemoryCache)
                        {
                            using (var memoryStream = new MemoryStream((int)fileSize))
                            {
                                if (!GetData(responseStream, memoryStream, 0))
                                {
                                    RaiseUserCancelError();
                                    return;
                                }

                                using (var fileStream = GetFileStream())
                                {
                                    fileStream.Write(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
                                }
                            }
                        }
                        else
                        {
                            long offset = 0;
                            var cache = CacheInfo.DownloadingFiles.FirstOrDefault(k => k.FilePath == _tempSavePath);
                            if (cache != null)
                            {
                                offset = cache.TransferredByte;
                            }
                            using (var fileStream = GetFileStream())
                            {
                                if (!GetData(responseStream, fileStream, offset))
                                {
                                    RaiseUserCancelError();
                                    return;
                                }
                            }
                        }
                    }

                    DownloadFinished?.Invoke();

                }
                catch (Exception ex)
                {
                    ErrorOccured?.Invoke(ex);
                    return;
                }
            });

            await Task.WhenAll(_downloadTask, _progressTask);

            lock (_downloadObject)
            {
                _isDownloading = false;
            }
        }

        public async Task StopAsync()
        {
            _cancelTokenSource.Cancel();
            await Task.WhenAll(_downloadTask, _progressTask);
        }

        private FileStream GetFileStream()
        {
            return new FileStream(SavePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        }

        private bool GetData(Stream inputStream, Stream outputStream, long offset)
        {
            var buffer = new byte[1024]; // buffer length, here set it to 1KB
            if (offset != 0 && outputStream.Length - 1 == offset)
            {
                outputStream.Position = offset;
            }

            int readSize = 0;
            do
            {
                if (readSize > 0)
                {
                    DataReceived?.Invoke(readSize);
                    outputStream.Write(buffer, 0, readSize);
                }

                if (_cancelTokenSource.IsCancellationRequested)
                {
                    Console.WriteLine(@"Download canceled.");
                    return false;
                }

                readSize = inputStream.Read(buffer, 0, buffer.Length);
            } while (readSize > 0);

            return true;
        }

        private void StartSynchronousProgressTask()
        {
            _progressTask = Task.Run(() =>
            {
                var watch = new Stopwatch();
                bool isFinished = false;
                DownloadFinished += () => { isFinished = true; };
                const int interval = 500;
                DataReceived += b => { };
                while (!isFinished && !_cancelTokenSource.IsCancellationRequested)
                {
                    // todo
                    Thread.Sleep(interval);
                }
            });
        }

        private HttpWebRequest CreateRequestObject()
        {
            var request = WebRequest.Create(Url) as HttpWebRequest;
            if (request is null)
            {
                throw new NotImplementedException();
            }

            request.Timeout = 30000;
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36";
            return request;
        }

        private void RaiseUserCancelError()
        {
            ErrorOccured?.Invoke(new Exception("Download was canceled by user."));
        }
    }
}
