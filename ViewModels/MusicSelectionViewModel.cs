using Playnite.SDK;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PlayniteSounds.Downloaders;
using PlayniteSounds.Models;
using PlayniteSounds.Views;
using PlayniteSounds.Players;

namespace PlayniteSounds.ViewModels
{
    class DialogCanceledException : Exception
    {}

    class MusicSelectionViewModel : ObservableObject
    {
        private IPlayniteAPI PlayniteApi { get; }
        readonly private IDownloadManager DownloadManager;

        private IDialogsFactory Dialogs => PlayniteApi.Dialogs;

        private IMusicPlayer _musicPlayer;

        private string _currentlyPreview;
        public string CurrentlyPreview
        {
            get
            {
                return _currentlyPreview;
            }

            set
            {
                _currentlyPreview = value;
                OnPropertyChanged();
            }
        }

        static GlobalProgressActionArgs progressArgs=null;
        static string prefix;

        public static void SetProgress(int pos=0, int max=0, string text=default)
        {
            if (progressArgs is null)
                return;

            if (max > 0)
            {
                progressArgs.IsIndeterminate = false;
                progressArgs.ProgressMaxValue = (double)max;
                progressArgs.CurrentProgressValue = (double)pos;
            }
            progressArgs.Text =
                string.IsNullOrEmpty(text)
                ? $"{prefix} ({pos}/{max})"
                : $"{prefix} {text}";
        }

        private PlayniteSoundsSettings settings;
        private Window window;
        private string searchTerm;
        public string SearchTerm
        {
            get => searchTerm;
            set
            {
                searchTerm = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<GenericItemOption> searchResults = new ObservableCollection<GenericItemOption>();
        public ObservableCollection<GenericItemOption> SearchResults
        {
            get
            {
                return searchResults;
            }

            set
            {
                searchResults = value;
                OnPropertyChanged();
            }
        }

        private List<GenericItemOption> selectedResult = default;
        public List<GenericItemOption> SelectedResult
        {
            get => selectedResult;
            set
            {
                selectedResult = value;
                OnPropertyChanged();
            }
        }
        public bool CancelationRequested { get; private set; } = false;

        public RelayCommand<object> CloseCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                CancelationRequested = true;
                CloseView(false);
            });
        }

