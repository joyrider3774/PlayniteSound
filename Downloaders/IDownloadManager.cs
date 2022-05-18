using Playnite.SDK;
using System.Collections.Generic;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloadManager : IDownloader
    {
        GenericItemOption BestAlbumPick(IEnumerable<GenericItemOption> albums, string gameName, string regexGameName);
        GenericItemOption BestSongPick(IEnumerable<GenericItemOption> songs, string regexGameName);
    }
}
