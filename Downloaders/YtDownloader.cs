using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Playnite.SDK;
using PlayniteSounds.Models;
using PlayniteSounds.Common;
using PlayniteSounds.ViewModels;
using PlayniteSounds.Common.Extensions;


namespace PlayniteSounds.Downloaders
{
    internal class YtDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly PlayniteSoundsSettings _settings;
        private readonly HttpClient _httpClient;

        public YtDownloader(HttpClient httpClient, PlayniteSoundsSettings settings)
        {
            _settings = settings;
            _httpClient = httpClient;
        }

        private const string BaseYtUrl = "https://www.youtube.com";
        public string BaseUrl() => BaseYtUrl;

        private const string youtubeDLArg = "-x --audio-format mp3 --audio-quality 0 --ffmpeg-location=\"{0}\" -o \"{3}\" {1}/watch?v={2}";

        private const Source DlSource = Source.Youtube;
        public Source DownloadSource() => DlSource;

        public IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false)
            => GetAlbumsFromYoutubeClient(gameName, cancellationToken, auto);

        public IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken = default)
            => album.Songs ?? GetSongsFromYoutubeClient(album, cancellationToken) ?? new List<Song>();

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken) => DownloadSongFromYoutubeDl(song, path, cancellationToken);

        private IEnumerable<Album> GetAlbumsFromYoutubeClient(string gameName, CancellationToken cancellationToken, bool auto)
        {
            try
            {
                return new YoutubeClient(_httpClient)
                    .Search(gameName + (auto ? " Soundtrack" : ""), 100, cancellationToken)
                    .Select(x =>
                        new Album
                        {
                            Name = x.Title,
                            Id = x.Id,
                            Source = Source.Youtube,
                            IconUrl = x.ThumbnailUrl.ToString(),
                            Count = x.Count
                        })
                    .ToList();
            }
            catch {}

            return null;
        }

        private IEnumerable<Song> GetSongsFromYoutubeClient(Album album, CancellationToken cancellationToken)
        {
            try
            {
                return new YoutubeClient(_httpClient)
                    .GetPlaylist(album.Id, cancellationToken)
                    .Select(x => new Song
                    {
                        Name = x.Title,
                        Id = x.Id,
                        Length = x.Duration,
                        Source = DlSource,
                        IconUrl = x.ThumbnailUrl.ToString()
                    });
            }
            catch {}
            return null;
        }

        private static void UpdateProgress(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return;

            Logger.Debug(stdout);

            Regex filter = new Regex(@"^\[download\]\s+(.*)\s*$");
            MatchCollection lines = filter.Matches(stdout);
            foreach (Match line in lines)
            {
                string progress = line.Groups[1].Value;
                int? percent = null;
                Regex regex = new Regex(@"([\d]+).*%.*");
                MatchCollection matches = regex.Matches(progress);
                if (matches.Count > 0 && matches[0].Groups.Count > 1)
                {
                    percent = int.Parse(matches[0].Groups[1].Value);
                }
                MusicSelectionViewModel.SetProgress(
                    percent ?? 0,
                    percent == null ? 0 : 100,
                    text: "\n\n" + progress
                );
            };
        }

        private bool DownloadSongFromYoutubeDl(Song song, string path, CancellationToken cancellationToken)
        {
            string downloader = PathExt.GetFullPath(_settings.YtDlpPath);
            string arguments = string.Format(youtubeDLArg, PathExt.GetFullPath(_settings.FFmpegPath), BaseUrl(), song.Id, path);
            string workDir = Path.GetDirectoryName(downloader);

            try
            {
                Logger.Debug($"Starting downloader: {downloader}, {arguments}, {workDir}");
                var startupPath = downloader;

                var info = new ProcessStartInfo(startupPath)
                {
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrEmpty(workDir) ? (new FileInfo(startupPath)).Directory.FullName : workDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                int result = 0;
                MusicSelectionViewModel.SetProgress(text: "\n\n");
                using (var proc = Process.Start(info))
                {
                    proc.OutputDataReceived += (sender, args) => UpdateProgress(args.Data);
                    proc.BeginOutputReadLine();

                    while (!proc.WaitForExit(100))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {

                            proc.CancelOutputRead();
                            ProcessTreeKiller.Kill(proc);
                            proc.WaitForExit();
                            throw new Exception("Cancelled");
                        }
                    }

                    result = proc.ExitCode;
                    if (result != 0)
                    {
                        string errorOutput = proc.StandardError.ReadToEnd();
                        Logger.Error($"Something went wrong when attempting to download from Youtube with Id '{song.Id}' and Path '{path}'. Error {errorOutput}");
                        throw new Exception($"Fail to download.\n\n{errorOutput}");
                    }
                }
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e, cancellationToken);
                return false;
            }
            return true;
        }
    }
}