        public RelayCommand<object> SkipCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                CloseView(false);
            });
        }


        public RelayCommand<object> PreviewCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                PreviewMusic((a as GenericObjectOption).Object as Song);
            });
        }

        public RelayCommand<object> BackCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                SelectedResult = default;
                CloseView(true);
            });
        }

        public RelayCommand<object> ConfirmCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                ConfirmDialog();
            }, (a) => SelectedResult?.Count > 0);
        }

        public RelayCommand<object> SearchCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                Search();
            }, (a) => !string.IsNullOrEmpty(SearchTerm));
        }

        public RelayCommand<object> ItemDoubleClickCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                SelectedResult = new List<GenericItemOption> { a as GenericItemOption };
                ConfirmDialog();
            });
        }

        public RelayCommand WindowOpenedCommand
        {
            get => new RelayCommand(() => Search());
        }

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly Func<string, CancellationToken, List<GenericItemOption>> searchFunction;

        private bool isSong;
        public bool PreviewEnabled { get => isSong; }
        public SelectionMode SelectionMode { get => isSong ? SelectionMode.Multiple : SelectionMode.Single; }
        public string CheckBoxesVisibility { get => isSong ? "Visible" : "Collapsed"; }
        public string BackVisibility { get => isSong ? "Visible" : "Collapsed"; }
        public string SkipVisibility { get => PlayniteSounds.GlobalStatus.CurrentProgressValue < PlayniteSounds.GlobalStatus.ProgressMaxValue ? "Visible" : "Collapsed"; }
        public string SearchVisibility { get => !isSong ? "Visible" : "Collapsed"; }

        private string title;

        public MusicSelectionViewModel(
            IPlayniteAPI PlayniteApi,
            IDownloadManager DownloadManager,
            PlayniteSoundsSettings settings,
            string title,
            Func<string, CancellationToken, List<GenericItemOption>> searchFunction,
            string defaultSearch = null,
            bool isSong = false,
            List<GenericItemOption> previous = default)
        {
            this.PlayniteApi = PlayniteApi;
            this.DownloadManager = DownloadManager;
            this.settings = settings;
            this.title = title;
            this.searchFunction = searchFunction;
            this.isSong = isSong;

            SearchTerm = defaultSearch;
            if (previous?.Count > 0)
            {
                SearchResults = previous.ToObservable();
            }
        }

        public bool? ShowDialog()
        {
            window = PlayniteApi.Dialogs.CreateWindow( new WindowCreationOptions());
            window.Height = 600;
            window.Width = 800;
            window.Title = title;
            window.Content = new MusicSelectionView();
            window.DataContext = this;
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _musicPlayer = MusicPlayer.Create(settings);
            _musicPlayer.MediaEnded += MediaEnded;

            return window.ShowDialog();
        }

        public void CloseView(bool? result)
        {
            UnmuteMusic();
            window.DialogResult = result;
            window.Close();
        }

        public void Close()
        {
            StopPreview();
            UnmuteMusic();
        }

        public void ConfirmDialog()
        {
            CloseView(true);
        }

        private void StopPreview()
        {
            _musicPlayer.Stop();
            _musicPlayer.Close();
            CurrentlyPreview = null;
        }

        private void UnmuteMusic()
        {
            settings.PreviewIsPlaying = false;
        }

        private void MuteMusic()
        {
            settings.PreviewIsPlaying = true;
        }

        private void PlayPreview(string path)
        {
            MuteMusic();
            CurrentlyPreview = path;
            _musicPlayer.Load(path);
            _musicPlayer.Play();
        }

        private void PreviewMusic(Song song)
        {
            string tempPath = Downloaders.DownloadManager.GetTempPath(song);

            if ( tempPath == CurrentlyPreview)
            {
                StopPreview();
                UnmuteMusic();
                return;
            }

            if (!File.Exists(tempPath))
            {
                bool downloaded = false;

                Dialogs.ActivateGlobalProgress(
                    args => downloaded = DownloadSong(song, null, args),
                    new GlobalProgressOptions(ResourceProvider.GetString("LOCLoadingLabel") + "\n\n" + song.Name, true)
                    );
            }

            StopPreview();

            if (!File.Exists(tempPath))
            {
                logger.Error($"Download failed");
                UnmuteMusic();
                return;
            }
            PlayPreview(tempPath);
        }
        private void MediaEnded(object sender, EventArgs e)
        {
            StopPreview();
            UnmuteMusic();
        }

        public void Search()
        {
            Dialogs.ActivateGlobalProgress(
                args => SearchResults = SearchForResults(SearchTerm, args),
                new GlobalProgressOptions(
                    (PlayniteSounds.GlobalStatus?.Text ?? "") + ": " + ResourceProvider.GetString(!isSong ? "LOC_PLAYNITESOUNDS_SearchingForAlbums" : "LOC_PLAYNITESOUNDS_ReadingSongs")
                    , true
                ));
        }

        private ObservableCollection<GenericItemOption> SearchForResults(string keyword, GlobalProgressActionArgs args)
        {
            List<GenericItemOption> result = null;

            try
            {
                progressArgs = args;
                prefix = args.Text;
                result = searchFunction(keyword, args.CancelToken);
            }
            catch (Exception e)
            {
                PlayniteSounds.HandleException(e, args.CancelToken);
            }

            return result?.ToObservable();
        }
        private bool DownloadSong(Song song, string path, GlobalProgressActionArgs args)
        {
            progressArgs = args;
            prefix = args.Text;
            return DownloadManager.DownloadSong(song, null, args.CancelToken);
        }

    }
}
