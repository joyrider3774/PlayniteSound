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
using System.Media;
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

namespace PlayniteSounds
{
    public class PlayniteSounds : GenericPlugin
    {
        public bool ReloadMusic { get; set; }

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

        private PlayniteSoundsSettingsViewModel SettingsModel { get; }

        private bool _gameRunning;
        private bool _musicEnded;
        private bool _firstSelectSound = true;
        private bool _closeAudioFilesNextPlay;

        private string _prevMusicFileName = string.Empty;  //used to prevent same file abeing restarted 

        private readonly string _extraMetaDataFolder;
        private readonly string _musicFilesDataPath;
        private readonly string _soundFilesDataPath;
        private readonly string _soundManagerFilesDataPath;
        private readonly string _defaultMusicPath;
        private readonly string _gameMusicFilePath;
        private readonly string _platformMusicFilePath;

        private readonly Dictionary<string, PlayerEntry> _players = new Dictionary<string, PlayerEntry>();

        private MediaPlayer _musicPlayer;
        private readonly MediaTimeline _timeLine;

        private readonly List<GameMenuItem> _gameMenuItems;
        private readonly List<MainMenuItem> _mainMenuItems;

        private ISet<string> _pausers = new HashSet<string>();

        #region Constructor

        public PlayniteSounds(IPlayniteAPI api) : base(api)
        {
            try
            {
                SoundFile.ApplicationInfo = PlayniteApi.ApplicationInfo;

                _extraMetaDataFolder = Path.Combine(api.Paths.ConfigurationPath, SoundDirectory.ExtraMetaData);

                _musicFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Music);

                _soundFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Sound);
                _soundManagerFilesDataPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.SoundManager);

