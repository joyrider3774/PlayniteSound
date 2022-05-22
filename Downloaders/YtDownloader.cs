using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Playnite.SDK;
using PlayniteSounds.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;
using YoutubeExplode.Search;

namespace PlayniteSounds.Downloaders
{
    internal class YtDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        private readonly YoutubeClient _youtubeClient;
        private readonly PlayniteSoundsSettings _settings;

        public YtDownloader(HttpClient httpClient, PlayniteSoundsSettings settings)
        {
            _youtubeClient = new YoutubeClient(httpClient);
            _settings = settings;
        }

        private const string BaseYtUrl = "https://www.youtube.com";
        public string BaseUrl() => BaseYtUrl;

        private const Source DlSource = Source.Youtube;
        public Source DownloadSource() => DlSource;

        public IEnumerable<Album> GetAlbumsForGame(string gameName, bool auto = false)
            => GetAlbumsFromExplodeApiAsync(gameName, auto).Result;

        public IEnumerable<Song> GetSongsFromAlbum(Album album) 
            => album.Songs ?? GetSongsFromExplodeApiAsync(album).Result;

        public bool DownloadSong(Song song, string path) => DownloadSongExplodeAsync(song, path).Result;

        private async Task<IEnumerable<Album>> GetAlbumsFromExplodeApiAsync(string gameName, bool auto)
        {
            if (auto)
            {
                gameName += " Soundtrack";
            }

            var albums = new List<Album>();
            var videos = new List<Song>();

            var videoResults = _youtubeClient.Search.GetResultBatchesAsync(gameName, SearchFilter.Video);
            var videoEnumerator = videoResults.GetAsyncEnumerator();

            for (var i = 0; i < 1 && await videoEnumerator.MoveNextAsync(); i++)
            {
                var batchOfVideos =
                    from VideoSearchResult videoSearchResult in videoEnumerator.Current.Items
                    select new Song
                    {
                        Name = videoSearchResult.Title,
                        Id = videoSearchResult.Id,
                        Length = videoSearchResult.Duration,
                        Source = DlSource
                    };

                videos.AddRange(batchOfVideos);
                
                await videoEnumerator.MoveNextAsync();
            }

            if (videos.Any()) albums.Add(new Album
            {
                Name = Common.Constants.Resource.YoutubeSearch,
                Songs = videos,
                Source = DlSource
            });

            if (_settings.YtPlaylists)
            {
                var playlistResults = _youtubeClient.Search.GetResultBatchesAsync(gameName, SearchFilter.Playlist);

                var playlistEnumerator = playlistResults.GetAsyncEnumerator();
                for (var i = 0; i < 1 && await playlistEnumerator.MoveNextAsync(); i++)
                {
                    var batchOfPlaylists =
                        from PlaylistSearchResult playlistSearchResult in playlistEnumerator.Current.Items
                        select new Album
                        {
                            Name = playlistSearchResult.Title,
                            Id = playlistSearchResult.Id,
                            Source = DlSource
                        };

                    albums.AddRange(batchOfPlaylists);
                }
            }

            return albums;
        }

        private async Task<IEnumerable<Song>> GetSongsFromExplodeApiAsync(Album album)
        {
            var playlistVideos = new List<YoutubeExplode.Playlists.PlaylistVideo>();
            var videoResults = _youtubeClient.Playlists.GetVideoBatchesAsync(album.Id);

            var videoEnumerator = videoResults.GetAsyncEnumerator();
            while (await videoEnumerator.MoveNextAsync())
            {
                playlistVideos.AddRange(videoEnumerator.Current.Items);
            }

            // These methods currently seem to hang when used, but only if the above code is not executed.
            // Current theory is the use of IAsyncEnumerable, as it targets C# 8.0, but this project targets C# 7.3.
            //var a = await _youtubeClient.Playlists.GetVideoBatchesAsync(album.Id).SelectManyAwait(b => b.Items.ToList());
            // Worked for awhile, but after updating to the latest version of Explode, it began to hang
            // Attempts to revert to prior version of Explode were unsuccessful
            //playlistVideos = (List<YoutubeExplode.Playlists.PlaylistVideo>)await _youtubeClient.Playlists.GetVideosAsync(album.Id);

            // Outright unsupported
            //var look = await _youtubeClient.Playlists.GetVideoBatchesAsync(album.Id).FlattenAsync();

            return playlistVideos.Select(v =>
                new Song
                {
                    Name = v.Title,
                    Id = v.Id,
                    Length = v.Duration,
                    Source = DlSource
                });
        }

        private async Task<bool> DownloadSongExplodeAsync(Song song, string path)
        {
            try
            {
                await _youtubeClient.Videos.DownloadAsync(song.Id, path, o => o.SetFFmpegPath(_settings.FFmpegPath));
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Something went wrong when attempting to download from Youtube with Id '{song.Id}' and Path '{path}'");
                return false;
            }
        }
    }
}
