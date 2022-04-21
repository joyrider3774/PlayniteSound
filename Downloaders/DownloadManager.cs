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
        private readonly IDownloader khdownloader;
        
        private Regex songTitleRegex = new Regex(@"(Main )?(Theme|Title|Menu)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public DownloadManager(HttpClient httpClient, HtmlWeb web)
        {
            khdownloader = new KHDownloader(httpClient, web);
        }

        public string BaseUrl() 
            => khdownloader.BaseUrl();

        public IEnumerable<GenericItemOption> GetAlbumsForGame(string gameName) 
            => khdownloader.GetAlbumsForGame(gameName);
        public IEnumerable<GenericItemOption> GetSongsFromAlbum(GenericItemOption album) 
            => khdownloader.GetSongsFromAlbum(album);
        public bool DownloadSong(GenericItemOption song, string path)
            => khdownloader.DownloadSong(song, path);

        public GenericItemOption BestAlbumPick(IEnumerable<GenericItemOption> albums, string gameName, string regexGameName)
        {
            var ostRegex = new Regex($@"{regexGameName}.*(Soundtrack|OST)", RegexOptions.IgnoreCase);
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
            var titleMatch = songs.FirstOrDefault(s => songTitleRegex.IsMatch(s.Name));
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
