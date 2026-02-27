using Playnite.SDK;
using Playnite.SDK.Controls;
using PlayniteSounds.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;

namespace PlayniteSounds.Controls
{
    public partial class MusicControl : PluginUserControl, INotifyPropertyChanged
    {
        static private PlayniteSoundsSettings _settings;

        static List<MusicControl> musicControls = new List<MusicControl>();

        static MusicControl()
        {
            TagProperty.OverrideMetadata(typeof(MusicControl), new FrameworkPropertyMetadata(null, OnTagChanged));
        }

        public MusicControl(PlayniteSoundsSettings settings)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = this;
            _settings = settings;
            _settings.PropertyChanged += OnSettingsChanged;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            musicControls.Add(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            musicControls.AddMissing(this);
            UpdateMute();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            musicControls.Remove(this);
            UpdateMute();
        }

        private static void UpdateMute()
        {
            bool mute = musicControls.Count(c => Convert.ToBoolean(c.Tag)) > 0;
            if (_settings.VideoIsPlaying != mute)
            {
                _settings.VideoIsPlaying = mute;
            }
        }

        private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MusicControl musicControl)
            {
                UpdateMute();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public RelayCommand<bool> VideoPlayingCommand
            => new RelayCommand<bool>(videoIsPlaying => VideoIsPlaying = videoIsPlaying);

        public string CurrentMusicName
        {
            get => _settings.CurrentMusicName;
            set => _settings.CurrentMusicName = value;
        }

        public bool VideoIsPlaying
        {
            get => _settings.VideoIsPlaying;
            set => _settings.VideoIsPlaying = value;
        }

        public void OnSettingsChanged( object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(_settings.VideoIsPlaying))
            {
                OnPropertyChanged(nameof(VideoPlayingCommand));
                OnPropertyChanged(nameof(VideoIsPlaying));
            }
            else if (args.PropertyName == nameof(_settings.CurrentMusicName))
            {
                OnPropertyChanged(nameof(CurrentMusicName));
            }
        }
    }
}
