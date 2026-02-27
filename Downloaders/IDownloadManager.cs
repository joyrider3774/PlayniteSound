using System.Collections.Generic;
using System.Threading;
using Playnite.SDK.Models;
using PlayniteSounds.Models;

namespace PlayniteSounds.Downloaders
{
    public interface IDownloadManager
    {
        Album BestAlbumPick(IEnumerable<Album> albums, Game game);
        Song BestSongPick(IEnumerable<Song> songs, string regexGameName);
        IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false);
        int GetAlbumRelevance(Album album, Game game);
        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken);
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken);
        void Cleanup();
    }
}
