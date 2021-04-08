using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PlayniteSounds
{
    public partial class PlayniteSoundsSettingsView : UserControl
    {
        private readonly PlayniteSounds plugin;

        public PlayniteSoundsSettingsView(PlayniteSounds plugin)
        {
            this.plugin = plugin;
            InitializeComponent();
        }

        private void ButReloadAudio_Click(object sender, RoutedEventArgs e)
        {
            plugin.ReloadAudioFiles();
        }

        private void ButOpenSoundsFolder_Click(object sender, RoutedEventArgs e)
        {
            plugin.OpenSoundsFolder();
        }

        private void ButOpenMusicFolder_Click(object sender, RoutedEventArgs e)
        {
            plugin.OpenMusicFolder();
        }

        private void ButOpenInfo_Click(object sender, RoutedEventArgs e)
        {
            plugin.HelpMenu();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            plugin.ResetMusicVolume();
        }

        private void ButSaveSounds_Click(object sender, RoutedEventArgs e)
        {
            plugin.SaveSounds();
        }

        private void ButLoadSounds_Click(object sender, RoutedEventArgs e)
        {
            plugin.LoadSounds();
        }

        private void ButImportSounds_Click(object sender, RoutedEventArgs e)
        {
            plugin.ImportSounds();
        }

        private void ButRemoveSounds_Click(object sender, RoutedEventArgs e)
        {
            plugin.RemoveSounds();
        }

        private void ButOpenSoundManagerFolder_Click(object sender, RoutedEventArgs e)
        {
            plugin.OpenSoundManagerFolder();
        }
    }
}