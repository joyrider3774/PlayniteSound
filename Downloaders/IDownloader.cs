using Playnite.SDK;
using System.Collections.Generic;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloader
    {
        string BaseUrl();
        IEnumerable<GenericItemOption> GetAlbumsForGame(string gameName);
        IEnumerable<GenericItemOption> GetSongsFromAlbum(GenericItemOption album);
        bool DownloadSong(GenericItemOption song, string path);
    }
}
