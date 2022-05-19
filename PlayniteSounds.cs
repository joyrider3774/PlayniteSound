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

        private static readonly IDownloadManager DownloadManager = new DownloadManager();
        private PlayniteSoundsSettingsViewModel SettingsModel { get; }

        private bool _gameRunning;
        private bool _musicEnded = false;
        private bool _firstSelectSound = true;
        private bool _closeAudioFilesNextPlay;

        private string _prevMusicFileName = string.Empty;  //used to prevent same file being restarted 

        private readonly string _pluginUserDataPath;
        private readonly string _musicFilesDataPath;
        private readonly string _soundFilesDataPath;
        private readonly string _soundManagerFilesDataPath;
        private readonly string _defaultMusicPath;
        private readonly string _gameMusicFilePath;
        private readonly string _platformMusicFilePath;
        private readonly string _orphanDirectory;

        private readonly Dictionary<string, PlayerEntry> _players = new Dictionary<string, PlayerEntry>();

        private MediaPlayer _musicPlayer;
        private readonly MediaTimeline _timeLine;

        private readonly List<GameMenuItem> _gameMenuItems;
        private readonly List<MainMenuItem> _mainMenuItems;

        #region Constructor

        public PlayniteSounds(IPlayniteAPI api) : base(api)
        {
            try
            {
                SoundFile.ApplicationInfo = PlayniteApi.ApplicationInfo;

                _pluginUserDataPath = GetPluginUserDataPath();

                _musicFilesDataPath = Path.Combine(_pluginUserDataPath, SoundDirectory.Music);
                _soundFilesDataPath = Path.Combine(_pluginUserDataPath, SoundDirectory.Sound);
                _soundManagerFilesDataPath = Path.Combine(_pluginUserDataPath, SoundDirectory.SoundManager);

                _defaultMusicPath = Path.Combine(_musicFilesDataPath, SoundDirectory.Default);
                Directory.CreateDirectory(_defaultMusicPath);

                _platformMusicFilePath = Path.Combine(_musicFilesDataPath, SoundDirectory.Platform);
                Directory.CreateDirectory(_platformMusicFilePath);

                _gameMusicFilePath = Path.Combine(_musicFilesDataPath, SoundDirectory.Game);
                Directory.CreateDirectory(_gameMusicFilePath);

                _orphanDirectory = Path.Combine(_musicFilesDataPath, SoundDirectory.Orphans);

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
                    ConstructGameMenuItem(Resource.ActionsCopySelectMusicFile, SelectMusicForSelectedGames),
                    ConstructGameMenuItem(Resource.ActionsDownloadMusicForGames, DownloadMusicForSelectedGames),
                    ConstructGameMenuItem(Resource.ActionsOpenSelected, OpenMusicDirectory),
                    ConstructGameMenuItem(Resource.ActionsDeleteSelected, DeleteMusicDirectories)
                };

                _mainMenuItems = new List<MainMenuItem>
                {
                    ConstructMainMenuItem(Resource.ActionsOpenMusicFolder, OpenMusicFolder),
                    ConstructMainMenuItem(Resource.ActionsOpenSoundsFolder, OpenSoundsFolder),
                    ConstructMainMenuItem(Resource.ActionsReloadAudioFiles, ReloadAudioFiles),
                    ConstructMainMenuItem(Resource.ActionsHelp, HelpMenu),
                    ConstructMainMenuItem(Resource.ActionsUpdateLegacy, UpdateFromLegacyVersion)
                };
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

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
            Application.Current.Deactivated += OnApplicationDeactivate;
            Application.Current.Activated += OnApplicationActivate;
            Application.Current.MainWindow.StateChanged += OnWindowStateChanged;

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
                CreateDownloadDialogue(games);
            }

            Settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
            SavePluginSettings(Settings);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var gameMenuItems = new List<GameMenuItem>(_gameMenuItems);

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

            foreach (var platform in PlayniteApi.Database.Platforms.OrderBy((o) => o.Name))
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
            ConstructItems(mainMenuItems, ConstructMainMenuItem, _musicFilesDataPath, defaultSubMenu + "|");
            mainMenuItems.Add(
                ConstructMainMenuItem(Resource.ActionsCopySelectMusicFile, SelectMusicForDefault, defaultSubMenu));

            return mainMenuItems;
        }

        #endregion

        #region State Changes

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (Settings.PauseOnDeactivate)
            /*Then*/ switch (Application.Current?.MainWindow?.WindowState)
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

        private void PlayMusicFromFirst(IEnumerable<Game> games)
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

            var musicFiles = Directory.GetFiles(fileDirectory);
            var musicFile = musicFiles.FirstOrDefault() ?? string.Empty;

            var rand = new Random();
            if (musicFiles.Length > 1 && (Settings.RandomizeOnEverySelect ||
                (_musicEnded && Settings.RandomizeOnMusicEnd)))
            /*Then*/
            do
            {
                musicFile = musicFiles[rand.Next(musicFiles.Length)];
            }
            while (_prevMusicFileName == musicFile);

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

        private void PlayMusicFromPath(string filePath)
        {
            if (ReloadMusic || filePath != _prevMusicFileName)
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
            var fullFileName = Path.Combine(_pluginUserDataPath, SoundDirectory.Sound, fileName);

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
                    Resource.ActionsCopyPlayMusicFile, () => PlayMusicFromPath(file), songSubMenu));
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

        private GenericItemOption PromptForAlbum(string gameName)
            => PromptForSelect(Resource.DialogMessageCaptionAlbum,
                gameName, a => DownloadManager.GetAlbumsForGame(a).ToList(), gameName);

        private GenericItemOption PromptForSong(List<GenericItemOption> songsToPartialUrls, string albumName)
            => PromptForSelect(Resource.DialogMessageCaptionSong,
                albumName, a => songsToPartialUrls.OrderByDescending(s => s.Name.StartsWith(a)).ToList(), string.Empty);

        private GenericItemOption PromptForSelect(
            string captionFormat,
            string formatArg,
            Func<string, List<GenericItemOption>> search,
            string defaultSearch)
            => Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(), search, defaultSearch, string.Format(captionFormat, formatArg));

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

        private void UpdateFromLegacyVersion()
        {
            var platformDirectories = Directory.GetDirectories(_musicFilesDataPath);

            Directory.CreateDirectory(_orphanDirectory);

            var playniteGames = PlayniteApi.Database.Games.ToList();
            playniteGames.ForEach(g => g.Name = StringUtilities.SanitizeGameName(g.Name));

            platformDirectories.ForEach(p => UpdateLegacyPlatform(p, playniteGames));

            var anyOrphans = Directory.GetFileSystemEntries(_orphanDirectory).Any();
            if (anyOrphans)
            {
                var viewOrphans = 
                    GetBoolFromYesNoDialog(string.Format(Resource.DialogUpdateLegacyOrphans, _orphanDirectory));
                if (viewOrphans)
                {
                    Process.Start(_orphanDirectory);
                }
            }
        }

        private void UpdateLegacyPlatform(string platformDirectory, IEnumerable<Game> games)
        {
            Logger.Info($"Working on Platform: {platformDirectory}");

            var platformDirectoryName = GetDirectoryNameFromPath(platformDirectory);
            
            if (platformDirectory.Equals(_defaultMusicPath, StringComparison.OrdinalIgnoreCase) ||
                platformDirectory.Equals(_gameMusicFilePath, StringComparison.OrdinalIgnoreCase) ||
                platformDirectory.Equals(_platformMusicFilePath, StringComparison.OrdinalIgnoreCase) ||
                platformDirectory.Equals(_orphanDirectory, StringComparison.Ordinal))
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
            gameFiles.ForEach(g => MoveLegacyGameFile(g, platformDirectoryName, games));

            Logger.Info($"Deleting {platformDirectory}...");
            Directory.Delete(platformDirectory);
        }
        
        private void MoveLegacyGameFile(string looseGameFile, string platformDirectoryName, IEnumerable<Game> games)
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
                Logger.Info($"No corresponding game or a conflicting file exits for '{looseGameFileName}'");
                var orphanPlatformDirectory = Path.Combine(_orphanDirectory, platformDirectoryName);
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
            Directory.Delete(gameDirectory, true);
            UpdateMissingTag(game, false, gameDirectory);
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
            UpdateMissingTag(game, Directory.GetFiles(CreateMusicDirectory(game)).HasNonEmptyItems(), CreateMusicDirectory(game));
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
                PlayMusicFromPath(newMusicFile);
            }
            else
            {
                PlayMusicBasedOnSelected();
            }
        }

        #endregion

        #region Download

        private void DownloadMusicForSelectedGames() => PromptUserToDownload(SelectedGames);

        private void PromptUserToDownload(IEnumerable<Game> games)
        {
            var albumSelect = GetBoolFromYesNoDialog(Resource.DialogMessageAlbumSelect);
            var songSelect = GetBoolFromYesNoDialog(Resource.DialogMessageSongSelect);
            var overwriteSelect = GetBoolFromYesNoDialog(Resource.DialogMessageOverwriteSelect);

            CloseMusic();

            CreateDownloadDialogue(games, albumSelect, songSelect, overwriteSelect);

            ShowMessage(Resource.DialogMessageDone);

            ReloadMusic = true;
            ReplayMusic();
        }

        private void CreateDownloadDialogue(
            IEnumerable<Game> games,
            bool albumSelect = false,
            bool songSelect = false,
            bool overwriteSelect = false)
        {
            var progressTitle = $"{App.AppName}-{Resource.DialogMessageDownloadingFiles}";
            var progressOptions = new GlobalProgressOptions(progressTitle, true) { IsIndeterminate = false };

            Dialogs.ActivateGlobalProgress(
                a => Try(() => StartDownload(a, games.ToList(), progressTitle, albumSelect, songSelect, overwriteSelect)),
                progressOptions);
        }

        private void StartDownload(
            GlobalProgressActionArgs args,
            List<Game> games,
            string progressTitle,
            bool albumSelect,
            bool songSelect,
            bool overwrite)
        {
            args.ProgressMaxValue = games.Count;
            foreach (var game in games.TakeWhile(_ => !args.CancelToken.IsCancellationRequested))
            {
                args.Text = $"{progressTitle}\n\n{args.CurrentProgressValue++}/{args.ProgressMaxValue}\n{game.Name}";

                var gameDirectory = CreateMusicDirectory(game);

                var downloadSucceeded = 
                    DownloadSongFromGame(game.Name, gameDirectory, songSelect, albumSelect, overwrite);

                UpdateMissingTag(game, downloadSucceeded, gameDirectory);
            }
        }

        private bool DownloadSongFromGame(
            string gameName, string gameDirectory, bool songSelect, bool albumSelect, bool overwrite)
        {
            Logger.Info($"Starting album search for game '{gameName}'");

            var strippedGameName = StringUtilities.StripStrings(gameName);

            var regexGameName = songSelect && albumSelect
                ? string.Empty
                : StringUtilities.ReplaceStrings(strippedGameName);

            GenericItemOption album = null;
            if (albumSelect)
            {
                album = PromptForAlbum(strippedGameName);
            }
            else
            {
                var albums = DownloadManager.GetAlbumsForGame(strippedGameName).ToList();
                if (albums.Any())
                {
                    album = DownloadManager.BestAlbumPick(albums, strippedGameName, regexGameName);
                }
                else
                {
                    Logger.Info($"Did not find any albums for game '{gameName}'");
                }
            }

            if (album == null)
            {
                return false;
            }

            Logger.Info($"Selected album '{album.Name}' for game '{gameName}'");

            var songs = DownloadManager.GetSongsFromAlbum(album).ToList();
            if (!songs.Any())
            {
                Logger.Info($"Did not find any songs for album '{album.Name}' of game '{gameName}'");
                return false;
            }

            Logger.Info($"Found songs for album '{album.Name}' of game '{gameName}'");

            var songToPartialUrl = songSelect
                ? PromptForSong(songs, album.Name)
                : DownloadManager.BestSongPick(songs, regexGameName);

            if (songToPartialUrl == null)
            {
                return false;
            }

            var sanitizedFileName = StringUtilities.SanitizeGameName(songToPartialUrl.Name) + ".mp3";
            var newFilePath = Path.Combine(gameDirectory, sanitizedFileName);
            if (overwrite && File.Exists(newFilePath))
            {
                Logger.Info($"Song file '{sanitizedFileName}' for game '{gameName}' already exists. Skipping....");
                return false;
            }

            Logger.Info($"Overwriting song file '{sanitizedFileName}' for game '{gameName}'.");

            if (!DownloadManager.DownloadSong(songToPartialUrl, newFilePath))
            {
                Logger.Info($"Failed to download song '{songToPartialUrl.Name}' for album '{album.Name}' of game '{gameName}' from url '{songToPartialUrl.Description}'");
                return false;
            }

            Logger.Info($"Downloaded file '{sanitizedFileName}' in album '{album.Name}' of game '{gameName}'");
            return true;
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

        private bool AddTagToGame(Game game, Tag tag)
        {
            if (game.Tags == null)
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

        private bool ShouldPlayMusic() => ShouldPlayAudio(Settings.MusicState);

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
            => Path.Combine(_gameMusicFilePath, game.Id.ToString());

        private string CreatePlatformDirectoryPathFromGame(Game game) 
            => CreatePlatformDirectory(game.Platforms?.FirstOrDefault()?.Name ?? SoundDirectory.NoPlatform);

        private string CreateMusicDirectory(Game game)
            => Directory.CreateDirectory(GetMusicDirectoryPath(game)).FullName;

        private string CreatePlatformDirectory(string platform)
            => Directory.CreateDirectory(Path.Combine(_platformMusicFilePath, platform)).FullName;

        private static string GetDirectoryNameFromPath(string directory)
            => directory.Substring(directory.LastIndexOf('\\')).Replace("\\", string.Empty);

        private void PlayMusicBasedOnSelected()
        {
            if (ShouldPlayMusicOrClose())
            /*Then*/
            switch (SelectedGames.Count())
            {
                case 1:
                    PlayMusicFromFirstSelected();
                    break;
                case 0 when Settings.PlayBackgroundWhenNoneSelected:
                    PlayMusicFromPath(_defaultMusicPath);
                    break;
            }
        }

        public void HandleException(Exception e)
        {
            Logger.Error(e, new StackTrace(e).GetFrame(0).GetMethod().Name);
            Dialogs.ShowErrorMessage(e.Message, App.AppName);
        }

        private void Try(Action action) { try { action(); } catch (Exception ex) { HandleException(ex); } }

        private PlayniteSoundsSettings Settings => SettingsModel.Settings;
        private IEnumerable<Game> SelectedGames => PlayniteApi.MainView.SelectedGames;
        private IDialogsFactory Dialogs => PlayniteApi.Dialogs;

        #endregion
    }
}