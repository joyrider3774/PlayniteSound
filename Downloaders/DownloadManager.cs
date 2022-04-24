using HtmlAgilityPack;
using Playnite.SDK;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System;

namespace PlayniteSounds.Downloaders
{
    internal class DownloadManager : IDownloadManager
    {
        private static readonly HtmlWeb Web = new HtmlWeb();
        private static readonly HttpClient HttpClient = new HttpClient();
        
        private static readonly List<string> SongTitleEnds = new List<string> { "Theme", "Title", "Menu" };

        private readonly IDownloader _khDownloader;

        public DownloadManager()
        {
            _khDownloader = new KHDownloader(HttpClient, Web);
        }

        public string BaseUrl() 
            => _khDownloader.BaseUrl();

        public IEnumerable<GenericItemOption> GetAlbumsForGame(string gameName) 
            => _khDownloader.GetAlbumsForGame(gameName);
        public IEnumerable<GenericItemOption> GetSongsFromAlbum(GenericItemOption album) 
            => _khDownloader.GetSongsFromAlbum(album);
        public bool DownloadSong(GenericItemOption song, string path)
            => _khDownloader.DownloadSong(song, path);

        public GenericItemOption BestAlbumPick(IEnumerable<GenericItemOption> albums, string gameName, string regexGameName)
        {
            var ostRegex = new Regex($@"{regexGameName}.*(Soundtrack|OST|Score)", RegexOptions.IgnoreCase);
            var ostMatch = albums.FirstOrDefault(a => ostRegex.IsMatch(a.Name));
            if (ostMatch != null)
            {
                return ostMatch;
            }

            var exactMatch = albums.FirstOrDefault(a => string.Equals(a.Name, gameName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var closeMatch = albums.FirstOrDefault(a => a.Name.StartsWith(gameName, StringComparison.OrdinalIgnoreCase));
            if (closeMatch != null)
            {
                return closeMatch;
            }

            return albums.FirstOrDefault();
        }

        public GenericItemOption BestSongPick(IEnumerable<GenericItemOption> songs, string regexGameName)
        {
            var titleMatch = songs.FirstOrDefault(s => SongTitleEnds.Any(e => s.Name.EndsWith(e)));
            if (titleMatch != null)
            {
                return titleMatch;
            }

            var nameRegex = new Regex(regexGameName, RegexOptions.IgnoreCase);
            var gameNameMatch = songs.FirstOrDefault(s => nameRegex.IsMatch(s.Name));
            if (gameNameMatch != null)
            {
                return gameNameMatch;
            }

            return songs.FirstOrDefault();
        }
    }
}
