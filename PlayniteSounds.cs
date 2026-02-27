using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Media;
using System.ComponentModel;
using Playnite.SDK.Events;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.IO.Compression;
using System.Threading;
using PlayniteSounds.Downloaders;
using PlayniteSounds.Common;
using PlayniteSounds.Common.Constants;
using PlayniteSounds.Models;
using PlayniteSounds.Controls;
using PlayniteSounds.ViewModels;
using PlayniteSounds.Players;
using PlayniteSounds.Monitors;
using System.Threading.Tasks;
using PlayniteSounds.Common.Extensions;

namespace PlayniteSounds
{
    class SDL_mixer
    {
        const string nativeLibName = "SDL2_mixer";

        [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HaltMusic();
        [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mix_FreeMusic(IntPtr music);
    }

    public class PlayniteSounds : GenericPlugin
    {
        public bool ReloadMusic { get; set; }

        public static IPlayniteAPI playniteAPI;

        private static readonly string PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string IconPath = Path.Combine(PluginFolder, "icon.png");

        private static readonly Lazy<string> HelpMessage = new Lazy<string>(() =>
                    Resource.MsgHelp1 + "\n\n" +
                    Resource.MsgHelp2 + "\n\n" +
                    Resource.MsgHelp3 + " " +
                    Resource.MsgHelp4 + " " +
                    Resource.MsgHelp5 + "\n\n" +
                    Resource.MsgHelp6 + "\n\n" +
                    HelpLine(SoundFile.BaseApplicationStartedSound) +
                    HelpLine(SoundFile.BaseApplicationStoppedSound) +
                    HelpLine(SoundFile.BaseGameInstalledSound) +
                    HelpLine(SoundFile.BaseGameSelectedSound) +
                    HelpLine(SoundFile.BaseGameStartedSound) +
                    HelpLine(SoundFile.BaseGameStartingSound) +
                    HelpLine(SoundFile.BaseGameStoppedSound) +
                    HelpLine(SoundFile.BaseGameUninstalledSound) +
                    HelpLine(SoundFile.BaseLibraryUpdatedSound) +
                    Resource.MsgHelp7);

        private static readonly ILogger Logger = LogManager.GetLogger();

        private IDownloadManager DownloadManager;

        public PlayniteSoundsSettingsViewModel SettingsModel { get; }

        private bool _gameRunning;
        private bool _musicEnded;
        private bool _firstSelect = true;
        private bool _closeAudioFilesNextPlay;

        private string _prevMusicFileName = string.Empty;  //used to prevent same file being restarted

        private readonly string _extraMetaDataFolder;
        private readonly string _musicFilesDataPath;
        private readonly string _soundFilesDataPath;
        private readonly string _soundManagerFilesDataPath;
        private readonly string _defaultMusicPath;
        private readonly string _gameMusicFilePath;
        private readonly string _platformMusicFilePath;
        private readonly string _filterMusicFilePath;

        private readonly Dictionary<string, PlayerEntry> _players = new Dictionary<string, PlayerEntry>();

        private IMusicPlayer _musicPlayer;
        private MusicFader _musicFader;


        private readonly List<GameMenuItem> _gameMenuItems;
        private readonly List<MainMenuItem> _mainMenuItems;

        private ISet<string> _pausers = new HashSet<string>();
        static public dynamic FullscreenSettings;

        #region Constructor

        public PlayniteSounds(IPlayniteAPI api) : base(api)
        {
            playniteAPI = api;
            try
            {
                FullscreenSettings = PlayniteApi.ApplicationSettings.Fullscreen
                    .GetType()
                    .GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(PlayniteApi.ApplicationSettings.Fullscreen);

                SoundFile.ApplicationInfo = PlayniteApi.ApplicationInfo;

                _extraMetaDataFolder = Path.Combine(api.Paths.ConfigurationPath, SoundDirectory.ExtraMetaData);

                _musicFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Music);

                _soundFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Sound);
                _soundManagerFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.SoundManager);

                _defaultMusicPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Default);
                Directory.CreateDirectory(_defaultMusicPath);

                _platformMusicFilePath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Platform);
                Directory.CreateDirectory(_platformMusicFilePath);

