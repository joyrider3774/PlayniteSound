using System.Collections.Generic;
using System.Threading;
using PlayniteSounds.Models;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloader
    {
        string BaseUrl();
        Source DownloadSource();
        IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false);
        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken=default);
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken);
    }
}
