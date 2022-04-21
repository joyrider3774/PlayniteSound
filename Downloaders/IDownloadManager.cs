using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayniteSounds.Downloaders
{
    internal interface IDownloadManager : IDownloader
    {
        GenericItemOption BestAlbumPick(IEnumerable<GenericItemOption> albums, string gameName, string regexGameName);
        GenericItemOption BestSongPick(IEnumerable<GenericItemOption> songs, string regexGameName);
    }
}
