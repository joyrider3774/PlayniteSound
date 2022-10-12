using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System;
using PlayniteSounds.Models;

namespace PlayniteSounds.Downloaders
{
    internal class DownloadManager : IDownloadManager
    {
        private static readonly TimeSpan MaxTime = new TimeSpan(0, 8, 0);
        private static readonly HtmlWeb Web = new HtmlWeb();
        private static readonly HttpClient HttpClient = new HttpClient();
        
        private static readonly List<string> SongTitleEnds = new List<string> { "Theme", "Title", "Menu" };

        private readonly PlayniteSoundsSettings _settings;

        private readonly IDownloader _khDownloader;
        private readonly IDownloader _ytDownloader;

        public DownloadManager(PlayniteSoundsSettings settings)
        {
            _settings = settings;
            _khDownloader = new KhDownloader(HttpClient, Web);
            _ytDownloader = new YtDownloader(HttpClient, _settings);
        }

        public IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, bool auto = false)
        {
            if ((source is Source.All || source is Source.Youtube) && string.IsNullOrWhiteSpace(_settings.FFmpegPath))
            {
                throw new Exception("Cannot download from Youtube without the FFmpeg Path specified in settings.");
            }

            if (source is Source.All)
                    return (_settings.AutoParallelDownload && auto) || (_settings.ManualParallelDownload && !auto)
                        ? _settings.Downloaders.SelectMany(d => GetAlbumFromSource(gameName, d, auto))
                        : _settings.Downloaders.Select(d => GetAlbumFromSource(gameName, d, auto)).FirstOrDefault(dl => dl.Any());

            return SourceToDownloader(source).GetAlbumsForGame(gameName, auto);
        }

        public IEnumerable<Song> GetSongsFromAlbum(Album album)
            => SourceToDownloader(album.Source).GetSongsFromAlbum(album);

        public bool DownloadSong(Song song, string path)
            => SourceToDownloader(song.Source).DownloadSong(song, path);

        public Album BestAlbumPick(IEnumerable<Album> albums, string gameName, string regexGameName)
        {
            var albumsList = albums.ToList();

            if (albumsList.Count is 1)
            {
                return albumsList.First();
            }

            var ostRegex = new Regex($@"{regexGameName}.*(Soundtrack|OST|Score)", RegexOptions.IgnoreCase);
            var ostMatch = albumsList.FirstOrDefault(a => ostRegex.IsMatch(a.Name));
            if (ostMatch != null)
            {
                return ostMatch;
            }

            var exactMatch = albumsList.FirstOrDefault(a => string.Equals(a.Name, gameName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var closeMatch = albumsList.FirstOrDefault(a => a.Name.StartsWith(gameName, StringComparison.OrdinalIgnoreCase));
            return closeMatch ?? albumsList.FirstOrDefault();
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
                case Source.KHInsider: return _khDownloader;
                case Source.Youtube:   return _ytDownloader;
                default: throw new ArgumentException($"Unrecognized download source: {source}");
            }
        }

        private IEnumerable<Album> GetAlbumFromSource(string gameName, Source source, bool auto)
            => SourceToDownloader(source).GetAlbumsForGame(gameName, auto);
    }
}
