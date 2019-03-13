using DownloaderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CliDownloader
{
    class Program
    {
        static void Main(string[] args)
        {
            var downloader = new ComponentDownloader("https://github.com/Milkitic/Osu-Player/releases/download/2.0.6994.3181/Osu-Player.zip", "E:\\");
            downloader.RequestCreated += Downloader_RequestCreated;
            downloader.ResponseReceived += Downloader_ResponseReceived;
            downloader.DownloadStarted += Downloader_DownloadStarted;
            downloader.ProgressChanged += Downloader_ProgressChanged;
            downloader.DownloadFinished += Downloader_DownloadFinished;
            downloader.ErrorOccured += Downloader_ErrorOccured;
            downloader.NamingConflict += Downloader_NamingConflict;
            downloader.StartAsync().Wait();
            Console.WriteLine("press any key to continue...");
            Console.ReadKey(true);
        }

        private static void Downloader_NamingConflict(object sender, NamingConflictEventArgs e)
        {
            Console.WriteLine("The server returns a file name which is different from the name you requested. Enter 'y' to use server's name.");
            var s = Console.ReadLine();
            if (s?.Trim().ToLower() == "y")
            {
                e.UseServerName = true;
            }
        }

        private static void Downloader_ErrorOccured(Exception ex)
        {
            Console.WriteLine("error occured: \r\n" + ex);
        }

        private static void Downloader_DownloadFinished(float totalTime, float avgSpeed, float maxSpeed)
        {
            Console.WriteLine("download finished, used {0}s amount. average speed: {1}", totalTime, avgSpeed);
        }

        private static void Downloader_DownloadStarted(long totalSize)
        {
            Console.WriteLine("start to fetch data, {0}B amount.", totalSize);
        }

        private static void Downloader_ResponseReceived(string url)
        {
            Console.WriteLine("response from {0}, ready to download.", url);
        }

        private static void Downloader_RequestCreated(string url)
        {
            Console.WriteLine("request to {0}.", url);
        }

        private static void Downloader_ProgressChanged(long fetchedSize, float speed)
        {
            Console.WriteLine("downloaded size: {0} B, speed: {1} B/s.", fetchedSize, speed);
        }
    }
}
