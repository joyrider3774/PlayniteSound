using Playnite.SDK.Data;
using PlayniteSounds.Players;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Windows.Media.TextFormatting;

namespace PlayniteSounds.Models
{
    public class PlayniteSoundsSettings: ObservableObject
    {
        private AudioState _musicState = AudioState.Always;
        public AudioState MusicState
        {
            get => _musicState;
            set
            {
                _musicState = value;
                OnPropertyChanged();
                OnPropertyChanged("DetailsMusicTypeVisibility");
            }
        }
        [DontSerialize]
        public bool MusicOnFullScreen { get => MusicState == AudioState.Always || MusicState == AudioState.Fullscreen; }
        public AudioState SoundState { get; set; } = AudioState.Always;

        private MusicType _musicType = MusicType.Game;
        public MusicType MusicType
        {
            get => _musicType;
            set
            {
                _musicType = value;
                OnPropertyChanged();
                OnPropertyChanged("DetailsMusicTypeVisibility");
            }
        }

        public MusicType DetailsMusicType { get; set; } = MusicType.Same;

        public MusicType ChoosenMusicType
        {
            get => !GameDetailsVisible ? MusicType : DetailsMusicType == MusicType.Same ? MusicType : DetailsMusicType;
        }

        [DontSerialize]
        public string DetailsMusicTypeVisibility
        {
            get =>(
                MusicState != AudioState.Never
             && MusicState != AudioState.Desktop
             ) ? "Visible" : "Collapsed"
            ;
        }

        [DontSerialize]
        public int MusicVolume
        {
            get => (int)(100 * PlayniteSounds.FullscreenSettings.BackgroundVolume);
            set
            {
                PlayniteSounds.FullscreenSettings.BackgroundVolume = (float)value / 100;
                OnPropertyChanged();
            }
        }

        private double fadeDuration = 0.5;
        public double FadeDuration
        {
            get => fadeDuration;
            set => SetValue(ref fadeDuration, Math.Round(value, 2));
        }

        public bool StopMusic { get; set; } = true;
        public bool SkipFirstSelectSound { get; set; }

        private bool playBackupMusic = false;
        public bool PlayBackupMusic
        {
            get => playBackupMusic;
            set
            {
                playBackupMusic = value;
                OnPropertyChanged();
            }
        }

        private bool useWMPLegacyApp = true;
        public bool UseWMPLegacyApp
        {
            get => useWMPLegacyApp;
            set
            {
                WMPIsInstalled = WMPMusicPlayer.WMPIsInstalled();
                useWMPLegacyApp = WMPMusicPlayer.WMPIsInstalled() && value;
                OnPropertyChanged();
            }
        }

        [DontSerialize]
        public bool WMPIsInstalled
        {
            get => WMPMusicPlayer.WMPIsInstalled();
            set => OnPropertyChanged();
        }

        [DontSerialize]
        public bool PauseOnDeactivate
        {
            get => PlayniteSounds.FullscreenSettings.MuteInBackground;
            set { PlayniteSounds.FullscreenSettings.MuteInBackground = value; }
        }

        public bool SkipFirstSelectMusic { get; set; }
        public bool PauseNotInLibrary { get; set; }
        public bool RandomizeOnEverySelect { get; set; }
        public bool RandomizeOnMusicEnd { get; set; } = true;
        public bool TagMissingEntries { get; set; }
        public bool AutoDownload { get; set; }
        public bool AutoParallelDownload { get; set; }
        public bool ManualParallelDownload { get; set; } = true;
        public bool YtPlaylists { get; set; } = true;
        public bool NormalizeMusic { get; set; } = true;
        public bool TagNormalizedGames { get; set; }

        [DontSerialize]
        private string ytDlpPath { get; set; }
        public string YtDlpPath {
            get => ytDlpPath;
            set
            {
                ytDlpPath = value;
                OnPropertyChanged();
            }
        }

        [DontSerialize]
        private string ffmpegPath { get; set; }
        public string FFmpegPath
        {
            get => ffmpegPath;
            set
            {
                ffmpegPath = value;
                OnPropertyChanged();
            }
        }

        [DontSerialize]
        private string ffmpegNormalizePath { get; set; }
        public string FFmpegNormalizePath
        {
            get => ffmpegNormalizePath;
            set
            {
                ffmpegNormalizePath = value;
                OnPropertyChanged();
            }
        }


        public string FFmpegNormalizeArgs { get; set; }
        [DontSerialize]
        public IList<Source> Downloaders { get; set; } = new List<Source> { Source.Youtube };
        public DateTime LastAutoLibUpdateAssetsDownload { get; set; } = DateTime.Now;
        public bool PromptedForMigration { get; set; }

        [DontSerialize]
        private bool videoIsPlaying { get; set; }
        [DontSerialize]
        public bool VideoIsPlaying
        {
            get => videoIsPlaying;
            set {
                videoIsPlaying = value;
                OnPropertyChanged();
            }
        }

        [DontSerialize]
        private string currentMusicName { get; set; } = string.Empty;
        [DontSerialize]
        public string CurrentMusicName
        {
            get => currentMusicName;
            set {
                currentMusicName = value;
                OnPropertyChanged();
            }
        }

        public bool PauseOnTrailer { get; set; } = true;

        public bool CollectFromGames { get; set; } = false;
        public bool CollectFromGamesOnBackup { get; set; } = false;

        [DontSerialize]
        private bool previewIsPlaying { get; set; }
        [DontSerialize]
        public bool PreviewIsPlaying
        {
            get => previewIsPlaying;
            set {
                previewIsPlaying = value;
                OnPropertyChanged();
            }
        }

        [DontSerialize]
        public bool GameDetailsVisible { get; set; } = false;
        public bool RestartBackupMusic { get; set; } = false;
    }
}
