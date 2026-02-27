using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Playnite.SDK;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using PlayniteSounds.Common;

namespace PlayniteSounds.Players
{
    public class WMPMusicPlayer : IMusicPlayer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private MediaPlayer _mediaPlayer;
        private MediaTimeline _timeLine;

        private MediaPlayer _preloadedMediaPlayer = null;
        private MediaTimeline _preloadedTimeLine = null;
        private string _preloadedFile = "";
        private TimeSpan _preloadedPos = default;

        private void OnMediaEnded(object sender, EventArgs e)
        {
            MediaEnded?.Invoke(sender, e);
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {

            if (e.ErrorException.GetType() == typeof(System.Windows.Media.InvalidWmpVersionException))
            {
                Logger.Info($"Windows Media Player Legacy App is not installed. Fallback to SDL");
                throw e.ErrorException;
            }

            MediaFailed?.Invoke(sender, e);
        }

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;
        public double Volume
        {
            get => _mediaPlayer?.Volume ?? 0;
            set
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = value;
                }
            }
        }

        public bool IsLoaded { get => _mediaPlayer?.Clock != null; }

        public bool IsActive { get => _mediaPlayer?.Clock?.CurrentState == ClockState.Active; }

        public string Source { get => _mediaPlayer?.Source?.LocalPath ?? string.Empty; }

        public TimeSpan? CurrentTime { get => _mediaPlayer?.Clock?.CurrentTime ?? TimeSpan.Zero; }

        static public bool WMPIsInstalled()
        {
            try
            {
                Type wmpType = Type.GetTypeFromProgID("MediaPlayer.MediaPlayer.1");
                if (wmpType != null)
                {
                    dynamic wmpObject = Activator.CreateInstance(wmpType);
                    Marshal.ReleaseComObject(wmpObject);
                    return true;
                }
            }
            catch  { }
            return false;
        }
        public WMPMusicPlayer()
        {
            if (!WMPIsInstalled())
            {
                string error = $"Windows Media Player Legacy (App) is not installed.";
                Logger.Trace(error);
                throw new Exception(error);
            }
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
            _timeLine = new MediaTimeline();

            Logger.Trace("Using Windows Media Player Legacy (App)");
        }

        public void Destroy()
        {
            Close();
            _mediaPlayer = null;
        }

        public void Stop()
        {
            _mediaPlayer?.Clock?.Controller?.Stop();
        }

        void SwapMediaPlayers()
        {
            if (_preloadedMediaPlayer == null)
            {
                return;
            }
            MediaPlayer temp = _mediaPlayer;
            _mediaPlayer = _preloadedMediaPlayer;
            _preloadedMediaPlayer = temp;
        }

        void DisposePeloaded()
        {
            if (_preloadedMediaPlayer == null)
            {
                return;
            }
            _preloadedMediaPlayer.MediaEnded -= OnMediaEnded;
            _preloadedMediaPlayer.MediaFailed -= OnMediaFailed;
            _preloadedMediaPlayer.Clock = null;
            _preloadedMediaPlayer.Close();
            _preloadedMediaPlayer = null;
            _preloadedTimeLine = null;
            _preloadedFile = "";
        }

        public void PreLoad(string filePath, TimeSpan startFrom = default)
        {
            DisposePeloaded();
            _preloadedMediaPlayer = new MediaPlayer();
            _preloadedMediaPlayer.MediaEnded += OnMediaEnded;
            _preloadedMediaPlayer.MediaFailed += OnMediaFailed;
            _preloadedMediaPlayer.Volume = 0;
            _preloadedTimeLine = new MediaTimeline(new Uri(filePath));
            _preloadedMediaPlayer.Clock = _preloadedTimeLine.CreateClock();
            _preloadedFile = filePath;

            if (startFrom != default)
            {
                _preloadedMediaPlayer.Clock.Controller.Seek(startFrom, TimeSeekOrigin.BeginTime);
            }
            else
            {
                _preloadedMediaPlayer.Clock.Controller.Seek(TimeSpan.FromSeconds(1), TimeSeekOrigin.BeginTime);
                _preloadedMediaPlayer.Clock.Controller.Seek(startFrom != default ? startFrom: TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            }
            _preloadedMediaPlayer.Clock.Controller.Pause();
            _preloadedPos = startFrom;
        }

        public void Play(TimeSpan startFrom = default)
        {
            if (_preloadedPos != startFrom && _mediaPlayer?.Clock?.IsPaused == true)
            {
                _mediaPlayer?.Clock?.Controller?.Resume();
                _mediaPlayer?.Clock?.Controller?.Seek(startFrom != default ? startFrom: TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            }
            else if (_mediaPlayer?.Clock?.IsPaused == true)
            {
                _mediaPlayer?.Clock?.Controller?.Resume();
            }
            else
            {
                _mediaPlayer?.Clock?.Controller?.Resume();
                _mediaPlayer?.Clock?.Controller?.Seek(startFrom != default ? startFrom: TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            }
            _preloadedPos = default;
        }

        public void Load(string filePath)
        {
            if (_preloadedMediaPlayer != null && _preloadedFile == filePath)
            {
                SwapMediaPlayers();
            }
            else
            {
                _timeLine.Source = new Uri(filePath);
                _mediaPlayer.Clock = _timeLine.CreateClock();
                _preloadedPos = default;
            }
            DisposePeloaded();
        }

        public void Close()
        {
            if (_mediaPlayer?.Clock != null)
            {
                _mediaPlayer.Clock = null;
            }
            _mediaPlayer?.Close();
        }

        public void Pause()
        {
            _mediaPlayer?.Clock?.Controller?.Pause();
        }

        public void Resume()
        {
            _mediaPlayer?.Clock?.Controller?.Resume();
        }

        static private bool EnableDISMFeature(string featureName)
        {
            bool result = false;

            PlayniteSounds.playniteAPI.Dialogs.ActivateGlobalProgress(a =>
                PlayniteSounds.Try(() =>
                {
                    a.ProgressMaxValue = 100;
                    a.CurrentProgressValue = 0;
                    a.Text = $"{ResourceProvider.GetString("LOCSetupRunning")} {featureName}";
                    a.IsIndeterminate = false;

                    result = Dism.EnableFeature(
                        featureName,
                        (progress, message) =>
                        {
                            a.CurrentProgressValue = progress;
                            a.Text = $"{ResourceProvider.GetString("LOCSetupRunning")} {featureName}\n\n{message}: {progress}%";
                        });

                }),
                new GlobalProgressOptions($"{ResourceProvider.GetString("LOCSetupRunning")} {featureName}", false) { IsIndeterminate = false });
            return result;
        }

        static public bool InstallWMP()
        {
            if (WMPIsInstalled())
            {
                Process.Start(@"optionalfeatures.exe");
                return false;
            }

            var optionInstall = new MessageBoxOption("LOCAddonInstall", true, false);
            var optionSettings = new MessageBoxOption("LOCExtensionsBrowse", false, false);
            var optionCancel = new MessageBoxOption(ResourceProvider.GetString("LOCCancelLabel"), false, true);

            var res = PlayniteSounds.playniteAPI.Dialogs.ShowMessage(
                                ResourceProvider.GetString("LOC_PLAYNITESOUNDS_Legacy_WMP_NotInstalled"),
                                ResourceProvider.GetString("LOC_PLAYNITESOUNDS_InstallWMP"),
                                MessageBoxImage.Error,
                                new List<MessageBoxOption>() { optionInstall, optionSettings, optionCancel });

            if (res == optionInstall)
            {

                if (!EnableDISMFeature("WindowsMediaPlayer"))
                {
                    Process.Start(@"optionalfeatures.exe");
                }
                else
                {
                    return true;
                }
            }
            else if (res == optionSettings)
            {
                Process.Start(@"optionalfeatures.exe");
            }

            return false;
        }

    }
}
