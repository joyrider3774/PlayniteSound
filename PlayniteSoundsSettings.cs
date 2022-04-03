using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayniteSounds
{
    public class PlayniteSoundsSettings
    {
        public int MusicWhere { get; set; } = 3;
        public int SoundWhere { get; set; } = 3;
        public int MusicType { get; set; } = 2;
        public int MusicVolume { get; set; } = 25;
        public int StopMusic { get; set; } = 1;
        public bool SkipFirstSelectSound { get; set; } = false;
        public bool PauseOnDeactivate { get; set; } = true;
    }

    public class PlayniteSoundsSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly PlayniteSounds plugin;
        private PlayniteSoundsSettings editingClone { get; set; }

        private PlayniteSoundsSettings settings;
        public PlayniteSoundsSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public PlayniteSoundsSettingsViewModel(PlayniteSounds plugin)
        {
            try
            {
                // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
                this.plugin = plugin;

                // Load saved settings.
                var savedSettings = plugin.LoadPluginSettings<PlayniteSoundsSettings>();

                // LoadPluginSettings returns null if not saved data is available.
                if (savedSettings != null)
                {
                    Settings = savedSettings;
                }
                else
                {
                    Settings = new PlayniteSoundsSettings();
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "PlayniteSoundSettingsViewModel()");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(E.ToString(), Constants.AppName);
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            try
            {
                editingClone = Serialization.GetClone(Settings);
            }
            catch (Exception E)
            {
                logger.Error(E, "BeginEdit()");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(E.ToString(), Constants.AppName);
            }
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            try
            {
                plugin.SavePluginSettings(Settings);
                plugin.MusicNeedsReload = plugin.MusicNeedsReload || ((Settings.MusicType != editingClone.MusicType) || (Settings.MusicWhere != editingClone.MusicWhere));
                plugin.ReplayMusic();
                plugin.ResetMusicVolume();
            }
            catch (Exception E)
            {
                logger.Error(E, "EndEdit()");
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(E.ToString(), Constants.AppName);
            }
           
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }

    }
}