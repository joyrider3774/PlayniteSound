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
            _khDownloader = new KhDownloader(HttpClient, Web);
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
            var albumsList = albums.ToList();

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

        public GenericItemOption BestSongPick(IEnumerable<GenericItemOption> songs, string regexGameName)
        {
            var songsList = songs.ToList();
            var titleMatch = songsList.FirstOrDefault(s => SongTitleEnds.Any(e => s.Name.EndsWith(e)));
            if (titleMatch != null)
            {
                return titleMatch;
            }

            var nameRegex = new Regex(regexGameName, RegexOptions.IgnoreCase);
            var gameNameMatch = songsList.FirstOrDefault(s => nameRegex.IsMatch(s.Name));
            return gameNameMatch ?? songsList.FirstOrDefault();
        }
    }
}
