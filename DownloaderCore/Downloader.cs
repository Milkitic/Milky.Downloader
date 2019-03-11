using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DownloaderCore
{
    public delegate void DownloadStartedHandler(long totalSize);
    public delegate void DataReceivedHandler(long fetchedSize, float speed);
    public delegate void DownloadErrorHandler(Exception ex);
    public delegate void DownloadFinishedHandler();

    public class Downloader
    {
        static Downloader()
        {
            if (ServicePointManager.SecurityProtocol != SecurityProtocolType.Tls12)
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; // for some reason
        }

        public string Url { get; set; }
        public string SavePath { get; set; }
        public string SaveDirectory => Path.GetDirectoryName(SavePath);

        public event DownloadStartedHandler DownloadStarted;
        public event DataReceivedHandler ProgressChanged;
        public event DownloadFinishedHandler DownloadFinished;
        public event DownloadErrorHandler ErrorOccured;
        public event Action RequestCreated;
        public event Action ResponseReceived;

        private event Action<long> DataReceived;

        private Task _downloadTask;
        private Task _progressTask;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _downloadObject = new object();
        private bool _isDownloading;
        private bool _useMemoryCache = false;

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

        public Downloader(string url, string savePath)
        {
            Url = url;
            SavePath = savePath;
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
                                if (GetData(responseStream, memoryStream))
                                {
                                    ErrorOccured?.Invoke(new NotImplementedException());
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
                            using (var fileStream = GetFileStream())
                            {
                                if (GetData(responseStream, fileStream))
                                {
                                    ErrorOccured?.Invoke(new NotImplementedException());
                                    return;
                                }
                            }
                        }
                    }

                    DownloadFinished?.Invoke();

                }
                catch (WebException e)
                {
                    if (e.InnerException is SocketException)
                    {
                        Console.WriteLine(@"连接失败。");
                        throw;
                    }

                    if (e.Status == WebExceptionStatus.Timeout)
                    {
                        Console.WriteLine(@"超时。");
                        throw;
                    }

                    throw;
                }
            });

            await Task.WhenAll(_downloadTask);

            lock (_downloadObject)
            {
                _isDownloading = false;
            }
        }

        private FileStream GetFileStream()
        {
            return new FileStream(SavePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        }

        private bool GetData(Stream inputStream, Stream outputStream)
        {
            var buffer = new byte[1024]; // buffer length, here set it to 1KB

            int readSize = 0;
            do
            {
                if (readSize > 0)
                {
                    DataReceived?.Invoke(readSize);
                    outputStream.Write(buffer, 0, readSize);
                }

                if (_cts.IsCancellationRequested)
                {
                    Console.WriteLine(@"Download canceled.");
                    return true;
                }

                readSize = inputStream.Read(buffer, 0, buffer.Length);
            } while (readSize > 0);

            return false;
        }

        private void StartSynchronousProgressTask()
        {
            _progressTask = Task.Run(() =>
            {
                bool isFinished = false;
                DownloadFinished += () => { isFinished = true; };
                const int interval = 500;

                while (!isFinished && !_cts.IsCancellationRequested)
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

        public void Interrupt()
        {
            _cts.Cancel();
            Task.WaitAll(_downloadTask);
        }
    }
}
