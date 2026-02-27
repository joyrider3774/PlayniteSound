using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteSounds.Common.Extensions;
using PlayniteSounds.Models;
using PlayniteSounds.Players;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PlayniteSounds
{
    public class PlayniteSoundsSettingsViewModel : ObservableObject, ISettings
    {
        private PlayniteSoundsSettings _settings;
        public PlayniteSoundsSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand<object> BrowseForFFmpegFile
        {
            get => new RelayCommand<object>((a) =>
            {
                var filePath = _plugin.PlayniteApi.Dialogs.SelectFile("ffmpeg|ffmpeg.exe");
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    Settings.FFmpegPath = filePath;
                }
            });
        }

        public RelayCommand<object> BrowseForFFmpegNormalizeFile
        {
            get => new RelayCommand<object>((a) =>
            {
                var filePath = _plugin.PlayniteApi.Dialogs.SelectFile(string.Empty);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    Settings.FFmpegNormalizePath = filePath;
                }
            });
        }

        public RelayCommand InstallWMPCommand
        {
            get => new RelayCommand(() =>
            {
                if (WMPMusicPlayer.InstallWMP())
                {
                    Settings.WMPIsInstalled = true;
                }
            });
        }

        public RelayCommand<object> NavigateUrlCommand
        {
            get => new RelayCommand<object>((url) =>
            {
                PlayniteSounds.Try(() => Process.Start(new Uri(url.ToString()).AbsoluteUri));
            });
        }

        public RelayCommand<object> BrowseForYtDlpFile
        {
            get => new RelayCommand<object>((a) =>
            {
                var filePath = _plugin.PlayniteApi.Dialogs.SelectFile("yt-dlp|yt-dlp.exe");
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    Settings.YtDlpPath = filePath;
                }
            });
        }

        private PlayniteSoundsSettings EditingClone { get; set; }

        private readonly PlayniteSounds _plugin;


        public PlayniteSoundsSettingsViewModel(PlayniteSounds plugin)
        {
            try
            {
                // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
                _plugin = plugin;

                // Load saved settings.
                var savedSettings = plugin.LoadPluginSettings<PlayniteSoundsSettings>();

                // LoadPluginSettings returns null if no saved data is available.
                Settings = savedSettings ?? new PlayniteSoundsSettings();
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e);
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            try
            {
                EditingClone = Serialization.GetClone(Settings);
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e);
            }
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = EditingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            try
            {
                _plugin.SavePluginSettings(Settings);

                var musicTypeChanged = Settings.MusicType != EditingClone.MusicType;
                var musicStateChanged = Settings.MusicState != EditingClone.MusicState;
                var DetailsMusicType = Settings.DetailsMusicType != EditingClone.DetailsMusicType;

                _plugin.ReloadMusic = _plugin.ReloadMusic || musicTypeChanged || musicStateChanged || DetailsMusicType;

                var replayMusic = _plugin.ReloadMusic
                    || Settings.ChoosenMusicType != EditingClone.ChoosenMusicType
                    || Settings.PlayBackupMusic != EditingClone.PlayBackupMusic
                    || Settings.CollectFromGames != EditingClone.CollectFromGames
                    || Settings.CollectFromGamesOnBackup != EditingClone.CollectFromGamesOnBackup;

                if (Settings.UseWMPLegacyApp != EditingClone.UseWMPLegacyApp)
                {
                    _plugin.UpdateMediaPlayer(Settings);
                }
                _plugin.UpdateDownloadManager(Settings);
                if (replayMusic)
                {
                    _plugin.ReplayMusic();
                }
                _plugin.ResetMusicVolume();
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e);
            }

        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            var outcome = true;

            if (!string.IsNullOrEmpty(Settings.FFmpegPath) && !File.Exists(PathExt.GetFullPath(Settings.FFmpegPath)))
            {
                errors.Add($"The path to FFmpeg '{Settings.FFmpegPath}' is invalid");
                outcome = false;
            }

            if (!string.IsNullOrEmpty(Settings.YtDlpPath) && !File.Exists(PathExt.GetFullPath(Settings.YtDlpPath)))
            {
                errors.Add($"The path to Yt-Dlp '{Settings.YtDlpPath}' is invalid");
                outcome = false;
            }

            return outcome;
        }
    }
}