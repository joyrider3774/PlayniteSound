using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System;
using PlayniteSounds.Models;
using System.IO;
using Playnite.SDK;
using System.Diagnostics;
using System.Threading;
using Playnite.SDK.Models;
using PlayniteSounds.Common;

namespace PlayniteSounds.Downloaders
{
    internal class DownloadManager : IDownloadManager
    {
        private static readonly TimeSpan MaxTime = new TimeSpan(0, 8, 0);
        private static readonly HtmlWeb Web = new HtmlWeb();
        private static readonly HttpClient HttpClient = new HttpClient();

        private static readonly List<string> SongTitleEnds = new List<string> { "Theme", "Title", "Menu" };

        private readonly PlayniteSoundsSettings _settings;
        static private string _tempPath;
        private readonly IDownloader _ytDownloader;

        public DownloadManager(PlayniteSoundsSettings settings, string tempPath)
        {
            _settings = settings;
            _tempPath = tempPath;
            _ytDownloader = new YtDownloader(HttpClient, _settings);
            Cleanup();
        }

        public IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false)
        {
            if ((source is Source.All || source is Source.Youtube)
                && (string.IsNullOrWhiteSpace(_settings.FFmpegPath) || string.IsNullOrWhiteSpace(_settings.YtDlpPath)))
            {
                throw new Exception("Cannot download from Youtube without the FFmpeg and YT-DLP Paths specified in settings.");
            }

            if (source is Source.All)
                    return (_settings.AutoParallelDownload && auto) || (_settings.ManualParallelDownload && !auto)
                        ? _settings.Downloaders.SelectMany(d => GetAlbumFromSource(gameName, d, cancellationToken, auto))
                        : _settings.Downloaders.Select(d => GetAlbumFromSource(gameName, d, cancellationToken, auto)).FirstOrDefault(dl => dl.Any());

            return SourceToDownloader(source).GetAlbumsForGame(gameName, cancellationToken, auto);
        }

        public int GetAlbumRelevance(Album album, Game game)
        {
            string gameName = StringUtilities.StripStrings(game.Name);
            Regex ostRegex = new Regex($@"{StringUtilities.ReplaceStrings(gameName)}.*(Soundtrack|OST|Score)", RegexOptions.IgnoreCase);

            return
                   100000 * (ostRegex.IsMatch(album.Name) ? 1 : 0)
                 + 10000 * (string.Equals(album.Name, gameName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                 + 1000 * (album.Name.StartsWith(gameName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                 + 100 *  ((game.Platforms?.Count>0 && !(album.Platforms is null)) ? game.Platforms.Max(gp => album.Platforms.Max(ap => PlatformComparasion(gp.Name, ap))) :0 )
                 + 100 * ((album.Type?.Length>0 && (string.Equals(album.Type, "GameRip", StringComparison.OrdinalIgnoreCase))) ? 1: 0)
                 + 90 * ((album.Type?.Length>0 && (string.Equals(album.Type, "Soundtrack", StringComparison.OrdinalIgnoreCase))) ? 1: 0)
                 + 10 * ((album.Year?.Length > 0 && game.ReleaseYear > 0 && album.Year.Contains(game.ReleaseYear.ToString())) ? 1 : 0);
        }


        public IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken token)
            => SourceToDownloader(album.Source).GetSongsFromAlbum(album, token);

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken)
        {
            string temp = GetTempPath(song);

            if (!string.IsNullOrEmpty(path) && path != temp && File.Exists(temp))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }

            if ( string.IsNullOrEmpty(path) && File.Exists(temp))
            {
                return true;
            }
            if (string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(_tempPath);
            }
            try
            {
                return SourceToDownloader(song.Source).DownloadSong(song, path ?? temp, cancellationToken);
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e, cancellationToken);
            }
            return false;
        }

        static public string GetTempPath(Song song) => Path.Combine(
            _tempPath,
            BitConverter.ToString(
            new System.Security.Cryptography.SHA256Managed()
            .ComputeHash(System.Text.Encoding.UTF8.GetBytes(song.Id))).Replace("-", "")
            + (string.IsNullOrEmpty(System.IO.Path.GetExtension(song.Id)) ? ".mp3" : System.IO.Path.GetExtension(song.Id))
        );

        public void Cleanup()
        {
            if(Directory.Exists(_tempPath))
                 Directory.Delete(_tempPath, true);
        }

        public int PlatformComparasion(string gp, string ap)
        {
            if (string.Equals(gp, ap, StringComparison.OrdinalIgnoreCase)) return 10;

            string gp_abbr = Regex.Replace(gp, @"[^A-Z]", "");
            string ap_abbr = Regex.Replace(ap, @"[^A-Z0-9]", "");
            if (gp_abbr.Length > 2 && gp_abbr == ap_abbr) return 10;

            char[] delimiterChars = { ' ', ',', '.', ':', '\t' , '/' };

            HashSet<string> gp_w = gp.Split(delimiterChars).Select( a => a.ToUpper()).ToHashSet();
            HashSet<string> ap_w = ap.Split(delimiterChars).Select( a => a.ToUpper()).ToHashSet();

            gp_w.IntersectWith(ap_w);

            return gp_w.Count;

        }

        public Album BestAlbumPick(IEnumerable<Album> albums, Game game)
        {

            List<Album> albumsList = albums.ToList();

            if (albumsList.Count is 1)
            {
                return albumsList.First();
            }

            IOrderedEnumerable<Album> sorted = albumsList.OrderByDescending(a => GetAlbumRelevance(a, game));
            return sorted.FirstOrDefault();
        }

        public Song BestSongPick(IEnumerable<Song> songs, string regexGameName)
        {
            var songsList = songs.Where(s => !s.Length.HasValue || s.Length.Value < MaxTime).ToList();

            if (songsList.Count is 1)
            {
                return songsList.First();
            }

            var titleMatch = songsList.FirstOrDefault(s => SongTitleEnds.Any(e => s.Name.EndsWith(e)));
            if (titleMatch != null)
            {
                return titleMatch;
            }

            var nameRegex = new Regex(regexGameName, RegexOptions.IgnoreCase);
            var gameNameMatch = songsList.FirstOrDefault(s => nameRegex.IsMatch(s.Name));
            return gameNameMatch ?? songsList.FirstOrDefault();
        }

        private IDownloader SourceToDownloader(Source source)
        {
            switch (source)
            {
                case Source.Youtube:   return _ytDownloader;
                default: throw new ArgumentException($"Unrecognized download source: {source}");
            }
        }

        private IEnumerable<Album> GetAlbumFromSource(string gameName, Source source,  CancellationToken token, bool auto)
            => SourceToDownloader(source).GetAlbumsForGame(gameName, token, auto );
    }
}
