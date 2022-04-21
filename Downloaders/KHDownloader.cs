using Playnite.SDK;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using HtmlAgilityPack;
using PlayniteSounds.Common;

namespace PlayniteSounds.Downloaders
{
    internal class KHDownloader : IDownloader
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly HttpClient HttpClient;
        private readonly HtmlWeb Web;

        private const string KHInsiderBaseUrl = @"https://downloads.khinsider.com/";
        public string BaseUrl() => KHInsiderBaseUrl;

        public KHDownloader(HttpClient httpClient, HtmlWeb web)
        {
            HttpClient = httpClient;
            Web = web;
        }

        public IEnumerable<GenericItemOption> GetAlbumsForGame(string gameName)
        {
            var htmlDoc = Web.Load($"{KHInsiderBaseUrl}search?search={gameName}");

            var tableRows = htmlDoc.DocumentNode.Descendants("tr");

            var albumsToPartialUrls = new List<GenericItemOption>();
            foreach (var row in tableRows)
            {
                var titleField = row.Descendants("td").Skip(1).FirstOrDefault();
                if (titleField == null)
                {
                    logger.Info($"Found album entry of game '{gameName}' without title field");
                    continue;
                }

                var htmlLink = titleField.Descendants("a").FirstOrDefault();
                if (htmlLink == null)
                {
                    logger.Info($"Found entry for album entry of game '{gameName}' without title");
                    continue;
                }

                var albumName = htmlLink.InnerHtml;
                var albumPartialLink = htmlLink.GetAttributeValue("href", null);
                if (albumPartialLink == null)
                {
                    logger.Info($"Found entry for album '{albumName}' of game '{gameName}' without link in title");
                    continue;
                }

                albumsToPartialUrls.Add(new GenericItemOption(StringManipulation.StripStrings(albumName), albumPartialLink));
            }

            return albumsToPartialUrls;
        }

        public IEnumerable<GenericItemOption> GetSongsFromAlbum(GenericItemOption album)
        {
            var songsToPartialUrls = new List<GenericItemOption>();

            var htmlDoc = Web.Load($"{KHInsiderBaseUrl}{album.Description}");

            // Validate Html
            var headerRow = htmlDoc.GetElementbyId("songlist_header");
            var headers = headerRow.Descendants("th").Select(n => n.InnerHtml);
            if (headers.All(h => !h.Contains("MP3")))
            {
                logger.Info($"No mp3 in album '{album.Name}'");
                return songsToPartialUrls;
            }

            var table = htmlDoc.GetElementbyId("songlist");

            // Get table and skip header
            var tableRows = table.Descendants("tr").Skip(1).ToList();
            if (tableRows.Count < 2)
            {
                logger.Info($"No songs in album '{album.Name}'");
                return songsToPartialUrls;
            }

            // Remove footer
            tableRows.RemoveAt(tableRows.Count - 1);

            foreach (var row in tableRows)
            {
                var songNameEntry = row.Descendants("a").Select(
                    r => new GenericItemOption(r.InnerHtml, r.GetAttributeValue("href", null)))
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Description));

                songNameEntry.Name = StringManipulation.StripStrings(songNameEntry.Name);

                songsToPartialUrls.Add(songNameEntry);
            }

            return songsToPartialUrls;
        }

        public bool DownloadSong(GenericItemOption song, string path)
        {
            // Get Url to file from Song html page
            var htmlDoc = Web.Load($"{KHInsiderBaseUrl}{song.Description}");

            var fileUrl = htmlDoc.GetElementbyId("audio").GetAttributeValue("src", null);
            if (fileUrl == null)
            {
                logger.Info($"Did not find file url for song '{song.Name}'");
                return false;
            }

            var httpMesage = HttpClient.GetAsync(fileUrl).Result;
            using (FileStream fs = File.Create(path))
            {
                httpMesage.Content.CopyToAsync(fs).Wait();
            }

            return true;
        }
    }
}
