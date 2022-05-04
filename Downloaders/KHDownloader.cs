using Playnite.SDK;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using HtmlAgilityPack;
using PlayniteSounds.Common;

namespace PlayniteSounds.Downloaders
{
    internal class KhDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly HttpClient _httpClient;
        private readonly HtmlWeb _web;

        private const string KhInsiderBaseUrl = @"https://downloads.khinsider.com/";
        public string BaseUrl() => KhInsiderBaseUrl;

        public KhDownloader(HttpClient httpClient, HtmlWeb web)
        {
            _httpClient = httpClient;
            _web = web;
        }

        public IEnumerable<GenericItemOption> GetAlbumsForGame(string gameName)
        {
            var htmlDoc = _web.Load($"{KhInsiderBaseUrl}search?search={gameName}");

            var tableRows = htmlDoc.DocumentNode.Descendants("tr");

            var albumsToPartialUrls = new List<GenericItemOption>();
            foreach (var row in tableRows)
            {
                var titleField = row.Descendants("td").Skip(1).FirstOrDefault();
                if (titleField == null)
                {
                    Logger.Info($"Found album entry of game '{gameName}' without title field");
                    continue;
                }

                var htmlLink = titleField.Descendants("a").FirstOrDefault();
                if (htmlLink == null)
                {
                    Logger.Info($"Found entry for album entry of game '{gameName}' without title");
                    continue;
                }

                var albumName = htmlLink.InnerHtml;
                var albumPartialLink = htmlLink.GetAttributeValue("href", null);
                if (albumPartialLink == null)
                {
                    Logger.Info($"Found entry for album '{albumName}' of game '{gameName}' without link in title");
                    continue;
                }

                albumsToPartialUrls.Add(new GenericItemOption(StringUtilities.StripStrings(albumName), albumPartialLink));
            }

            return albumsToPartialUrls;
        }

        public IEnumerable<GenericItemOption> GetSongsFromAlbum(GenericItemOption album)
        {
            var songsToPartialUrls = new List<GenericItemOption>();

            var htmlDoc = _web.Load($"{KhInsiderBaseUrl}{album.Description}");

            // Validate Html
            var headerRow = htmlDoc.GetElementbyId("songlist_header");
            var headers = headerRow.Descendants("th").Select(n => n.InnerHtml);
            if (headers.All(h => !h.Contains("MP3")))
            {
                Logger.Info($"No mp3 in album '{album.Name}'");
                return songsToPartialUrls;
            }

            var table = htmlDoc.GetElementbyId("songlist");

            // Get table and skip header
            var tableRows = table.Descendants("tr").Skip(1).ToList();
            if (tableRows.Count < 2)
            {
                Logger.Info($"No songs in album '{album.Name}'");
                return songsToPartialUrls;
            }

            // Remove footer
            tableRows.RemoveAt(tableRows.Count - 1);

            foreach (var row in tableRows)
            {
                var songNameEntry = row.Descendants("a").Select(
                    r => new GenericItemOption(r.InnerHtml, r.GetAttributeValue("href", null)))
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Description));

                songNameEntry.Name = StringUtilities.StripStrings(songNameEntry.Name);

                songsToPartialUrls.Add(songNameEntry);
            }

            return songsToPartialUrls;
        }

        public bool DownloadSong(GenericItemOption song, string path)
        {
            // Get Url to file from Song html page
            var htmlDoc = _web.Load($"{KhInsiderBaseUrl}{song.Description}");

            var fileUrl = htmlDoc.GetElementbyId("audio").GetAttributeValue("src", null);
            if (fileUrl == null)
            {
                Logger.Info($"Did not find file url for song '{song.Name}'");
                return false;
            }

            var httpMessage = _httpClient.GetAsync(fileUrl).Result;
            using (var fs = File.Create(path))
            {
                httpMessage.Content.CopyToAsync(fs).Wait();
            }

            return true;
        }
    }
}
