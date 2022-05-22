using System;
using System.Collections.Generic;

namespace PlayniteSounds.Models
{
    public class PlayniteSoundsSettings
    {
        public AudioState MusicState { get; set; } = AudioState.Always;
        public AudioState SoundState { get; set; } = AudioState.Always;
        public MusicType MusicType { get; set; } = MusicType.Game;
        public int MusicVolume { get; set; } = 25;
        public bool StopMusic { get; set; } = true;
        public bool SkipFirstSelectSound { get; set; }
        public bool PlayBackgroundWhenNoneSelected { get; set; }
        public bool PauseOnDeactivate { get; set; } = true;
        public bool RandomizeOnEverySelect { get; set; }
        public bool RandomizeOnMusicEnd { get; set; } = true;
        public bool TagMissingEntries { get; set; }
        public bool AutoDownload { get; set; }
        public bool AutoParallelDownload { get; set; }
        public bool ManualParallelDownload { get; set; } = true;
        public bool YtPlaylists { get; set; } = true;
        public string FFmpegPath { get; set; }
        public IList<Source> Downloaders { get; set; } = new List<Source> { Source.Youtube };
        public DateTime LastAutoLibUpdateAssetsDownload { get; set; } = DateTime.Now;
    }
}