                _defaultMusicPath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Default);
                Directory.CreateDirectory(_defaultMusicPath);

                _platformMusicFilePath = Path.Combine(_extraMetaDataFolder, SoundDirectory.Platform);
                Directory.CreateDirectory(_platformMusicFilePath);

                _gameMusicFilePath = Path.Combine(_extraMetaDataFolder, SoundDirectory.GamesFolder);
                Directory.CreateDirectory(_gameMusicFilePath);

                SettingsModel = new PlayniteSoundsSettingsViewModel(this);
                Properties = new GenericPluginProperties
                {
                    HasSettings = true
                };

                Localization.SetPluginLanguage(PluginFolder, api.ApplicationSettings.Language);
                _musicPlayer = new MediaPlayer();
                _musicPlayer.MediaEnded += MediaEnded;
                _timeLine = new MediaTimeline();
                //{
                //    RepeatBehavior = RepeatBehavior.Forever                    
                //};

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
                    ConstructMainMenuItem("Migrate (temp)", Migrate),
                    ConstructMainMenuItem(Resource.ActionsOpenMusicFolder, OpenMusicFolder),
                    ConstructMainMenuItem(Resource.ActionsOpenSoundsFolder, OpenSoundsFolder),
                    ConstructMainMenuItem(Resource.ActionsReloadAudioFiles, ReloadAudioFiles),
                    ConstructMainMenuItem(Resource.ActionsHelp, HelpMenu),
                    ConstructMainMenuItem(Resource.ActionsUpdateLegacy, UpdateFromLegacyVersion)
                };

                DownloadManager = new DownloadManager(Settings);

                PlayniteApi.Database.Games.ItemCollectionChanged += CleanupDeletedGames;
                PlayniteApi.Database.Platforms.ItemCollectionChanged += CleanupDeletedPlatforms;
                PlayniteApi.UriHandler.RegisterSource("Sounds", HandleUriEvent);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        public void UpdateDownloadManager(PlayniteSoundsSettings settings)
            => DownloadManager = new DownloadManager(settings);
        

        private static string HelpLine(string baseMessage)
            => $"{SoundFile.DesktopPrefix}{baseMessage} - {SoundFile.FullScreenPrefix}{baseMessage}\n";

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
            if (!(_firstSelectSound && Settings.SkipFirstSelectSound))
            {
                PlaySoundFileFromName(SoundFile.GameSelectedSound);
            }
            _firstSelectSound = false;

            PlayMusicBasedOnSelected();
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
            // Add code to be executed when game is preparing to be started.
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
            // Add code to be executed when game is preparing to be started.
            PlaySoundFileFromName(SoundFile.GameStoppedSound);
            ResumeMusic();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            PlaySoundFileFromName(SoundFile.ApplicationStartedSound);

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Application.Current.MainWindow.StateChanged += OnWindowStateChanged;
            Application.Current.Deactivated += OnApplicationDeactivate;
            Application.Current.Activated += OnApplicationActivate;

            CopyAudioFiles();
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

            _musicPlayer.MediaEnded -= MediaEnded;
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
                CreateDownloadDialogue(games, Source.All);
            }

            Settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
            SavePluginSettings(Settings);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var gameMenuItems = new List<GameMenuItem>();

            if (Settings.Downloaders.Contains(Source.KHInsider))
            {
                gameMenuItems.Add(ConstructGameMenuItem(
                    "All", _ => DownloadMusicForSelectedGames(Source.All), "|" + Resource.Actions_Download));
                gameMenuItems.Add(ConstructGameMenuItem(
                    "KHInsider", _ => DownloadMusicForSelectedGames(Source.KHInsider), "|" + Resource.Actions_Download));
            }
            
            gameMenuItems.AddRange(_gameMenuItems);

            if (SingleGame())
            {
                var game = SelectedGames.First();

                var gameDirectory = CreateMusicDirectory(game);
                var songSubMenu = $"|{Resource.ActionsSubMenuSongs}|";

                ConstructItems(gameMenuItems, ConstructGameMenuItem, gameDirectory, songSubMenu, true);
            }

            return gameMenuItems;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var mainMenuItems = new List<MainMenuItem>(_mainMenuItems);

            foreach (var platform in PlayniteApi.Database.Platforms.OrderBy(o => o.Name))
            {
                var platformDirectory = CreatePlatformDirectory(platform.Name);

                var platformSelect = $"|{Resource.ActionsPlatform}|{platform.Name}";
                mainMenuItems.Add(ConstructMainMenuItem(
                    Resource.ActionsCopySelectMusicFile, 
                    () => SelectMusicForPlatform(platform.Name), 
                    platformSelect));

                var platformSongSubMenu = $"{platformSelect}|{Resource.ActionsSubMenuSongs}|";
                ConstructItems(mainMenuItems, ConstructMainMenuItem, platformDirectory, platformSongSubMenu);
            }

            var defaultSubMenu = $"|{Resource.ActionsDefault}";
            mainMenuItems.Add(
                ConstructMainMenuItem(Resource.ActionsCopySelectMusicFile, SelectMusicForDefault, defaultSubMenu));
            ConstructItems(mainMenuItems, ConstructMainMenuItem, _defaultMusicPath, defaultSubMenu + "|");

            return mainMenuItems;
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
        private void CleanupDeletedPlatforms(object sender, ItemCollectionChangedEventArgs<Platform> ItemCollectionChangedArgs)
        {
            foreach (var removedItem in ItemCollectionChangedArgs.RemovedItems)
            {
                var platformPath = GetPlatformDirectoryPath(removedItem.Name);
                if (Directory.Exists(platformPath))
                {
                    Directory.Delete(platformPath, true);
                }
            }
        }

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
            if (args.Mode == PowerModes.Resume)
            {
                Try(RestartMusic);
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
            if (_musicPlayer != null)
            {
                _musicPlayer.Volume = Settings.MusicVolume / 100.0;
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

        private void PlayMusicFromFirst(IEnumerable<Game> games = null)
        {
            var game = games.FirstOrDefault();

            string fileDirectory;
            switch (Settings.MusicType)
            {
                case MusicType.Game:
                    fileDirectory = CreateMusicDirectory(game);
                    break;
                case MusicType.Platform:
                    fileDirectory = CreatePlatformDirectoryPathFromGame(game);
                    break;
                default:
                    fileDirectory = _defaultMusicPath;
                    break;
            }
            PlayMusicFromDirectory(fileDirectory);
        }

        private void PlayMusicFromDirectory(string fileDirectory)
        {

            var musicFiles = Directory.GetFiles(fileDirectory);
            var musicFile = musicFiles.FirstOrDefault() ?? string.Empty;
            var musicEndRandom = _musicEnded && Settings.RandomizeOnMusicEnd;

            var rand = new Random();
            if (musicFiles.Length > 1 && (Settings.RandomizeOnEverySelect || musicEndRandom))
            {
                ReloadMusic = true;
                do
                {
                    musicFile = musicFiles[rand.Next(musicFiles.Length)];
                }
                while (_prevMusicFileName == musicFile);
            }

            PlayMusicFromPath(musicFile);
        }

        private void ResumeMusic()
        {
            if (ShouldPlayMusic() && _musicPlayer.Clock != null)
            {
                Try(_musicPlayer.Clock.Controller.Resume);
            }
        }

        private void PauseMusic()
        {
            if (ShouldPlayMusic() && _musicPlayer.Clock != null)
            {
                Try(_musicPlayer.Clock.Controller.Pause);
            }
        }

        private void CloseMusic()
        {
            if (_musicPlayer.Clock != null)
            {
                Try(SubCloseMusic);
            }
        }

        private void SubCloseMusic()
        {
            _musicPlayer.Clock.Controller.Stop();
            _musicPlayer.Clock = null;
            _musicPlayer.Close();
        }

        private void ForcePlayMusicFromPath(string filePath)
        {
            ReloadMusic = true;
            PlayMusicFromPath(filePath);
        }

        private void PlayMusicFromPath(string filePath)
        {
            //need to use directoryname on verification otherwise when game music randomly changes
            //on musicend music will be restarted when we select another game in for Default or Platform Mode
            //in case of "random music on selection" or "Random Music on Musicend" ReloadMusic will be set
            //check on empty needs to happen before directory verification or exceptions occur if no such music exists
            //it still needs to call the sub to play the music but it will just close the music as File.exists will fail there
            if (ReloadMusic || _prevMusicFileName.Equals(string.Empty) || filePath.Equals(string.Empty) ||
                (Path.GetDirectoryName(filePath) != Path.GetDirectoryName(_prevMusicFileName)))
            {
                Try(() => SubPlayMusicFromPath(filePath));
            }
        }

        private void SubPlayMusicFromPath(string filePath)
        {
            CloseMusic();
            ReloadMusic = false;
            _prevMusicFileName = string.Empty;
            if (File.Exists(filePath))
            {
                _prevMusicFileName = filePath;
                _timeLine.Source = new Uri(filePath);
                _musicPlayer.Volume = Settings.MusicVolume / 100.0;
                _musicPlayer.Clock = _timeLine.CreateClock();
                _musicPlayer.Clock.Controller.Begin();
                _musicEnded = false;
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
            /*Then*/ if (entry.MediaPlayer == null)
            {
                entry.SoundPlayer.Stop();
                entry.SoundPlayer.PlaySync();
            }
            else
            {
                entry.MediaPlayer.Stop();
                entry.MediaPlayer.Play();
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
                entry.MediaPlayer = new MediaPlayer();
                entry.MediaPlayer.Open(new Uri(fullFileName));
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
            if (entry.MediaPlayer != null)
            {
                var filename = entry.MediaPlayer.Source == null
                    ? string.Empty
                    : entry.MediaPlayer.Source.LocalPath;

                entry.MediaPlayer.Stop();
                entry.MediaPlayer.Close();
                entry.MediaPlayer = null;
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
            else if (_musicPlayer.Clock != null)
            {
                _musicPlayer.Clock.Controller.Stop();
                _musicPlayer.Clock.Controller.Begin();
            }
        }

        #endregion

        #region UI

        #region Menu UI

        private void ConstructItems<TMenuItem>(
            List<TMenuItem> menuItems, 
            Func<string, Action, string, TMenuItem> menuItemConstructor, 
            string directory, 
            string subMenu,
            bool isGame = false)
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                var songName = Path.GetFileNameWithoutExtension(file);
                var songSubMenu = subMenu + songName;

                menuItems.Add(menuItemConstructor(
                    Resource.ActionsCopyPlayMusicFile, () => ForcePlayMusicFromPath(file), songSubMenu));
                menuItems.Add(menuItemConstructor(
                    Resource.ActionsCopyDeleteMusicFile, () => DeleteMusicFile(file, songName, isGame), songSubMenu));
            }           
        }

        private static GameMenuItem ConstructGameMenuItem(string resource, Action action, string subMenu = "")
            => ConstructGameMenuItem(resource, _ => action(), subMenu);

        private static GameMenuItem ConstructGameMenuItem(string resource, Action<GameMenuItemActionArgs> action, string subMenu = "") => new GameMenuItem
        {
            MenuSection = App.AppName + subMenu,
            Icon = IconPath,
            Description = resource,
            Action = action
        };

        private static MainMenuItem ConstructMainMenuItem(string resource, Action action, string subMenu = "")
            => ConstructMainMenuItem(resource, _ => action(), subMenu);

        private static MainMenuItem ConstructMainMenuItem(string resource, Action<MainMenuItemActionArgs> action, string subMenu = "") => new MainMenuItem
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

        private Song PromptUserForYoutubeSearch(string gameName)
            => PromptForSelect<Song>(Resource.DialogMessageCaptionSong,
                gameName,
                s => SearchYoutube(s).Select(a => new GenericObjectOption(a.Name, a.ToString(), a) as GenericItemOption).ToList(),
                gameName + " soundtrack");

        private Album PromptForAlbum(string gameName, Source source)
            => PromptForSelect<Album>(Resource.DialogMessageCaptionAlbum,
                gameName,
                s => DownloadManager.GetAlbumsForGame(s, source)
                    .Select(a => new GenericObjectOption(a.Name, a.ToString(), a) as GenericItemOption).ToList(),
                gameName + (source is Source.Youtube ? " soundtrack" : string.Empty));

        private Song PromptForSong(List<Song> songsToPartialUrls, string albumName)
            => PromptForSelect<Song>(Resource.DialogMessageCaptionSong,
                albumName,
                a => songsToPartialUrls.OrderByDescending(s => s.Name.StartsWith(a))
                    .Select(s =>
                        new GenericObjectOption(s.Name, s.ToString(), s) as GenericItemOption).ToList(),
                string.Empty);

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
        }

        private void SubCopyAudioFiles(string soundFilesInstallPath)
        {
            CloseAudioFiles();

            Directory.CreateDirectory(_soundFilesDataPath);
            var files = Directory.GetFiles(soundFilesInstallPath);
            files.ForEach(f => File.Copy(f, Path.Combine(_soundFilesDataPath, Path.GetFileName(f)), true));
        }

        private void Migrate()
        {
            var dir = Path.Combine(GetPluginUserDataPath(), "Music Files\\Game");
            foreach (var directory in Directory.GetDirectories(dir))
            {
                var directoryName = directory.Substring(directory.LastIndexOf('\\') + 1);
                foreach(var file in Directory.GetFiles(directory))
                {
                    var fileName = file.Substring(file.LastIndexOf('\\') + 1);
                    var newDirectory = Path.Combine(_gameMusicFilePath, directoryName, SoundDirectory.Music);
                    var newFilePath = Path.Combine(newDirectory, fileName);

                    try
                    {
                        Directory.CreateDirectory(newDirectory);
                        File.Move(file, newFilePath);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"Failed to move file '{file}' to '{newFilePath}' due to: {e.Message}");
                    }
                }
            }
            Directory.Delete(dir, true);

            foreach(var directory in Directory.GetDirectories(GetPluginUserDataPath()))
            {
                Directory.Move(directory, _extraMetaDataFolder);
            }
        }

        private void UpdateFromLegacyVersion()
        {
            var oldDirectory = GetPluginUserDataPath();
            var oldMusicDirectory = Path.Combine(oldDirectory, SoundDirectory.Music);
            var orphanDirectory = Path.Combine(oldDirectory, SoundDirectory.Orphans);

            var platformDirectories = Directory.GetDirectories(oldMusicDirectory);

            Directory.CreateDirectory(orphanDirectory);

            var playniteGames = PlayniteApi.Database.Games.ToList();
            playniteGames.ForEach(g => g.Name = StringUtilities.SanitizeGameName(g.Name));

            platformDirectories.ForEach(p => UpdateLegacyPlatform(orphanDirectory, p, playniteGames));

            var defaultFile = Path.Combine(oldMusicDirectory, SoundFile.DefaultMusicName);
            if (File.Exists(defaultFile))
            {
                Logger.Info($"Moving default music file from music files data path...");

                File.Move(defaultFile, Path.Combine(_defaultMusicPath, SoundFile.DefaultMusicName));

                Logger.Info($"Moved default music file from music files data path.");
            }

            var soundFiles = Path.Combine(oldMusicDirectory, SoundDirectory.Sound);
            if (File.Exists(soundFiles))
            {
                Logger.Info($"Moving sound files from music files data path...");

                File.Move(soundFiles, Path.Combine(_defaultMusicPath, SoundDirectory.Sound));

                Logger.Info($"Moved default sound files from music files data path.");
            }

            var anyOrphans = Directory.GetFileSystemEntries(orphanDirectory).Any();
            if (anyOrphans)
            {
                var viewOrphans =
                    GetBoolFromYesNoDialog(string.Format(Resource.DialogUpdateLegacyOrphans, orphanDirectory));
                if (viewOrphans)
                {
                    Process.Start(orphanDirectory);
                }
            }
            else
            {
                ShowMessage(Resource.DialogMessageDone);
            }
        }

        private void UpdateLegacyPlatform(string orphanDirectory, string platformDirectory, IEnumerable<Game> games)
        {
            Logger.Info($"Working on Platform: {platformDirectory}");

            var platformDirectoryName = GetDirectoryNameFromPath(platformDirectory);
            
            if (platformDirectoryName.Equals(SoundDirectory.Default, StringComparison.OrdinalIgnoreCase) ||
                platformDirectoryName.Equals(SoundDirectory.Orphans, StringComparison.Ordinal))
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
        
        private void MoveLegacyGameFile(string looseGameFile, string platformDirectoryName, string orphanDirectory, IEnumerable<Game> games)
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

            PlayMusicFromFirst(SelectedGames);
        }

        private void SelectMusicForSelectedGames()
        {
            RestartMusicAfterSelect(
                () => SelectedGames.Select(g => SelectMusicForDirectory(CreateMusicDirectory(g))).FirstOrDefault(),
                SingleGame() && Settings.MusicType is MusicType.Game);

            Game game = SelectedGames.FirstOrDefault();
            UpdateMissingTag(game, Directory.GetFiles(GetMusicDirectoryPath(game)).HasNonEmptyItems(), CreateMusicDirectory(game));
        }

        private void SelectMusicForPlatform(string platform)
        {
            var playNewMusic = 
                Settings.MusicType is MusicType.Platform
                && SingleGame()
                && SelectedGames.First().Platforms.Any(p => p.Name == platform);

            RestartMusicAfterSelect(() => SelectMusicForDirectory(CreatePlatformDirectory(platform)), playNewMusic);
        }

        private void SelectMusicForDefault()
            => RestartMusicAfterSelect(
                () => SelectMusicForDirectory(_defaultMusicPath),
                Settings.MusicType is MusicType.Default);

        private List<string> SelectMusicForDirectory(string directory)
        {
            var newMusicFiles = PlayniteApi.Dialogs.SelectFiles("MP3 File|*.mp3") ?? new List<string>();

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

            args.ProgressMaxValue = games.Count;
            foreach (var game in games.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                args.Text = $"{progressTitle}\n\n{++args.CurrentProgressValue}/{args.ProgressMaxValue}\n{game.Name}";

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
                FileName = Settings.FFmpegNormalizePath
            };

            info.EnvironmentVariables["FFMPEG_PATH"] = Settings.FFmpegPath;

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


        private IEnumerable<Song> SearchYoutube(string search)
        {
            var album = DownloadManager.GetAlbumsForGame(search, Source.Youtube).First();
            return DownloadManager.GetSongsFromAlbum(album);
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

            CloseMusic();

            CreateDownloadDialogue(games, source, albumSelect, songSelect, overwriteSelect);

            ShowMessage(Resource.DialogMessageDone);

            ReloadMusic = true;
            ReplayMusic();
        }

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
            args.ProgressMaxValue = games.Count;
            foreach (var game in games.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                args.Text = $"{progressTitle}\n\n{++args.CurrentProgressValue}/{args.ProgressMaxValue}\n{game.Name}";

                var gameDirectory = CreateMusicDirectory(game);

                var newFilePath = 
                    DownloadSongFromGame(source, game.Name, gameDirectory, songSelect, albumSelect, overwrite);

                var fileDownloaded = newFilePath != null;
                if (Settings.NormalizeMusic && fileDownloaded)
                {
                    args.Text += $" - {Resource.DialogMessageNormalizingFiles}";
                    if (NormalizeAudioFile(newFilePath))
                    {
                        UpdateNormalizedTag(game);
                    }
                }

                UpdateMissingTag(game, fileDownloaded, gameDirectory);
            }
        }

        private string DownloadSongFromGame(
            Source source,
            string gameName,
            string gameDirectory,
            bool songSelect,
            bool albumSelect,
            bool overwrite)
        {

            var strippedGameName = StringUtilities.StripStrings(gameName);

            var regexGameName = songSelect && albumSelect
                ? string.Empty
                : StringUtilities.ReplaceStrings(strippedGameName);

            var album = SelectAlbumForGame(source, gameName, strippedGameName, regexGameName, albumSelect, songSelect);
            if (album is null)
            {
                return null;
            }

            Logger.Info($"Selected album '{album.Name}' from source '{album.Source}' for game '{gameName}'");

            var song = SelectSongFromAlbum(album, gameName, strippedGameName, regexGameName, songSelect);
            if (song is null)
            {
                return null;
            }

            Logger.Info($"Selected song '{song.Name}' from album '{album.Name}' for game '{gameName}'");

            var sanitizedFileName = StringUtilities.SanitizeGameName(song.Name) + ".mp3";
            var newFilePath = Path.Combine(gameDirectory, sanitizedFileName);
            if (!overwrite && File.Exists(newFilePath))
            {
                Logger.Info($"Song file '{sanitizedFileName}' for game '{gameName}' already exists. Skipping....");
                return null;
            }

            Logger.Info($"Overwriting song file '{sanitizedFileName}' for game '{gameName}'.");

            if (!DownloadManager.DownloadSong(song, newFilePath))
            {
                Logger.Info($"Failed to download song '{song.Name}' for album '{album.Name}' of game '{gameName}' with source {song.Source} and Id '{song.Id}'");
                return null;
            }

            Logger.Info($"Downloaded file '{sanitizedFileName}' in album '{album.Name}' of game '{gameName}'");
            return newFilePath;
        }

        private Album SelectAlbumForGame(
            Source source, 
            string gameName, 
            string strippedGameName, 
            string regexGameName,
            bool albumSelect, 
            bool songSelect)
        {
            Album album = null;

            var skipAlbumSearch = OnlySearchForYoutubeVideos(source) && songSelect;
            if (skipAlbumSearch)
            {
                Logger.Info($"Skipping album search for game '{gameName}'");
                album = new Album { Name = Resource.YoutubeSearch, Source = Source.Youtube };
            }
            else
            {
                Logger.Info($"Starting album search for game '{gameName}'");

                if (albumSelect)
                {
                    album = PromptForAlbum(strippedGameName, source);
                }
                else
                {
                    var albums = DownloadManager.GetAlbumsForGame(strippedGameName, source, true).ToList();
                    if (albums.Any())
                    {
                        album = DownloadManager.BestAlbumPick(albums, strippedGameName, regexGameName);
                    }
                    else
                    {
                        Logger.Info($"Did not find any albums for game '{gameName}' from source '{source}'");
                    }
                }
            }

            return album;
        }

        private Song SelectSongFromAlbum(
            Album album,
            string gameName,
            string strippedGameName,
            string regexGameName,
            bool songSelect)
        {
            Song song = null;

            if (OnlySearchForYoutubeVideos(album.Source))
            {
                song = songSelect
                    ? PromptUserForYoutubeSearch(strippedGameName)
                    : DownloadManager.BestSongPick(album.Songs.ToList(), regexGameName);
            }
            else
            {
                var songs = DownloadManager.GetSongsFromAlbum(album).ToList();
                if (!songs.Any())
                {
                    Logger.Info($"Did not find any songs for album '{album.Name}' of game '{gameName}'");
                }
                else
                {
                    Logger.Info($"Found songs for album '{album.Name}' of game '{gameName}'");
                    song = songSelect
                        ? PromptForSong(songs, regexGameName)
                        : DownloadManager.BestSongPick(songs, regexGameName);
                }
            }

            return song;
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
                var normalizedTag = PlayniteApi.Database.Tags.Add(Resource.NormTag);
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

        private bool ShouldPlayMusic() => _pausers.Count is 0 && ShouldPlayAudio(Settings.MusicState);

        private bool ShouldPlayAudio(AudioState state)
        {
            var desktopMode = IsDesktop();

            var playOnFullScreen = !desktopMode && state == AudioState.Fullscreen;
            var playOnBoth = state == AudioState.Always;
            var playOnDesktop = desktopMode && state == AudioState.Desktop;

            return !_gameRunning && (playOnFullScreen || playOnBoth || playOnDesktop);
        }

        private void ShowMessage(string resource) => Dialogs.ShowMessage(resource, App.AppName);

        private bool IsDesktop() => PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop;

        private bool SingleGame() => SelectedGames.Count() == 1;

        private string GetMusicDirectoryPath(Game game)
            => Path.Combine(_gameMusicFilePath, game.Id.ToString(), SoundDirectory.Music);

        private string CreatePlatformDirectoryPathFromGame(Game game) 
            => CreatePlatformDirectory(game?.Platforms?.FirstOrDefault()?.Name ?? SoundDirectory.NoPlatform);

        private string CreateMusicDirectory(Game game)
            => Directory.CreateDirectory(GetMusicDirectoryPath(game)).FullName;

        private string CreatePlatformDirectory(string platform)
            => Directory.CreateDirectory(GetPlatformDirectoryPath(platform)).FullName;

        private string GetPlatformDirectoryPath(string platform)
            => Path.Combine(_platformMusicFilePath, platform);

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
                    case 0 when Settings.PlayBackgroundWhenNoneSelected:
                        PlayMusicFromDirectory(_defaultMusicPath);
                        break;
                }
            }
        }

        public void HandleException(Exception e)
        {
            Logger.Error(e, new StackTrace(e).GetFrame(0).GetMethod().Name);
            Dialogs.ShowErrorMessage(e.Message, App.AppName);
        }

        public void Try(Action action) { try { action(); } catch (Exception ex) { HandleException(ex); } }

        private PlayniteSoundsSettings Settings => SettingsModel.Settings;
        private IEnumerable<Game> SelectedGames => PlayniteApi.MainView.SelectedGames;
        private IDialogsFactory Dialogs => PlayniteApi.Dialogs;

        #endregion
    }
}
