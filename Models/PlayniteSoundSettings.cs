using System;

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
        public DateTime LastAutoLibUpdateAssetsDownload { get; set; } = DateTime.Now;
    }
}
