using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteSounds.Models;
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
                var filePath = _plugin.PlayniteApi.Dialogs.SelectFile(string.Empty);
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
        public RelayCommand<object> NavigateUrlCommand
        {
            get => new RelayCommand<object>((url) =>
            {
                _plugin.Try(() => Process.Start((url as Uri).AbsoluteUri));
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
                plugin.HandleException(e);
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
                _plugin.HandleException(e);
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

                _plugin.ReloadMusic = _plugin.ReloadMusic || musicTypeChanged || musicStateChanged; 

                _plugin.UpdateDownloadManager(Settings);
                _plugin.ReplayMusic();
                _plugin.ResetMusicVolume();
            }
            catch (Exception e)
            {
                _plugin.HandleException(e);
            }
           
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            var outcome = true;

            if (!File.Exists(Settings.FFmpegPath))
            {
                errors.Add($"The path to FFmpeg '{Settings.FFmpegPath}' is invalid");
                outcome = false;
            }

            return outcome;
        }
    }
}