using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public string SaveDirectory { get; }

        public event DownloadStartedEventHandler DownloadStarted;
        public event DataReceivedEventHandler ProgressChanged;
        public event DownloadFinishedEventHandler DownloadFinished;
        public event DownloadErrorEventHandler ErrorOccured;
        public event RequestCreatedEventHandler RequestCreated;
        public event ResponseReceivedEventHandler ResponseReceived;
        public event EventHandler<NamingConflictEventArgs> NamingConflict;

        private event Action<long> DataReceived;

        private Task _downloadTask;
        private Task _progressTask;
        private readonly CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
        private readonly CancellationTokenSource _finishedTokenSource = new CancellationTokenSource();
        private readonly object _downloadObject = new object();
        private bool _isDownloading;

        private bool _useMemoryCache;

        private string _tempSavePath;
        private string _recommendName;
        private const int BufferLength = 512; // buffer length, here set it to 1KB

        private string RecommendSavePath => Path.Combine(SaveDirectory, _recommendName);

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

        public ComponentDownloader(string url, string targetSaveDir, string recommendName = null)
        {
            Url = url;
            SaveDirectory = targetSaveDir;
            _recommendName = recommendName;
        }

        public async Task StartAsync()
        {
            lock (_downloadObject)
            {
                if (_isDownloading)
                    throw new InvalidOperationException();
                _isDownloading = true;
            }

            try
            {
                WebRequest request = CreateRequestObject();
                if (_recommendName == null)
                    _recommendName = request.RequestUri.AbsoluteUri.Split('/').Last();
                RequestCreated?.Invoke(Url);

                var response = await request.GetResponseAsync();
                ResponseReceived?.Invoke(Url);

                if (response is null)
                {
                    RaiseError(new WebException("The server returns an empty response."));
                    return;
                }

                var newName = response.ResponseUri.AbsoluteUri.Split('/').Last();
                if (newName != _recommendName)
                {
                    var arg = new NamingConflictEventArgs();
                    NamingConflict?.Invoke(this, arg);
                    if (arg.UseServerName)
                    {
                        _recommendName = newName;
                    }
                }

                _tempSavePath = RecommendSavePath + ".milkydownload";

                StartSynchronousProgressTask();
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
                                RaiseError(new NotImplementedException());
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

                                    using (var fileStream = GetFileStream(false))
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
                                using (var fileStream = GetFileStream(offset != 0))
                                {
                                    if (!GetData(responseStream, fileStream, offset))
                                    {
                                        RaiseUserCancelError();
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RaiseError(ex);
                        return;
                    }

                    _finishedTokenSource?.Cancel();
                });

                await Task.WhenAll(_downloadTask, _progressTask);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }


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

        private FileStream GetFileStream(bool append)
        {
            return new FileStream(_tempSavePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        }

        private bool GetData(Stream inputStream, Stream outputStream, long offset)
        {
            var buffer = new byte[BufferLength];
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
                const int interval = 1000;
                var receivedList = new List<TransferInfo>();
                var lockObj = new object();
                long fetchedSize = 0;
                var lastTime = DateTime.Now;
                var startTime = DateTime.Now;
                DataReceived += b =>
                {
                    lock (lockObj)
                    {
                        receivedList.Add(new TransferInfo(lastTime, DateTime.Now, b));
                    }

                    lastTime = DateTime.Now;
                    fetchedSize += b;
                };

                //int count = 0;
                float avgSec = 5;
                float maxSpd = 0;
                while (!_finishedTokenSource.IsCancellationRequested)
                {
                    if (_cancelTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                    watch.Restart();
                    Thread.Sleep(interval);
                    watch.Stop();
                    var now = DateTime.Now;

                    long total;
                    lock (lockObj)
                    {
                        total = receivedList.Where(k => now - k.EndTime <= TimeSpan.FromSeconds(avgSec)).Sum(k => k.DataLength);
                    }

                    var speed = total / avgSec;
                    if (speed > maxSpd) maxSpd = speed;
                    ProgressChanged?.Invoke(fetchedSize, speed);
                    lock (lockObj)
                    {
                        receivedList.RemoveAll(k => now - k.EndTime > TimeSpan.FromSeconds(avgSec) && k.StartTime != startTime);
                    }
                }

                float totalSec = (float)(receivedList.Max(k => k.EndTime) - receivedList.Min(k => k.StartTime)).TotalSeconds;
                float avgSpd = fetchedSize / totalSec;

                var path = RecommendSavePath;
                int prefix = 2;
                while (File.Exists(path))
                {
                    path = string.Format("{0} ({1}){2}",
                        Path.Combine(Path.GetDirectoryName(RecommendSavePath),
                            Path.GetFileNameWithoutExtension(RecommendSavePath)),
                        prefix,
                        Path.GetExtension(RecommendSavePath));
                    prefix++;
                }
                File.Move(_tempSavePath, path);
                DownloadFinished?.Invoke(totalSec, avgSpd, maxSpd);
            });
        }

        private WebRequest CreateRequestObject()
        {
            var request = WebRequest.Create(Url);
            request.Timeout = 30000;
            if (request is HttpWebRequest httpRequest)
            {
                httpRequest.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36";
                return httpRequest;
            }

            if (request is FileWebRequest fileRequest)
            {
                return fileRequest;
            }

            throw new NotSupportedException();
        }

        private void RaiseUserCancelError()
        {
            RaiseError(new Exception("Download was canceled by user."));
        }

        private void RaiseError(Exception ex)
        {
            _cancelTokenSource?.Cancel();
            ErrorOccured?.Invoke(ex);
        }

        struct TransferInfo
        {
            public TransferInfo(DateTime startTime, DateTime endTime, long dataLength)
            {
                StartTime = startTime;
                EndTime = endTime;
                DataLength = dataLength;
            }

            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public long DataLength { get; set; }
        }
    }
}
