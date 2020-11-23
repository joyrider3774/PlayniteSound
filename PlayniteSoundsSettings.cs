using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayniteSounds
{
    public class PlayniteSoundsSettings : ISettings
    {
        private readonly PlayniteSounds plugin;
        private PlayniteSoundsSettings EditDataSettings;
        public int MusicWhere { get; set; } = 3;
        public int SoundWhere { get; set; } = 3; 
        public int MusicType { get; set; } = 2;
        public int MusicVolume { get; set; } = 25;

        // Parameterless constructor must exist if you want to use LoadPluginSettings method.
        public PlayniteSoundsSettings()
        {
        }

        public PlayniteSoundsSettings(PlayniteSounds plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<PlayniteSoundsSettings>();

            // LoadPluginSettings returns null if not saved data is available.
            if (savedSettings != null)
            {
                RestoreSettings(savedSettings);               
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            EditDataSettings = new PlayniteSoundsSettings(plugin);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            RestoreSettings(EditDataSettings);
            plugin.ReplayMusic();
            plugin.ResetMusicVolume();
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(this);
            plugin.MusicNeedsReload = plugin.MusicNeedsReload || ((EditDataSettings.MusicType != MusicType) || (EditDataSettings.MusicWhere != MusicWhere));
            plugin.ReplayMusic();
            plugin.ResetMusicVolume();
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }

        private void RestoreSettings(PlayniteSoundsSettings source)
        {
            MusicWhere = source.MusicWhere;
            MusicType = source.MusicType;
            SoundWhere = source.SoundWhere;
            MusicVolume = source.MusicVolume;
        }
    }
}