                _filterMusicFilePath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Filter);
                Directory.CreateDirectory(_filterMusicFilePath);

                _gameMusicFilePath = Path.Combine(_extraMetaDataFolder, SoundDirectory.GamesFolder);
                Directory.CreateDirectory(_gameMusicFilePath);

                SettingsModel = new PlayniteSoundsSettingsViewModel(this);
                SettingsModel.Settings.PropertyChanged += OnSettingsChanged;
                Properties = new GenericPluginProperties
                {
                    HasSettings = true
                };

                Localization.SetPluginLanguage(PluginFolder, api.ApplicationSettings.Language);
                _musicPlayer = MusicPlayer.Create(Settings);
                _musicPlayer.MediaEnded += MediaEnded;
                _musicPlayer.MediaFailed += MediaFailed;
                _musicFader = new MusicFader(_musicPlayer, Settings);


                _gameMenuItems = new List<GameMenuItem>
                {
                    ConstructGameMenuItem(Resource.Youtube, _ => DownloadMusicForSelectedGames(Source.Youtube), "|" + Resource.Actions_Download),
                    ConstructGameMenuItem(Resource.ActionsCopySelectMusicFile, SelectMusicForSelectedGames),
                    ConstructGameMenuItem(Resource.ActionsOpenSelected, OpenMusicDirectory),
                    ConstructGameMenuItem(Resource.ActionsDeleteSelected, DeleteMusicDirectories),
                    ConstructGameMenuItem(Resource.Actions_Normalize, CreateNormalizationDialogue),
                };

                _mainMenuItems = new List<MainMenuItem>
                {
                    ConstructMainMenuItem(Resource.ActionsOpenMusicFolder, OpenMusicFolder),
                    ConstructMainMenuItem(Resource.ActionsOpenSoundsFolder, OpenSoundsFolder),
                    ConstructMainMenuItem(Resource.ActionsReloadAudioFiles, ReloadAudioFiles),
                    ConstructMainMenuItem(Resource.ActionsHelp, HelpMenu),
                    new MainMenuItem { Description = "-", MenuSection = App.MainMenuName },
                    ConstructMainMenuItem(Resource.ActionsCopySelectMusicFile, SelectMusicForDefault, "|" + Resource.ActionsDefault),
                };

                DownloadManager = new DownloadManager(Settings, Path.Combine(_musicFilesDataPath,"tmp"));

                PlayniteApi.Database.Games.ItemCollectionChanged += CleanupDeletedGames;
                PlayniteApi.Database.Platforms.ItemCollectionChanged += UpdatePlatforms;
                PlayniteApi.Database.FilterPresets.ItemCollectionChanged += UpdateFilters;
                PlayniteApi.UriHandler.RegisterSource("Sounds", HandleUriEvent);

                #region Control constructor

                AddCustomElementSupport(new AddCustomElementSupportArgs
                {
                    SourceName = "Sounds",
                    ElementList = new List<string> { "MusicControl" }
                });
                MenuWindowMonitor.Attach(PlayniteApi, SettingsModel.Settings);

                #endregion
                AddSettingsSupport(new AddSettingsSupportArgs
                {
                    SourceName = "Sounds",
                    SettingsRoot = $"{nameof(SettingsModel)}.{nameof(SettingsModel.Settings)}"
                });
                if (SettingsModel.Settings.PauseOnTrailer)
                    MediaElementsMonitor.Attach(PlayniteApi, SettingsModel.Settings);

                //if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                {
                    (GetMainModel() as ObservableObject).PropertyChanged += OnMainModelChanged;
                }
                SupressNativeFulscreenMusic();


            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void OnMainModelChanged( object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "GameDetailsVisible")
            {
                SettingsModel.Settings.GameDetailsVisible = GetMainModel().GameDetailsVisible;
                if ( SettingsModel.Settings.DetailsMusicType != MusicType.Same
                  && SettingsModel.Settings.DetailsMusicType != SettingsModel.Settings.MusicType)
                {
                    ReplayMusic();
                }
            }
            if (args.PropertyName == "ActiveView" && SettingsModel.Settings.PauseNotInLibrary)
            {
                PauseOrResumeMusic();
            }
        }

        private dynamic GetMainModel()
        {
            return PlayniteApi.MainView
                .GetType()
                .GetField("mainModel", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(PlayniteApi.MainView);
        }


        private void SupressNativeFulscreenMusic()
        {
            if ( PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Fullscreen
                || SettingsModel.Settings.MusicState == AudioState.Desktop)
                return;

            dynamic backgroundMusicProperty = GetMainModel().App
                .GetType()
                .GetProperty("BackgroundMusic");

            IntPtr currentMusic = (IntPtr)backgroundMusicProperty.GetValue(null);
            if (currentMusic != new IntPtr(0))
            {
                // stop music
                SDL_mixer.Mix_HaltMusic();
                SDL_mixer.Mix_FreeMusic(currentMusic);
                backgroundMusicProperty.GetSetMethod(true).Invoke(null, new[] { new IntPtr(0) as object});
            }
        }
        private void Fullscreen_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FullscreenSettings.BackgroundVolume))
            {
                ResetMusicVolume();
            }
        }

        public void UpdateDownloadManager(PlayniteSoundsSettings settings)
            => DownloadManager = new DownloadManager(settings, Path.Combine(_musicFilesDataPath,"tmp"));

        public void UpdateMediaPlayer(PlayniteSoundsSettings settings)
        {
            if (settings.UseWMPLegacyApp && !WMPMusicPlayer.WMPIsInstalled())
            {
                return;
            }
            TimeSpan lastPos = _musicPlayer?.CurrentTime ?? default;

            CloseAudioFiles();
            SubCloseMusic();

            if (_musicPlayer != null)
            {
                _musicPlayer.MediaEnded -= MediaEnded;
                _musicPlayer.MediaFailed -= MediaFailed;
            }

            _musicFader?.Destroy();

            _musicPlayer = MusicPlayer.Create(Settings);
            _musicPlayer.MediaEnded += MediaEnded;
            _musicPlayer.MediaFailed += MediaFailed;
            _musicFader = new MusicFader(_musicPlayer, Settings);

            if (_prevMusicFileName != string.Empty)
            {
                Try(() => SubPlayMusicFromPath(_prevMusicFileName, lastPos));
            }
        }


        private static string HelpLine(string baseMessage)
            => $"{SoundFile.DesktopPrefix}{baseMessage} - {SoundFile.FullScreenPrefix}{baseMessage}\n";

        #region Control registration

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            var strArgs = args.Name.Split('_');

            var controlType = strArgs[0];

            switch (controlType)
            {
                case "MusicControl":
                    return new MusicControl(SettingsModel.Settings);
                default:
                    throw new ArgumentException($"Unrecognized controlType '{controlType}' for request '{args.Name}'");
            }
        }

        #endregion


        #endregion

        #region Playnite Interface

        public override Guid Id { get; } = Guid.Parse("9c960604-b8bc-4407-a4e4-e291c6097c7d");

        public override ISettings GetSettings(bool firstRunSettings) => SettingsModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new PlayniteSoundsSettingsView(this);

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
            => PlaySoundFileFromName(SoundFile.GameInstalledSound);

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
            => PlaySoundFileFromName(SoundFile.GameUninstalledSound);

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            if (!(_firstSelect && Settings.SkipFirstSelectSound))
            {
                PlaySoundFileFromName(SoundFile.GameSelectedSound);
            }

            if (!(_firstSelect && Settings.SkipFirstSelectMusic))
            {
                PlayMusicBasedOnSelected();
            }
            _firstSelect = false;
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (Settings.StopMusic)
            {
                PauseMusic();
                _gameRunning = true;
            }

            PlaySoundFileFromName(SoundFile.GameStartedSound, true);
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            if (!Settings.StopMusic)
            {
                PauseMusic();
                _gameRunning = true;
            }

            PlaySoundFileFromName(SoundFile.GameStartingSound);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            _gameRunning = false;
            PlaySoundFileFromName(SoundFile.GameStoppedSound);
            ResumeMusic();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // One-time operations
            UpdateFromLegacyVersion();
            CopyAudioFiles();

            PlaySoundFileFromName(SoundFile.ApplicationStartedSound);

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Application.Current.MainWindow.StateChanged += OnWindowStateChanged;
            Application.Current.Deactivated += OnApplicationDeactivate;
            Application.Current.Activated += OnApplicationActivate;

            dynamic ctx = Application.Current.MainWindow.DataContext;
            (ctx.AppSettings.Fullscreen as INotifyPropertyChanged).PropertyChanged += Fullscreen_PropertyChanged;

            // Application.Current.MainWindow.KeyDown += (_, e) =>
            // {
            //     if (e.Key == Key.MediaNextTrack)
            //     {
            //         PlayMusicBasedOnSelected();
            //         e.Handled = true;
            //     }
            // };
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Add code to be executed when Playnite is shutting down.
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Application.Current.Deactivated -= OnApplicationDeactivate;
            Application.Current.Activated -= OnApplicationActivate;

            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged -= OnWindowStateChanged;
            }

            PlaySoundFileFromName(SoundFile.ApplicationStoppedSound, true);
            CloseAudioFiles();
            CloseMusic();

            if (_musicPlayer != null)
            {
                _musicPlayer.MediaEnded -= MediaEnded;
                _musicPlayer.MediaFailed -= MediaFailed;
            }

            _musicFader?.Destroy();
            _musicFader = null;
            _musicPlayer = null;
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            PlaySoundFileFromName(SoundFile.LibraryUpdatedSound);

            if (Settings.AutoDownload)
            {
                var games = PlayniteApi.Database.Games
                    .Where(x => x.Added != null && x.Added > Settings.LastAutoLibUpdateAssetsDownload);
                MuteExceptions();
                CreateDownloadDialogue(games, Source.All);
                UnMuteExceptions();
            }

            Settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
            SavePluginSettings(Settings);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var gameMenuItems = new List<GameMenuItem>();

            gameMenuItems.AddRange(_gameMenuItems);

            if (SingleGame())
            {
                var files = Directory.GetFiles(CreateMusicDirectory(SelectedGames.First()));
                if (files.Any())
                {
                    gameMenuItems.Add(new GameMenuItem { Description = "-", MenuSection = App.AppName });
                    gameMenuItems.AddRange(ConstructItems(ConstructGameMenuItem, files, "|", true));
                }
            }

            return gameMenuItems;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var mainMenuItems = new List<MainMenuItem>(_mainMenuItems);

            mainMenuItems.AddRange(CreateDirectoryMainMenuItems(
                PlayniteApi.Database.Platforms,
                Resource.ActionsPlatform,
                CreatePlatformDirectory,
                SelectMusicForPlatform));

            mainMenuItems.AddRange(CreateDirectoryMainMenuItems(
                PlayniteApi.Database.FilterPresets,
                Resource.ActionsFilter,
                CreateFilterDirectory,
                SelectMusicForFilter));

            var defaultSubMenu = $"|{Resource.ActionsDefault}";
            var defaultFiles = Directory.GetFiles(_defaultMusicPath);
            if (defaultFiles.Any())
            {
                mainMenuItems.Add(new MainMenuItem
                {
                    Description = "-",
                    MenuSection = App.MainMenuName + defaultSubMenu
                });
                mainMenuItems.AddRange(ConstructItems(ConstructMainMenuItem, defaultFiles, defaultSubMenu + "|"));
            }

            return mainMenuItems;
        }

        private IEnumerable<MainMenuItem> CreateDirectoryMainMenuItems<T>(
            IEnumerable<T> databaseObjects,
            string menuPath,
            Func<T, string> directoryConstructor,
            Action<T> musicSelector) where T : DatabaseObject
        {
            foreach (var databaseObject in databaseObjects.OrderBy(o => o.Name))
            {
                var directorySelect = $"|{menuPath}|{databaseObject.Name}";

                yield return ConstructMainMenuItem(
                    Resource.ActionsCopySelectMusicFile,
                    () => musicSelector(databaseObject),
                    directorySelect);

                var files = Directory.GetFiles(directoryConstructor(databaseObject));
                if (files.Any())
                {
                    yield return new MainMenuItem
                    {
                        Description = "-",
                        MenuSection = App.MainMenuName + directorySelect
                    };

                    foreach (var item in ConstructItems(ConstructMainMenuItem, files, directorySelect + "|"))
                    {
                        yield return item;
                    }
                }
            }
        }

        private void CleanupDeletedGames(object sender, ItemCollectionChangedEventArgs<Game> ItemCollectionChangedArgs)
        {
            // Let ExtraMetaDataLoader handle cleanup if it exists
            if (PlayniteApi.Addons.Plugins.Any(p => p.Id.ToString() is App.ExtraMetaGuid))
            {
                return;
            }

            foreach (var removedItem in ItemCollectionChangedArgs.RemovedItems)
            {
                DeleteMusicDirectory(removedItem);
            }
        }

        private void UpdatePlatforms(object sender, ItemCollectionChangedEventArgs<Platform> ItemCollectionChangedArgs)
        {
            foreach (var addedItem in ItemCollectionChangedArgs.AddedItems)
            {
                CreatePlatformDirectory(addedItem.Name);
            }

            DeleteDirectories(ItemCollectionChangedArgs.RemovedItems, GetPlatformDirectoryPath);
        }

        private void UpdateFilters(object sender, ItemCollectionChangedEventArgs<FilterPreset> ItemCollectionChangedArgs)
        {
            foreach (var addedItem in ItemCollectionChangedArgs.AddedItems)
            {
                CreateFilterDirectory(addedItem.Id.ToString());
            }

            DeleteDirectories(ItemCollectionChangedArgs.RemovedItems, GetFilterDirectoryPath);
        }

        private void DeleteDirectories<T>(IEnumerable<T> directoryLinks, Func<T, string> PathConstructor)
            => directoryLinks.
                Select(PathConstructor).
                Where(Directory.Exists).
                ForEach(f => Directory.Delete(f, true));

        // ex: playnite://Sounds/Play/someId
        // Sounds maintains a list of plugins who want the music paused and will only allow play when
        // no other plugins have paused.
        private void HandleUriEvent(PlayniteUriEventArgs args)
        {
            var action = args.Arguments[0];
            var senderId = args.Arguments[1];

            switch (action.ToLower())
            {
                case "play":
                    _pausers.Remove(senderId);
                    ResumeMusic();
                    break;
                case "pause":
                    if (_pausers.Add(senderId) && _pausers.Count is 1)
                    {
                        PauseMusic();
                    }
                    break;
            }
        }

        #endregion

        #region State Changes

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (Settings.PauseOnDeactivate)
            {
                switch (Application.Current?.MainWindow?.WindowState)
                {
                    case WindowState.Normal:
                    case WindowState.Maximized:
                        ResumeMusic();
                        break;
                    case WindowState.Minimized:
                        PauseMusic();
                        break;
                }
            }
        }
        private void OnApplicationDeactivate(object sender, EventArgs e)
        {
            if (Settings.PauseOnDeactivate)
            {
                PauseMusic();
            }
        }

        private void OnApplicationActivate(object sender, EventArgs e)
        {
            if (Settings.PauseOnDeactivate)
            {
                ResumeMusic();
            }
        }

        //fix sounds not playing after system resume
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
        {
            var shouldNotPlay = Settings.PauseOnDeactivate
                && Application.Current?.MainWindow?.WindowState == WindowState.Minimized;
            if (args.Mode is PowerModes.Resume && !shouldNotPlay)
            {
                Try(RestartMusic);
            }
        }

        public void OnSettingsChanged( object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SettingsModel.Settings.VideoIsPlaying)
            || args.PropertyName == nameof(SettingsModel.Settings.PreviewIsPlaying))
            {
                if (SettingsModel.Settings.VideoIsPlaying
                || SettingsModel.Settings.PreviewIsPlaying )
                    PauseMusic();
                else
                    ResumeMusic();
            }
        }

        private void RestartMusic()
        {
            _closeAudioFilesNextPlay = true;
            ReloadMusic = true;
            ReplayMusic();
        }

        #endregion

        #region Audio Player

        public void ResetMusicVolume()
        {
            if (_musicPlayer != null && _musicPlayer.Volume != Settings.MusicVolume )
            {
                if (Settings.MusicVolume == 0)
                {
                    PauseMusic();
                }
                else if (_musicPlayer.Volume == 0)
                {
                    ResumeMusic();
                }

                _musicPlayer.Volume = Settings.MusicVolume / 100.0;
            }
        }

        public void PauseOrResumeMusic()
        {
            if (ShouldPlayMusic())
            {
                ResumeMusic();
            }
            else
            {
                PauseMusic();
            }
        }

        public void ReplayMusic()
        {
            if (SingleGame() && ShouldPlayMusicOrClose())
            {
                PlayMusicFromFirstSelected();
            }
        }

        private void PlayMusicFromFirstSelected() => PlayMusicFromFirst(SelectedGames);

        private List<string> CollectMusicFromSimilar(MusicType type, Game game = default)
        {
            List<string> files = new List<string>();
            if (type == MusicType.Platform && game is null)
            {
                return files;
            }

            List<Game> similarGames = new List<Game>();
            switch (type)
            {
                case MusicType.Platform:
                    similarGames = PlayniteApi.Database.Games.Where(g => g.PlatformIds.Intersect(game.PlatformIds).Any()).ToList();
                    break;
                case MusicType.Filter:
                    similarGames = PlayniteApi.Database.GetFilteredGames(PlayniteApi.MainView.GetCurrentFilterSettings()).ToList();
                    break;
                default:
                    similarGames = PlayniteApi.Database.Games.ToList();
                    break;
            }
            foreach( Game g in similarGames )
            {
                var path = GetMusicDirectoryPath(g);
                if (Directory.Exists(path))
                {
                    files.AddMissing(Directory.GetFiles(path));
                }
            }
            return files;
        }

        private void PlayMusicFromFirst(IEnumerable<Game> games = null)
        {
            var game = games.FirstOrDefault();

            string fileDirectory;
            switch (Settings.ChoosenMusicType)
            {
                case MusicType.Game:
                    fileDirectory = CreateMusicDirectory(game);
                    break;
                case MusicType.Platform:
                    fileDirectory = CreatePlatformDirectoryPathFromGame(game);
                    break;
                case MusicType.Filter:
                    fileDirectory = CreateCurrentFilterDirectory();
                    break;
                default:
                    fileDirectory = _defaultMusicPath;
                    break;
            }

            List<string> files = new List<string>(Directory.Exists(fileDirectory) ? Directory.GetFiles(fileDirectory) : new string[] { }) ;

            if ( Settings.CollectFromGames && Settings.ChoosenMusicType != MusicType.Game )
            {
                files.AddMissing(CollectMusicFromSimilar(Settings.ChoosenMusicType, game));
            }

            if (Settings.PlayBackupMusic && !files.Any())
            {
                files.AddMissing(GetBackupFiles());
                if (!Settings.RestartBackupMusic)
                {
                    PlayBackgroundMusic(files);
                    return;
                }
            }

            PauseBackgroundMusic();
            PlayMusicFromFiles(files);
        }

        private void PauseBackgroundMusic()
        {
            if (_isPlayingBackgroundMusic)
            {
                _backgroundMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
                _isPlayingBackgroundMusic = false;
            }
        }
        private void PlayBackgroundMusic(List<string> musicFiles)
        {

            if (_isPlayingBackgroundMusic && !_musicEnded)
            {
                return;
            }

            if (_isPlayingBackgroundMusic && _musicEnded)
            {
                _lastBackgroundMusicFileName = PlayMusicFromFiles(musicFiles);
                return;
            }

            _isPlayingBackgroundMusic = true;

            if (string.IsNullOrEmpty(_lastBackgroundMusicFileName) || _backgroundMusicPausedOnTime == default)
            {
                _lastBackgroundMusicFileName = PlayMusicFromFiles(musicFiles);
            }
            else if (_lastBackgroundMusicFileName != _prevMusicFileName)
            {
                Try(() => _musicFader?.Switch(
                    SubCloseMusic,
                    () => PreloadMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime),
                    () => SubPlayMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime)
                ));
            }
            else
            {
                //Cancel fade and made current fill in
                Try(() => _musicFader?.Switch(null, null, () => SubPlayMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime)));
            }
        }

        private string PlayMusicFromFiles(List<string> musicFiles)
        {
            var musicFile = !string.IsNullOrEmpty(_prevMusicFileName) ? _prevMusicFileName : musicFiles.FirstOrDefault() ?? string.Empty;
            var musicEndRandom = _musicEnded && Settings.RandomizeOnMusicEnd;

            var rand = new Random();

            var changedSelection = !musicFiles.Contains(_prevMusicFileName);

            if ((changedSelection && musicFiles.Count > 0) || (musicFiles.Count > 1 && (Settings.RandomizeOnEverySelect || musicEndRandom)))
            {
                ReloadMusic = true;
                do
                {
                    musicFile = musicFiles[rand.Next(musicFiles.Count)];
                }
                while (_prevMusicFileName == musicFile);
            }
            else if ( changedSelection && musicFiles.Count == 0 )
            {
                musicFile = string.Empty;
            }

            PlayMusicFromPath(musicFile);

            return musicFile;
        }

        private void ResumeMusic()
        {
            if (ShouldPlayMusic())
            {
                if (_musicPlayer?.IsLoaded == true)
                {
                    Try(()=>_musicFader?.Resume());
                }
                else
                {
                    PlayMusicBasedOnSelected();
                }
            }
        }

        private void PauseMusic()
        {
            if (_musicPlayer?.IsLoaded == true)
            {
                Try(()=>_musicFader?.Pause());
            }
        }

        private void CloseMusic()
        {
            if (_musicPlayer?.IsLoaded == true)
            {
                Try(() => _musicFader?.Switch(SubCloseMusic));
            }
        }

        private void SubCloseMusic()
        {
            if (_musicPlayer is null)
            {
                return;
            }

            _musicPlayer.Close();

            SettingsModel.Settings.CurrentMusicName = string.Empty;
        }

        private void ForcePlayMusicFromPath(string filePath)
        {
            ReloadMusic = true;
            PlayMusicFromPath(filePath);
        }

        private void PlayMusicFromPath(string filePath)
        {
            if (!ReloadMusic && filePath == _prevMusicFileName)
            {
                Try(() => _musicFader?.Switch(null, null, () => SubPlayMusicFromPath(filePath)));
                return;
            }
            //need to use directoryname on verification otherwise when game music randomly changes
            //on musicend music will be restarted when we select another game in for Default or Platform Mode
            //in case of "random music on selection" or "Random Music on Musicend" ReloadMusic will be set
            //check on empty needs to happen before directory verification or exceptions occur if no such music exists
            //it still needs to call the sub to play the music but it will just close the music as File.exists will fail there
            if (ReloadMusic || _prevMusicFileName.Equals(string.Empty) || filePath.Equals(string.Empty) ||
                (Path.GetDirectoryName(filePath) != Path.GetDirectoryName(_prevMusicFileName)))
            {
                if (File.Exists(filePath))
                {
                    Try(() => _musicFader?.Switch(
                        SubCloseMusic,
                        () => PreloadMusicFromPath(filePath),
                        () => SubPlayMusicFromPath(filePath)
                    ));

                }
                else
                    Try(() => _musicFader?.Switch(SubCloseAndStopMusic));
            }
        }

        private void SubCloseAndStopMusic()
        {
            SubCloseMusic();
            ReloadMusic = false;
            _prevMusicFileName = string.Empty;
        }

        private void PreloadMusicFromPath(string filePath, TimeSpan startFrom = default)
        {
            ReloadMusic = false;
            if (File.Exists(filePath))
            {
                _musicPlayer.PreLoad(filePath, startFrom);
            }
        }

        private void SubPlayMusicFromPath(string filePath, TimeSpan startFrom = default)
        {
            ReloadMusic = false;
            _prevMusicFileName = string.Empty;
            if (File.Exists(filePath))
            {
                _prevMusicFileName = filePath;
                _musicPlayer.Load(filePath);
                _musicPlayer.Play(startFrom);

                _musicEnded = false;
                SettingsModel.Settings.CurrentMusicName = Path.GetFileNameWithoutExtension(filePath);
            }
        }

        private void PlaySoundFileFromName(string fileName, bool useSoundPlayer = false)
        {
            if (ShouldPlaySound())
            {
                Try(() => SubPlaySoundFileFromName(fileName, useSoundPlayer));
            }
        }

        private void SubPlaySoundFileFromName(string fileName, bool useSoundPlayer)
        {
            if (_closeAudioFilesNextPlay)
            {
                CloseAudioFiles();
                _closeAudioFilesNextPlay = false;
            }

            _players.TryGetValue(fileName, out var entry);
            if (entry == null)
            {
                entry = CreatePlayerEntry(fileName, useSoundPlayer);
            }

            if (entry != null)
            /*Then*/ if (useSoundPlayer)
            {
                entry.SoundPlayer.Stop();
                entry.SoundPlayer.PlaySync();
            }
            else
            {
                entry.MusicPlayer.Stop();
                entry.MusicPlayer.Play();
            }
        }

        private PlayerEntry CreatePlayerEntry(string fileName, bool useSoundPlayer)
        {
            var fullFileName = Path.Combine(_extraMetaDataFolder, SoundDirectory.Sound, fileName);

            if (!File.Exists(fullFileName))
            {
                return null;
            }

            var entry = new PlayerEntry();
            if (useSoundPlayer)
            {
                entry.SoundPlayer = new SoundPlayer { SoundLocation = fullFileName };
                entry.SoundPlayer.Load();
            }
            else
            {
                // MediaPlayer can play multiple sounds together from multiple instances, but the SoundPlayer can not
                entry.MusicPlayer = MusicPlayer.Create(Settings);
                entry.MusicPlayer.Load(fullFileName);
            }

            return _players[fileName] = entry;
        }

        private void CloseAudioFiles()
        {
            foreach(var playerFile in _players.Keys.ToList())
            {
                var player = _players[playerFile];
                _players.Remove(playerFile);

                Try(() => CloseAudioFile(player));
            }
        }

        private static void CloseAudioFile(PlayerEntry entry)
        {
            if (entry.MusicPlayer != null)
            {
                var filename = entry.MusicPlayer.Source == null
                    ? string.Empty
                    : entry.MusicPlayer.Source;

                entry.MusicPlayer.Stop();
                entry.MusicPlayer.Close();
                entry.MusicPlayer = null;
                if (File.Exists(filename))
                {
                    var fileInfo = new FileInfo(filename);
                    for (var count = 0; IsFileLocked(fileInfo) && count < 100; count++)
                    {
                        Thread.Sleep(5);
                    }
                }
            }
            else
            {
                entry.SoundPlayer.Stop();
                entry.SoundPlayer = null;
            }
        }

        public void ReloadAudioFiles()
        {
            CloseAudioFiles();
            ShowMessage(Resource.ActionsReloadAudioFiles);
        }

        private void MediaEnded(object sender, EventArgs e)
        {
            _musicEnded = true;
            if (Settings.RandomizeOnMusicEnd)
            {
                // will play a random song if more than one exists
                ReloadMusic = true;
                ReplayMusic();
            }
            else if (_musicPlayer.IsLoaded)
            {
                _musicPlayer.Stop();
                _musicPlayer.Play();
            }
        }

        private void MediaFailed(object sender, ExceptionEventArgs e)
        {
            Logger.Error($"MediaFailed: {e.ErrorException}");
            if (e.ErrorException.GetType() == typeof(System.Windows.Media.InvalidWmpVersionException))
            {
                SubCloseMusic();

                _musicPlayer.MediaEnded -= MediaEnded;
                //_musicPlayer.MediaFailed -= MediaFailed;

                _musicFader.Destroy();
                _musicFader = null;
                _musicPlayer = null;

                WMPMusicPlayer.InstallWMP();
            }
        }
        #endregion

        #region UI

        #region Menu UI

        private IEnumerable<TMenuItem> ConstructItems<TMenuItem>(
            Func<string, Action, string, TMenuItem> menuItemConstructor,
            string[] files,
            string subMenu,
            bool isGame = false)
        {
            foreach (var file in files)
            {
                var songName = Path.GetFileNameWithoutExtension(file);
                var songSubMenu = subMenu + songName;

                yield return menuItemConstructor(
                    Resource.ActionsCopyPlayMusicFile, () => ForcePlayMusicFromPath(file), songSubMenu);
                yield return menuItemConstructor(
                    Resource.ActionsCopyDeleteMusicFile, () => DeleteMusicFile(file, songName, isGame), songSubMenu);
            }
        }

        private static GameMenuItem ConstructGameMenuItem(string resource, Action action, string subMenu = "")
            => ConstructGameMenuItem(resource, _ => action(), subMenu);

        private static GameMenuItem ConstructGameMenuItem(
            string resource, Action<GameMenuItemActionArgs> action, string subMenu = "") => new GameMenuItem
        {
            MenuSection = App.AppName + subMenu,
            Icon = IconPath,
            Description = resource,
            Action = action
        };

        private static MainMenuItem ConstructMainMenuItem(string resource, Action action, string subMenu = "")
            => ConstructMainMenuItem(resource, _ => action(), subMenu);

        private static MainMenuItem ConstructMainMenuItem(
            string resource, Action<MainMenuItemActionArgs> action, string subMenu = "") => new MainMenuItem
        {
            MenuSection = App.MainMenuName + subMenu,
            Icon = IconPath,
            Description = resource,
            Action = action
        };

        public void OpenMusicDirectory()
            => Try(() => SelectedGames.ForEach(g => Process.Start(GetMusicDirectoryPath(g))));

        #endregion

        #region Prompts

        private List<Song> PromptUserForYoutubeSearch(string gameName)
            => PromptForSelectWithPreview<Song>(Resource.DialogMessageCaptionSong,
                gameName,
                (s, t) => SearchYoutube(s,t)
                    ?.Select(a => new GenericObjectOption(a.Name, a.ToString(), a) as GenericItemOption)
                    ?.ToList(),
                gameName + " soundtrack");

        private Album PromptForAlbum(Game game, Source source, ref List<Album> albums, ref string actualSearch)
            => PromptForSelectWithPreview<Album>(Resource.DialogMessageCaptionAlbum,
                StringUtilities.StripStrings(game.Name),
                (s, t) =>
                {
                    var v = DownloadManager.GetAlbumsForGame(s, source, cancellationToken: t)
                        ?.OrderByDescending(a => DownloadManager.GetAlbumRelevance(a, game));
                    //?.ThenBy(a => a.Name.ToUpper())
                    var res = v?.Select(a => new GenericObjectOption(a.Name, a.ToString(), a) as GenericItemOption)
                    ?.ToList();
                    return res;
                },
                StringUtilities.StripStrings(game.Name) + (source is Source.Youtube ? " soundtrack" : string.Empty),
                ref albums,
                ref actualSearch
                ).FirstOrDefault();

        private List<Song> PromptForSong(Album album)
        {
            var songs = album.Songs?.ToList() ?? new List<Song>();
            var res = PromptForSelectWithPreview(Resource.DialogMessageCaptionSong,
                album.Name,
                (a, t) =>
                    DownloadManager.GetSongsFromAlbum(album, t)
                    ?.OrderByDescending(s => s.Name.StartsWith(a))
                    ?.Select(s => new GenericObjectOption(s.Name, s.ToString(), s) as GenericItemOption)
                    ?.ToList(),
                string.Empty,
                ref songs);
            if (songs?.Count > 0)
            {
                album.Songs = songs;
            }
            return res;
        }

        private T PromptForSelect<T>(
            string captionFormat,
            string formatArg,
            Func<string, List<GenericItemOption>> search,
            string defaultSearch)
        {
            var option = Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(), search, defaultSearch, string.Format(captionFormat, formatArg));

            if (option is GenericObjectOption idOption && idOption.Object is T obj)
            {
                return obj;
            }

            return default;
        }

        private List<T> PromptForSelectWithPreview<T>(
            string captionFormat,
            string formatArg,
            Func<string, CancellationToken, List<GenericItemOption>> search,
            string defaultSearch,
            T type = default)
        {
            List<T> tmp = new List<T>();
            string str = defaultSearch;
            return PromptForSelectWithPreview(captionFormat, formatArg, search, defaultSearch, ref tmp, ref str);
        }

        private List<T> PromptForSelectWithPreview<T>(
            string captionFormat,
            string formatArg,
            Func<string, CancellationToken, List<GenericItemOption>> search,
            string defaultSearch,
            ref List<T> previous)
        {
            string str = defaultSearch;
            return PromptForSelectWithPreview(captionFormat, formatArg, search, defaultSearch, ref previous, ref str);
        }

        private List<T> PromptForSelectWithPreview<T>(
            string captionFormat,
            string formatArg,
            Func<string, CancellationToken, List<GenericItemOption>> searchFunc,
            string defaultSearch,
            ref List<T> previous,
            ref string actualSearch)
        {
            List<GenericItemOption> items = previous
                ?.Select(a => new GenericObjectOption((a as DownloadItem).Name, a.ToString(), a) as GenericItemOption)
                ?.ToList();

            MusicSelectionViewModel model = new MusicSelectionViewModel(
                PlayniteApi,
                DownloadManager,
                SettingsModel.Settings,
                string.Format(captionFormat, formatArg) ?? ResourceProvider.GetString("LOCSelectItemTitle"),
                searchFunc,
                string.IsNullOrEmpty(actualSearch) ? defaultSearch : actualSearch,
                isSong: previous is List<Song>,
                previous: items
            );

            bool? result = Application.Current.Dispatcher.Invoke(() => model.ShowDialog());

            previous = model.SearchResults?.Select(r => (T)(r as GenericObjectOption)?.Object ).ToList();
            actualSearch = model.SearchTerm;

            if (result == true)
            {
                return model.SelectedResult?.Select(
                    r =>
                        (T)(r as GenericObjectOption)?.Object
                ).ToList();
            }
            else if (result == false && model.CancelationRequested)
            {
                throw new DialogCanceledException();
            }
            return new List<T>();

        }



        private bool GetBoolFromYesNoDialog(string caption)
            => Dialogs.ShowMessage(
                caption, Resource.DialogCaptionSelectOption, MessageBoxButton.YesNo) is MessageBoxResult.Yes;

        #endregion

        #region Settings

        #region Actions

        public void OpenMusicFolder() => OpenFolder(_musicFilesDataPath);

        public void OpenSoundsFolder() => OpenFolder(_soundFilesDataPath);

        private void OpenFolder(string folderPath) => Try(() => SubOpenFolder(folderPath));
        private void SubOpenFolder(string folderPath)
        {
            //need to release them otherwise explorer can't overwrite files even though you can delete them
            CloseAudioFiles();
            // just in case user deleted it
            Directory.CreateDirectory(folderPath);
            Process.Start(folderPath);
        }

        public void HelpMenu() => Dialogs.ShowMessage(HelpMessage.Value, App.AppName);

        #endregion

        #region Sound Manager

        public void LoadSounds() => Try(SubLoadSounds);
        private void SubLoadSounds()
        {
            //just in case user deleted it
            Directory.CreateDirectory(_soundManagerFilesDataPath);

            var dialog = new OpenFileDialog
            {
                Filter = "ZIP archive|*.zip",
                InitialDirectory = _soundManagerFilesDataPath
            };

            var result = dialog.ShowDialog(Dialogs.GetCurrentAppWindow());
            if (result == true)
            {
                CloseAudioFiles();
                var targetPath = dialog.FileName;
                //just in case user deleted it
                Directory.CreateDirectory(_soundFilesDataPath);
                // Have to extract each file one at a time to enabled overwrites
                using (var archive = ZipFile.OpenRead(targetPath))
                foreach (var entry in archive.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
                {
                    var entryDestination = Path.GetFullPath(Path.Combine(_soundFilesDataPath, entry.Name));
                    entry.ExtractToFile(entryDestination, true);
                }
                Dialogs.ShowMessage(
                    $"{Resource.ManagerLoadConfirm} {Path.GetFileNameWithoutExtension(targetPath)}");
            }
        }

        public void SaveSounds()
        {
            var windowExtension = Dialogs.CreateWindow(
                new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true
                });

            windowExtension.ShowInTaskbar = false;
            windowExtension.ResizeMode = ResizeMode.NoResize;
            windowExtension.Owner = Dialogs.GetCurrentAppWindow();
            windowExtension.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var saveNameBox = new TextBox
            {
                Margin = new Thickness(5, 5, 10, 5),
                Width = 200
            };
            stackPanel.Children.Add(saveNameBox);

            var saveNameButton = new Button
            {
                Margin = new Thickness(0, 5, 5, 5),
                Content = Resource.ManagerSave,
                IsEnabled = false,
                IsDefault = true
            };
            stackPanel.Children.Add(saveNameButton);

            saveNameBox.KeyUp += (sender, _) =>
            {
                // Only allow saving if filename is larger than 3 characters
                saveNameButton.IsEnabled = saveNameBox.Text.Trim().Length > 3;
            };

            saveNameButton.Click += (sender, _) =>
            {
                // Create ZIP file in sound manager folder
                try
                {
                    var soundPackName = saveNameBox.Text;
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundFilesDataPath);
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundManagerFilesDataPath);
                    ZipFile.CreateFromDirectory(
                        _soundFilesDataPath, Path.Combine(_soundManagerFilesDataPath, soundPackName + ".zip"));
                    Dialogs.ShowMessage($"{Resource.ManagerSaveConfirm} {soundPackName}");
                    windowExtension.Close();
                }
                catch (Exception e)
                {
                    HandleException(e);
                }
            };

            windowExtension.Content = stackPanel;
            windowExtension.SizeToContent = SizeToContent.WidthAndHeight;
            // Workaround for WPF bug which causes black sections to be displayed in the window
            windowExtension.ContentRendered += (s, e) => windowExtension.InvalidateMeasure();
            windowExtension.Loaded += (s, e) => saveNameBox.Focus();
            windowExtension.ShowDialog();
        }


        public void RemoveSounds() => Try(SubRemoveSounds);
        private void SubRemoveSounds()
        {
            //just in case user deleted it
            Directory.CreateDirectory(_soundManagerFilesDataPath);

            var dialog = new OpenFileDialog
            {
                Filter = "ZIP archive|*.zip",
                InitialDirectory = _soundManagerFilesDataPath
            };

            var result = dialog.ShowDialog(Dialogs.GetCurrentAppWindow());
            if (result == true)
            {
                var targetPath = dialog.FileName;
                File.Delete(targetPath);
                Dialogs.ShowMessage(
                    $"{Resource.ManagerDeleteConfirm} {Path.GetFileNameWithoutExtension(targetPath)}");
            }
        }

        public void ImportSounds()
        {
            var targetPaths = Dialogs.SelectFiles("ZIP archive|*.zip");

            if (targetPaths.HasNonEmptyItems())
            {
                Try(() => SubImportSounds(targetPaths));
            }
        }

        private void SubImportSounds(IEnumerable<string> targetPaths)
        {
            //just in case user deleted it
            Directory.CreateDirectory(_soundManagerFilesDataPath);
            foreach (var targetPath in targetPaths)
            {
                //just in case user selects a file from the soundManager location
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!targetDirectory.Equals(_soundManagerFilesDataPath, StringComparison.OrdinalIgnoreCase))
                {
                    var newTargetPath = Path.Combine(_soundManagerFilesDataPath, Path.GetFileName(targetPath));
                    File.Copy(targetPath, newTargetPath, true);
                }
            }
        }

        public void OpenSoundManagerFolder()
        {
            try
            {
                //just in case user deleted it
                Directory.CreateDirectory(_soundManagerFilesDataPath);
                Process.Start(_soundManagerFilesDataPath);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        #endregion

        #endregion

        #endregion

        #region File Management

        private void CopyAudioFiles()
        {
            var soundFilesInstallPath = Path.Combine(PluginFolder, SoundDirectory.Sound);

            if (Directory.Exists(soundFilesInstallPath) && !Directory.Exists(_soundFilesDataPath))
            {
                Try(() => SubCopyAudioFiles(soundFilesInstallPath));
            }

            var defaultMusicFile = Path.Combine(soundFilesInstallPath, SoundFile.DefaultMusicName);
            if (File.Exists(defaultMusicFile) && !Directory.Exists(_defaultMusicPath))
            {
                Directory.CreateDirectory(_defaultMusicPath);
                File.Move(defaultMusicFile, Path.Combine(_defaultMusicPath, SoundFile.DefaultMusicName));
            }
        }

        private void SubCopyAudioFiles(string soundFilesInstallPath)
        {
            CloseAudioFiles();

            Directory.CreateDirectory(_soundFilesDataPath);
            var files = Directory.GetFiles(soundFilesInstallPath).Where(f => f.EndsWith(".wav"));
            files.ForEach(f => File.Move(f, Path.Combine(_soundFilesDataPath, Path.GetFileName(f))));
        }

        private void UpdateFromLegacyVersion()
        {
            if (Settings.PromptedForMigration)
            {
                return;
            }

            Settings.PromptedForMigration = true;
            SavePluginSettings(Settings);

            var oldDirectory = GetPluginUserDataPath();
            var oldMusicDirectory = Path.Combine(oldDirectory, SoundDirectory.Music);
            var oldSoundFiles = Path.Combine(oldDirectory, SoundDirectory.Sound);

            var notLegacyFileSystem = !Directory.Exists(oldMusicDirectory) && !Directory.Exists(oldSoundFiles);
            if (notLegacyFileSystem || !GetBoolFromYesNoDialog(Resource.Migrate))
            {
                return;
            }

            Try(() => AttemptDirectoryMigration(oldDirectory, oldMusicDirectory, oldSoundFiles));
        }

        private void AttemptDirectoryMigration(string oldDirectory, string oldMusicDirectory, string oldSoundFiles)
        {
            var orphanDirectory = Path.Combine(oldDirectory, SoundDirectory.Orphans);

            if (GetBoolFromYesNoDialog(Resource.CreateBackup))
            {
                var backupFolderPath = Path.Combine(Dialogs.SelectFolder(), "Sounds Music Backup");
                Directory.CreateDirectory(backupFolderPath);

                Directory.GetDirectories(oldDirectory, "*", SearchOption.AllDirectories)
                    .ForEach(d => Directory.CreateDirectory(d.Replace(oldDirectory, backupFolderPath)));

                Directory.GetFiles(oldDirectory, "*.*", SearchOption.AllDirectories)
                    .ForEach(f => File.Copy(f, f.Replace(oldDirectory, backupFolderPath), true));
            }


            Directory.CreateDirectory(orphanDirectory);
            if (Directory.Exists(oldMusicDirectory))
            {
                var platformDirectories = Directory.GetDirectories(oldMusicDirectory);

                var playniteGames = PlayniteApi.Database.Games.ToList();
                playniteGames.ForEach(g => g.Name = StringUtilities.SanitizeGameName(g.Name));

                platformDirectories.ForEach(p => UpdateLegacyPlatform(orphanDirectory, p, playniteGames));

            }

            var defaultFile = Path.Combine(oldMusicDirectory, SoundFile.DefaultMusicName);
            if (File.Exists(defaultFile))
            {
                Logger.Info($"Moving default music file from data path...");

                File.Move(defaultFile, Path.Combine(_defaultMusicPath, SoundFile.DefaultMusicName));

                Logger.Info($"Moved default music file from music files data path.");
            }

            if (Directory.Exists(oldSoundFiles))
            {

                var newSoundsPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Sound);
                if (Directory.Exists(newSoundsPath))
                {
                    Logger.Info("Found sounds directory in ExtraMetaData folder. Deleting...");
                    Try(() => Directory.Delete(newSoundsPath, true));
                }

                Logger.Info($"Moving sound files from data path...");
                Try(() => Directory.Move(oldSoundFiles, newSoundsPath));
            }

            Try(() => Directory.Delete(oldMusicDirectory, true));

            var anyOrphans = Directory.GetFileSystemEntries(orphanDirectory).Any();
            if (anyOrphans)
            {
                var viewOrphans = GetBoolFromYesNoDialog(
                    string.Format(Resource.DialogUpdateLegacyOrphans, orphanDirectory));
                if (viewOrphans)
                {
                    Process.Start(orphanDirectory);
                }
            }
            else
            {
                Directory.Delete(orphanDirectory);
                ShowMessage(Resource.DialogMessageDone);
            }
        }

        private void UpdateLegacyPlatform(string orphanDirectory, string platformDirectory, IEnumerable<Game> games)
        {
            Logger.Info($"Working on Platform: {platformDirectory}");

            var platformDirectoryName = GetDirectoryNameFromPath(platformDirectory);

            if (platformDirectoryName.Equals(SoundDirectory.Orphans, StringComparison.Ordinal))
            {
                Logger.Info($"Ignoring directory: {platformDirectoryName}");
                return;
            }

            var defaultPlatformFile = Path.Combine(platformDirectory, SoundFile.DefaultMusicName);
            if (File.Exists(defaultPlatformFile))
            {
                Logger.Info($"Moving default music file for {platformDirectory}...");

                var newPlatformDirectory = CreatePlatformDirectory(platformDirectoryName);

                File.Move(defaultPlatformFile, Path.Combine(newPlatformDirectory, SoundFile.DefaultMusicName));

                Logger.Info($"Moved default music file for {platformDirectory}.");
            }

            var gameFiles = Directory.GetFiles(platformDirectory);
            gameFiles.ForEach(g => MoveLegacyGameFile(g, platformDirectoryName, orphanDirectory, games));

            Logger.Info($"Deleting {platformDirectory}...");
            Directory.Delete(platformDirectory);
        }

        private void MoveLegacyGameFile(
            string looseGameFile, string platformDirectoryName, string orphanDirectory, IEnumerable<Game> games)
        {
            var looseGameFileNameMp3 = Path.GetFileName(looseGameFile);
            var looseGameFileName = Path.GetFileNameWithoutExtension(looseGameFile);

            var game = games.FirstOrDefault(g => g.Name == looseGameFileName);
            var musicDirectory = game != null ? CreateMusicDirectory(game) : string.Empty;

            var newFilePath = Path.Combine(musicDirectory, looseGameFileNameMp3);

            if (game != null && !File.Exists(newFilePath))
            {
                Logger.Info($"Found game {game.Name} for file {looseGameFileNameMp3}, moving file to {newFilePath}");
                File.Move(looseGameFile, newFilePath);
            }
            else
            {
                Logger.Info($"No corresponding game or a conflicting file exists for '{looseGameFileName}'");
                var orphanPlatformDirectory = Path.Combine(orphanDirectory, platformDirectoryName);
                Directory.CreateDirectory(orphanPlatformDirectory);

                var newOrphanPath = Path.Combine(orphanPlatformDirectory, looseGameFileNameMp3);

                Logger.Info($"Moving '{looseGameFile}' to '{newOrphanPath}'");
                File.Move(looseGameFile, newOrphanPath);
            }
        }

        private void DeleteMusicDirectories()
            => PerformDeleteAction(
                Resource.DialogDeleteMusicDirectory,
                () => SelectedGames.ForEach(g => Try(() => DeleteMusicDirectory(g))));

        private void DeleteMusicDirectory(Game game)
        {
            var gameDirectory = GetMusicDirectoryPath(game);
            if (Directory.Exists(gameDirectory))
            {
                Directory.Delete(gameDirectory, true);
                UpdateMissingTag(game, false, gameDirectory);
            }
        }

        private void DeleteMusicFile(string musicFile, string musicFileName, bool isGame = false)
        {
            var deletePromptMessage = string.Format(Resource.DialogDeleteMusicFile, musicFileName);
            PerformDeleteAction(deletePromptMessage, () => File.Delete(musicFile));

            if (isGame)
            {
                var gameDirectory = Path.GetDirectoryName(musicFile);
                var gameId = GetDirectoryNameFromPath(gameDirectory);
                var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.Id.ToString() == gameId);

                if (game != null)
                {
                    UpdateMissingTag(game, false, gameDirectory);
                }
            }
        }

        private void PerformDeleteAction(string message, Action deleteAction)
        {
            if (!GetBoolFromYesNoDialog(message)) return;

            CloseMusic();

            deleteAction();

            Thread.Sleep(250);
            //need to force getting new music filename
            //if we were playing music 1 we delete music 2
            //the music type data would remain the same
            //and it would not load another music and start playing it again
            //because we closed the music above
            ReloadMusic = true;

            if (ShouldPlayMusic()) PlayMusicFromFirst(SelectedGames);
        }

        private void SelectMusicForSelectedGames()
        {
            RestartMusicAfterSelect(
                () => SelectedGames.Select(g => SelectMusicForDirectory(CreateMusicDirectory(g))).FirstOrDefault(),
                SingleGame() && Settings.ChoosenMusicType is MusicType.Game);

            Game game = SelectedGames.FirstOrDefault();
            UpdateMissingTag(
                game, Directory.GetFiles(GetMusicDirectoryPath(game)).HasNonEmptyItems(), CreateMusicDirectory(game));
        }

        private void SelectMusicForPlatform(Platform platform)
        {
            var playNewMusic =
                Settings.ChoosenMusicType is MusicType.Platform
                && SingleGame()
                && SelectedGames.First().Platforms.Contains(platform);

            RestartMusicAfterSelect(() => SelectMusicForDirectory(CreatePlatformDirectory(platform)), playNewMusic);
        }

        private void SelectMusicForFilter(FilterPreset filter)
        {
            var playNewMusic = Settings.ChoosenMusicType is MusicType.Filter
                && PlayniteApi.MainView.GetActiveFilterPreset() == filter.Id;

            RestartMusicAfterSelect(() => SelectMusicForDirectory(CreateFilterDirectory(filter)), playNewMusic);
        }

        private void SelectMusicForDefault()
            => RestartMusicAfterSelect(
                () => SelectMusicForDirectory(_defaultMusicPath), Settings.ChoosenMusicType is MusicType.Default);

        private List<string> SelectMusicForDirectory(string directory)
        {
            var newMusicFiles = PlayniteApi.Dialogs.SelectFiles("Music Files(*.mp3;*.wav;*.flac;*.wma;*.aif;*.m4a;*.aac;*.mid)|*.mp3;*.wav;*.flac;*.wma;*.aif;*.m4a;*.aac;*.mid") ?? new List<string>();

            foreach (var musicFile in newMusicFiles)
            {
                var newMusicFile = Path.Combine(directory, Path.GetFileName(musicFile));

                File.Copy(musicFile, newMusicFile, true);
            }

            return newMusicFiles;
        }

        private void RestartMusicAfterSelect(Func<List<string>> selectFunc, bool playNewMusic)
        {
            CloseMusic();

            var newMusic = selectFunc();
            var newMusicFile = newMusic?.FirstOrDefault();

            ReloadMusic = true;
            if (playNewMusic && newMusicFile != null)
            {
                ForcePlayMusicFromPath(newMusicFile);
            }
            else
            {
                PlayMusicBasedOnSelected();
            }
        }

        private void CreateNormalizationDialogue()
        {
            var progressTitle = $"{App.AppName} - {Resource.DialogMessageNormalizingFiles}";
            var progressOptions = new GlobalProgressOptions(progressTitle, true) { IsIndeterminate = false };

            var failedGames = new List<string>();

            CloseMusic();

            Dialogs.ActivateGlobalProgress(a => Try(() =>
            failedGames = NormalizeSelectedGameMusicFiles(a, SelectedGames.ToList(), progressTitle)),
                progressOptions);

            if (failedGames.Any())
            {
                var games = string.Join(", ", failedGames);
                Dialogs.ShowErrorMessage(string.Format("The following games had at least one file fail to normalize (see logs for details): ", games), App.AppName);
            }
            else
            {
                ShowMessage(Resource.DialogMessageDone);
            }

            ReloadMusic = true;
            ReplayMusic();
        }

        private List<string> NormalizeSelectedGameMusicFiles(
            GlobalProgressActionArgs args, IList<Game> games, string progressTitle)
        {
            var failedGames = new List<string>();

            int gameIdx = 0;
            foreach (var game in games.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                args.ProgressMaxValue = games.Count;
                args.CurrentProgressValue = ++gameIdx;
                args.Text = $"{progressTitle}\n\n{args.CurrentProgressValue}/{args.ProgressMaxValue}\n{game.Name}";

                var allMusicNormalized = true;
                foreach (var musicFile in Directory.GetFiles(GetMusicDirectoryPath(game)))
                {
                    if (!NormalizeAudioFile(musicFile))
                    {
                        allMusicNormalized = false;
                    }
                }

                if (allMusicNormalized)
                {
                    UpdateNormalizedTag(game);
                }
                else
                {
                    failedGames.Add(game.Name);
                }
            }

            return failedGames;
        }

        private bool NormalizeAudioFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(Settings.FFmpegNormalizePath))
            {
                throw new ArgumentException("FFmpeg-Normalize path is undefined");
            }

            if (!File.Exists(Settings.FFmpegNormalizePath))
            {
                throw new ArgumentException("FFmpeg-Normalize path does not exist");
            }

            var args = SoundFile.DefaultNormArgs;
            if (!string.IsNullOrWhiteSpace(Settings.FFmpegNormalizeArgs))
            {
                args = Settings.FFmpegNormalizeArgs;
                Logger.Info($"Using custom args '{args}' for file '{filePath}' during normalization.");
            }


            var info = new ProcessStartInfo
            {
                Arguments = $"{args} \"{filePath}\" -o \"{filePath}\" -f",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                FileName = PathExt.GetFullPath(Settings.FFmpegNormalizePath)
            };

            info.EnvironmentVariables["FFMPEG_PATH"] = PathExt.GetFullPath(Settings.FFmpegPath);

            var stdout = string.Empty;
            var stderr = string.Empty;
            using (var proc = new Process())
            {
                proc.StartInfo = info;
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        stdout += e.Data + Environment.NewLine;
                    }
                };

                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        stderr += e.Data + Environment.NewLine;
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Logger.Error($"FFmpeg-Normalize failed for file '{filePath}' with error: {stderr} and output: {stdout}");
                    return false;
                }

                Logger.Info($"FFmpeg-Normalize succeeded for file '{filePath}.");
                return true;
            }
        }

        #endregion

        #region Download

        private IEnumerable<Song> SearchYoutube(string search, CancellationToken token)
        {
            var album = DownloadManager.GetAlbumsForGame(search, Source.Youtube, cancellationToken: token).First();
            return DownloadManager.GetSongsFromAlbum(album, token);
        }

        private bool OnlySearchForYoutubeVideos(Source source) => source is Source.Youtube && !Settings.YtPlaylists;

        private void DownloadMusicForSelectedGames(Source source)
        {
            var games = SelectedGames.ToList();
            var albumSelect = true;
            var songSelect = true;
            if (games.Count() > 1)
            {
                albumSelect = GetBoolFromYesNoDialog(Resource.DialogMessageAlbumSelect);
                songSelect = GetBoolFromYesNoDialog(Resource.DialogMessageSongSelect);
            }

            var overwriteSelect = GetBoolFromYesNoDialog(Resource.DialogMessageOverwriteSelect);

            MuteExceptions(!(games.Count() == 1 || albumSelect || songSelect));

            CreateDownloadDialogue(games, source, albumSelect, songSelect, overwriteSelect);

            UnMuteExceptions(verbose: true);

            ReloadMusic = true;
            DownloadManager.Cleanup();
            ReplayMusic();
        }

        public static GlobalProgressActionArgs GlobalStatus= default;

        private void CreateDownloadDialogue(
            IEnumerable<Game> games,
            Source source,
            bool albumSelect = false,
            bool songSelect = false,
            bool overwriteSelect = false)
        {
            var progressTitle = $"{App.AppName} - {Resource.DialogMessageDownloadingFiles}";
            var progressOptions = new GlobalProgressOptions(progressTitle, true) { IsIndeterminate = false };

            Dialogs.ActivateGlobalProgress(a => Try(() =>
            StartDownload(a, games.ToList(), source, progressTitle, albumSelect, songSelect, overwriteSelect)),
                progressOptions);
        }

        private void StartDownload(
            GlobalProgressActionArgs args,
            List<Game> games,
            Source source,
            string progressTitle,
            bool albumSelect,
            bool songSelect,
            bool overwrite)
        {

            int gameIdx = 0;
            GlobalStatus = args;

            foreach (var game in games.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                args.ProgressMaxValue = games.Count;
                args.CurrentProgressValue = ++gameIdx;
                args.Text = $"{progressTitle} ({args.CurrentProgressValue}/{args.ProgressMaxValue})\n\n{game.Name}";

                var gameDirectory = CreateMusicDirectory(game);

                List<string> newFilePaths = default;
                try
                {
                    newFilePaths =
                        DownloadSongsFromGame(args, progressTitle, source, game, gameDirectory, songSelect, albumSelect, overwrite);
                }
                catch (DialogCanceledException)
                {
                    break;
                }

                var fileDownloaded = newFilePaths != null;
                bool normalized = false;
                if (Settings.NormalizeMusic && fileDownloaded)
                {
                    foreach (string newFilePath in newFilePaths)
                    {
                        args.Text += $" - {Resource.DialogMessageNormalizingFiles}";
                        normalized = normalized || NormalizeAudioFile(newFilePath);
                    }
                    if (normalized)
                    {
                        UpdateNormalizedTag(game);
                    }
                }

                UpdateMissingTag(game, fileDownloaded, gameDirectory);
            }
        }

        private List<string> DownloadSongsFromGame(
            GlobalProgressActionArgs args,
            string progressTitle,
            Source source,
            Game game,
            string gameDirectory,
            bool songSelect,
            bool albumSelect,
            bool overwrite)
        {

            var strippedGameName = StringUtilities.StripStrings(game.Name);

            var regexGameName = songSelect && albumSelect
                ? string.Empty
                : StringUtilities.ReplaceStrings(strippedGameName);

            Album album = null;
            List<Song> songs = null;

            List<Album> albums = new List<Album>();
            string actualSearch = string.Empty;
            do
            {
                album = SelectAlbumForGame(source, game, albumSelect, songSelect, ref albums, ref actualSearch, args.CancelToken);
                if (album is null)
                {
                    return null;
                }

                Logger.Info($"Selected album '{album.Name}' from source '{album.Source}' for game '{game.Name}'");

                songs = SelectSongsFromAlbum(album, game.Name, strippedGameName, regexGameName, songSelect, args.CancelToken);

            } while (songSelect && songs is null);

            if (songs?.FirstOrDefault() is null)
            {
                Logger.Info($"No songs found for album '{album.Name}' from source '{album.Source}' for game '{game.Name}'");
                return null;
            }

            List<string> newFilePaths = new List<string>();
            int musicIdx = 0;
            foreach (Song song in songs.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                if (songSelect)
                {
                    args.ProgressMaxValue = songs.Count;
                    args.CurrentProgressValue = ++musicIdx;
                }
                args.Text = $"{progressTitle} ({args.CurrentProgressValue}/{args.ProgressMaxValue})\n\n{game.Name}: {song.Name}";

                Logger.Info($"Selected song '{song.Name}' from album '{album.Name}' for game '{game.Name}'");

                var sanitizedFileName = StringUtilities.SanitizeGameName(song.Name) + ".mp3";
                var newFilePath = Path.Combine(gameDirectory, sanitizedFileName);
                if (!overwrite && File.Exists(newFilePath))
                {
                    Logger.Info($"Song file '{sanitizedFileName}' for game '{game.Name}' already exists. Skipping....");
                    continue;
                }

                Logger.Info($"Overwriting song file '{sanitizedFileName}' for game '{game.Name}'.");

                if (!DownloadManager.DownloadSong(song, newFilePath, args.CancelToken))
                {
                    Logger.Info($"Failed to download song '{song.Name}' for album '{album.Name}' of game '{game.Name}' with source {song.Source} and Id '{song.Id}'");
                    continue;
                }

                Logger.Info($"Downloaded file '{sanitizedFileName}' in album '{album.Name}' of game '{game.Name}'");
                newFilePaths.Add(newFilePath);
            }
            return newFilePaths.Count > 0 ? newFilePaths : null;
        }

        private Album SelectAlbumForGame(
            Source source,
            Game game,
            bool albumSelect,
            bool songSelect,
            ref List<Album> cache,
            ref string actualSearch,
            CancellationToken token)
        {
            Album album = null;

            var skipAlbumSearch = OnlySearchForYoutubeVideos(source) && songSelect;
            if (skipAlbumSearch)
            {
                Logger.Info($"Skipping album search for game '{game.Name}'");
                album = new Album { Name = Resource.YoutubeSearch, Source = Source.Youtube };
            }
            else
            {
                Logger.Info($"Starting album search for game '{game.Name}'");

                if (albumSelect)
                {
                    try
                    {
                        album = PromptForAlbum(game, source, ref cache, ref actualSearch);
                    }
                    catch (Exception e)
                    {
                        if (e is DialogCanceledException)
                            throw;

                        HandleException(e);
                        return default;
                    }
                }
                else
                {
                    var albums = DownloadManager.GetAlbumsForGame(
                        StringUtilities.StripStrings(game.Name), source, token, true )?.ToList();
                    if (albums?.Any() ?? false)
                    {
                        album = DownloadManager.BestAlbumPick(albums, game);
                    }
                    else
                    {
                        Logger.Info($"Did not find any albums for game '{game.Name}' from source '{source}'");
                    }
                }
            }

            return album;
        }

        private List<Song> SelectSongsFromAlbum(
            Album album,
            string gameName,
            string strippedGameName,
            string regexGameName,
            bool songSelect,
            CancellationToken token)
        {
            List<Song> selectedSong = null;

            if (OnlySearchForYoutubeVideos(album.Source))
            {
                if (songSelect)
                {
                    selectedSong = PromptUserForYoutubeSearch(strippedGameName);
                }
                else
                {
                    if (DownloadManager.BestSongPick(album.Songs.ToList(), regexGameName) is Song bestSong)
                    {
                        selectedSong = new List<Song>() { bestSong };
                    }
                    else
                    {
                        Logger.Info($"Can't pick best song from album '{album.Name}'. Skipped...");
                    }
                }
            }
            else
            {
                if ( songSelect )
                {
                    selectedSong = PromptForSong(album);
                }
                else
                {
                    List<Song> songs = DownloadManager.GetSongsFromAlbum(album, token).Where(s=>s != null).ToList();
                    if (!songs.Any())
                    {
                        Logger.Info($"Did not find any songs for album '{album.Name}' of game '{gameName}'");
                    }
                    else
                    {
                        Logger.Info($"Found songs for album '{album.Name}' of game '{gameName}'");
                        if (DownloadManager.BestSongPick(songs, regexGameName) is Song bestSong)
                        {
                            selectedSong = new List<Song> { bestSong };
                        }
                        else
                        {
                            Logger.Info($"Every song in album '{album.Name}' is too long. Skipped...");
                        }
                    }
                }
            }

            return selectedSong;
        }

        #endregion
        #region Helpers

        private void UpdateMissingTag(Game game, bool fileCreated, string gameDirectory)
        {
            if (Settings.TagMissingEntries)
            {
                var missingTag = PlayniteApi.Database.Tags.Add(Resource.MissingTag);

                if (fileCreated && RemoveTagFromGame(game, missingTag))
                {
                    Logger.Info($"Removed tag from '{game.Name}'");
                }
                else
                {
                    var noFiles = !Directory.Exists(gameDirectory) || !Directory.GetFiles(gameDirectory).Any();
                    if (noFiles && AddTagToGame(game, missingTag))
                    {
                        Logger.Info($"Added tag to '{game.Name}'");
                    }
                }
            }
        }

        private void UpdateNormalizedTag(Game game)
        {
            if (Settings.TagNormalizedGames)
            {
                Tag normalizedTag = PlayniteApi.Database.Tags.Add(Resource.NormTag);
                if (AddTagToGame(game, normalizedTag))
                {
                    Logger.Info($"Added normalized tag to '{game.Name}'");
                }
            }
        }

        private bool AddTagToGame(Game game, Tag tag)
        {
            if (game.Tags is null)
            {
                game.TagIds = new List<Guid> { tag.Id };
                PlayniteApi.Database.Games.Update(game);
                return true;
            }

            if (!game.TagIds.Contains(tag.Id))
            {
                game.TagIds.Add(tag.Id);
                PlayniteApi.Database.Games.Update(game);
                return true;
            }

            return false;
        }

        private bool RemoveTagFromGame(Game game, Tag tag)
        {
            if (game.Tags != null && game.TagIds.Remove(tag.Id))
            {
                PlayniteApi.Database.Games.Update(game);
                return true;
            }
            return false;
        }

        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (var stream = file.Open(FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Close();
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }

        private bool ShouldPlayMusicOrClose()
        {
            var shouldPlayMusic = ShouldPlayMusic();
            if (!shouldPlayMusic)
            {
                CloseMusic();
            }

            return shouldPlayMusic;
        }

        private bool ShouldPlaySound() => ShouldPlayAudio(Settings.SoundState);

        private bool ShouldPlayMusic() =>
            _musicPlayer != null
            && _pausers.Count is 0
            && SettingsModel.Settings.MusicVolume > 0
            && !SettingsModel.Settings.VideoIsPlaying
            && !SettingsModel.Settings.PreviewIsPlaying
            && !_gameRunning
            && ShouldPlayAudio(Settings.MusicState);

        private bool ShouldPlayAudio(AudioState state)
        {
            var desktopMode = IsDesktop();
            var playOnFullScreen = !desktopMode && (state == AudioState.Fullscreen || state == AudioState.Always);
            var playOnDesktop = desktopMode && (state == AudioState.Desktop || state == AudioState.Always);

            playOnDesktop &= desktopMode && (!SettingsModel.Settings.PauseNotInLibrary || GetMainModel().ActiveView.GetType().Name == "Library");

            var skipFirstSelectMusic = _firstSelect && Settings.SkipFirstSelectMusic;

            return (playOnFullScreen || playOnDesktop) && !skipFirstSelectMusic;
        }

        private void ShowMessage(string resource) => Dialogs.ShowMessage(resource, App.AppName);

        private bool IsDesktop() => PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop;

        private bool SingleGame() => SelectedGames?.Count() == 1;

        private string GetCurrentFilterDirectoryPath()
            => Path.Combine(_filterMusicFilePath, PlayniteApi.MainView.GetActiveFilterPreset().ToString());

        private string GetMusicDirectoryPath(Game game)
            => Path.Combine(_gameMusicFilePath, game.Id.ToString(), SoundDirectory.Music);

        private string CreatePlatformDirectoryPathFromGame(Game game)
            => CreatePlatformDirectory(game?.Platforms?.FirstOrDefault()?.Name ?? SoundDirectory.NoPlatform);

        private string CreateMusicDirectory(Game game)
            => Directory.CreateDirectory(GetMusicDirectoryPath(game)).FullName;

        private string CreatePlatformDirectory(Platform platform)
            => Directory.CreateDirectory(GetPlatformDirectoryPath(platform)).FullName;

        private string CreatePlatformDirectory(string platform)
            => Directory.CreateDirectory(GetPlatformDirectoryPath(platform)).FullName;

        private string CreateFilterDirectory(FilterPreset filter)
            => Directory.CreateDirectory(GetFilterDirectoryPath(filter)).FullName;

        private string CreateFilterDirectory(string filterId)
            => Directory.CreateDirectory(GetFilterDirectoryPath(filterId)).FullName;

        private string CreateCurrentFilterDirectory()
            => Directory.CreateDirectory(GetFilterDirectoryPath(PlayniteApi.MainView.GetActiveFilterPreset().ToString())).FullName;

        private string GetPlatformDirectoryPath(Platform platform)
            => Path.Combine(_platformMusicFilePath, platform.Name);

        private string GetPlatformDirectoryPath(string platform)
            => Path.Combine(_platformMusicFilePath, platform);
        private string GetFilterDirectoryPath(FilterPreset filter)
            => Path.Combine(_filterMusicFilePath, filter.Id.ToString());

        private string GetFilterDirectoryPath(string filterId)
            => Path.Combine(_filterMusicFilePath, filterId);

        private static string GetDirectoryNameFromPath(string directory)
            => directory.Substring(directory.LastIndexOf('\\')).Replace("\\", string.Empty);

        private void PlayMusicBasedOnSelected()
        {
            if (ShouldPlayMusicOrClose())
            {
                switch (SelectedGames?.Count())
                {
                    case 1:
                        PlayMusicFromFirstSelected();
                        break;
                    case 0 when Settings.PlayBackupMusic:
                        PlayMusicFromFiles(GetBackupFiles());
                        break;
                }
            }
        }

        // Backup order is game -> filter -> default
        private List<string> GetBackupFiles()
        {
            if (Settings.ChoosenMusicType != MusicType.Default)
            {
                string filterDirectory = GetCurrentFilterDirectoryPath();
                if (Directory.Exists(filterDirectory))
                {
                    List<string> filterFiles = Directory.GetFiles(filterDirectory).ToList();
                    if (Settings.CollectFromGamesOnBackup)
                    {
                        filterFiles.AddMissing(CollectMusicFromSimilar(MusicType.Filter));
                    }
                    if (filterFiles.Any())
                    {
                        return filterFiles;
                    }
                }
            }

            List<string> defaultFiles = Directory.Exists(_defaultMusicPath) ? Directory.GetFiles(_defaultMusicPath).ToList() : new List<string>();
            if (Settings.CollectFromGamesOnBackup)
            {
                defaultFiles.AddMissing(CollectMusicFromSimilar(MusicType.Default));
            }
            return defaultFiles;
        }

        static public bool _muteExceptions = false;
        static public List<string> _mutedErrors = new List<string>();

        private bool _isPlayingBackgroundMusic = false;
        private string _lastBackgroundMusicFileName = null;
        private TimeSpan _backgroundMusicPausedOnTime = default;

        static public void MuteExceptions(bool mute = true)
        {
            _muteExceptions = mute;
            _mutedErrors = new List<string>();
        }

        static public void UnMuteExceptions(bool verbose = false)
        {
            if (verbose && _muteExceptions && _mutedErrors.Count > 0)
            {
                List<string> errors = _mutedErrors.Take(3).ToList();

                if (_mutedErrors.Count() > 3)
                    errors.Add($"...({_mutedErrors.Count() - 3})...");

                Dialogs.ShowErrorMessage(Resource.WereErrors + "\r\n" + string.Join(",\r\n", errors)+".", App.AppName);
            }
            _muteExceptions = false;
            _mutedErrors = new List<string>();
        }


        static public void HandleException(Exception e, CancellationToken cancellationToken = default)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (e.InnerException is TaskCanceledException)
                {
                    Logger.Error(e, "Timeout at " + e.StackTrace);

                    _mutedErrors.AddMissing(Resource.TimeoutError);

                    if (!_muteExceptions)
                    {
                        Dialogs.ShowErrorMessage(Resource.TimeoutError, App.AppName);
                    }
                }
                else
                {
                    Logger.Error(e, e.StackTrace);
                    _mutedErrors.AddMissing(e.InnerException?.Message ?? e.Message);

                    if (!_muteExceptions)
                    {
                        Dialogs.ShowErrorMessage(e.InnerException?.Message ?? e.Message, App.AppName);
                    }
                }
            }
        }

        static public void Try(Action action) { try { action(); } catch (Exception ex) { HandleException(ex); } }

        private PlayniteSoundsSettings Settings => SettingsModel.Settings;
        private IEnumerable<Game> SelectedGames => PlayniteApi.MainView.SelectedGames;
        static private IDialogsFactory Dialogs => playniteAPI.Dialogs;

        #endregion


    }
}
