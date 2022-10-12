using System.Collections.Generic;
using PlayniteSounds.Models;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloader
    {
        string BaseUrl();
        Source DownloadSource();
        IEnumerable<Album> GetAlbumsForGame(string gameName, bool auto = false);
        IEnumerable<Song> GetSongsFromAlbum(Album album);
        bool DownloadSong(Song song, string path);
    }
}
