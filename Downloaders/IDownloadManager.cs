using System.Collections.Generic;
using PlayniteSounds.Models;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloadManager
    {
        Album BestAlbumPick(IEnumerable<Album> albums, string gameName, string regexGameName);
        Song BestSongPick(IEnumerable<Song> songs, string regexGameName);
        IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, bool auto = false);
        IEnumerable<Song> GetSongsFromAlbum(Album album);
        bool DownloadSong(Song song, string path);
    }
}
