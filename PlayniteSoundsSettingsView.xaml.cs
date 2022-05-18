using System.Windows;

namespace PlayniteSounds
{
    public partial class PlayniteSoundsSettingsView
    {
        private readonly PlayniteSounds _plugin;

        public PlayniteSoundsSettingsView(PlayniteSounds plugin)
        {
            _plugin = plugin;
            InitializeComponent();
        }

        private void ButReloadAudio_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ReloadAudioFiles();
        }

        private void ButOpenSoundsFolder_Click(object sender, RoutedEventArgs e)
        {
            _plugin.OpenSoundsFolder();
        }

        private void ButOpenMusicFolder_Click(object sender, RoutedEventArgs e)
        {
            _plugin.OpenMusicFolder();
        }

        private void ButOpenInfo_Click(object sender, RoutedEventArgs e)
        {
            _plugin.HelpMenu();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _plugin.ResetMusicVolume();
        }

        private void ButSaveSounds_Click(object sender, RoutedEventArgs e)
        {
            _plugin.SaveSounds();
        }

        private void ButLoadSounds_Click(object sender, RoutedEventArgs e)
        {
            _plugin.LoadSounds();
        }

        private void ButImportSounds_Click(object sender, RoutedEventArgs e)
        {
            _plugin.ImportSounds();
        }

        private void ButRemoveSounds_Click(object sender, RoutedEventArgs e)
        {
            _plugin.RemoveSounds();
        }

        private void ButOpenSoundManagerFolder_Click(object sender, RoutedEventArgs e)
        {
            _plugin.OpenSoundManagerFolder();
        }
    }
}