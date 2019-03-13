using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DownloaderCore
{
    public class Downloader
    {
        public Downloader(string savePath)
        {
            SavePath = savePath;
        }

        public List<DownloadedFileInfo> FinishedList { get; set; } = new List<DownloadedFileInfo>();
        public ConcurrentDictionary<string, ComponentDownloader> TaskList { get; set; } = new ConcurrentDictionary<string, ComponentDownloader>();
        public string SavePath { get; set; }

        public async Task StartNewTaskAsync(string url)
        {
            var downloader = new ComponentDownloader(url,SavePath);
            TaskList.TryAdd(url, downloader);
            await downloader.StartAsync();
        }

        public async Task StopTaskAsync(string url)
        {
            var t = TaskList.FirstOrDefault(k => k.Key == url).Value;
            if (t != null)
            {
                await t.StopAsync();
                TaskList.TryRemove(url, out _);
            }
        }
    }

    public class DownloadedFileInfo
    {
        public DownloadedFileInfo(float savePath, float averageSpeed, float maxSpeed, float takeSeconds, DateTime startDate)
        {
            SavePath = savePath;
            AverageSpeed = averageSpeed;
            MaxSpeed = maxSpeed;
            TakeSeconds = takeSeconds;
            StartDate = startDate;
        }

        public float SavePath { get; set; }
        public float AverageSpeed { get; set; }
        public float MaxSpeed { get; set; }
        public float TakeSeconds { get; set; }
        public DateTime StartDate { get; set; }
    }
}
