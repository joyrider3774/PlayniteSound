using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
        public bool MusicNeedsReload { get; set; }
        public bool MusicFilenameNeedsReload { get; set; }

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
        private PlayniteSoundsSettingsViewModel SettingsModel { get; set; }
        private PlayniteSoundsSettings Settings => SettingsModel.Settings;

        private bool _gameRunning = false;
        private bool _firstSelectSound = true;
        private bool _closeAudioFilesNextPlay = false;

        private string _prevMusicFileName = string.Empty;   //used to prevent same file being restarted 
        private string _prevMusicFileName2 = string.Empty;   //used with the new 'don't randomize on every select' option in case of multiple files
        private string _prevMusicGameData = string.Empty;    //used with the new 'don't randomize on every select' option in case of multiple files

        private readonly string _musicFilesDataPath;
        private readonly string _soundFilesDataPath;
        private readonly string _soundManagerFilesDataPath;

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
                SoundFile.PlayniteApi = PlayniteApi;

                _musicFilesDataPath = Path.Combine(GetPluginUserDataPath(), SoundDirectory.Music);
                _soundFilesDataPath = Path.Combine(GetPluginUserDataPath(), SoundDirectory.Sound);
                _soundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), SoundDirectory.SoundManager);

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
                    ConstructGameMenuItem(Resource.ActionsShowMusicFilename, ShowMusicFilename),
                    ConstructGameMenuItem(Resource.ActionsDownloadMusicForGames, PromptUserToDownload)
                };

                _mainMenuItems = new List<MainMenuItem>
                {
                    ConstructMainMenuItem(Resource.ActionsShowMusicFilename, ShowMusicFilename),
                    ConstructMainMenuItem(Resource.ActionsDownloadMusicForGames, PromptUserToDownload),
                    ConstructMainMenuItem(Resource.ActionsOpenMusicFolder, OpenMusicFolder),
                    ConstructMainMenuItem(Resource.ActionsOpenSoundsFolder, OpenSoundsFolder),
                    ConstructMainMenuItem(Resource.ActionsReloadAudioFiles, ReloadAudioFiles),
                    ConstructMainMenuItem(Resource.ActionsHelp, HelpMenu)
                };
            }


            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private static string HelpLine(string baseMessage)
            => $"{SoundFile.DesktopPrefix}{baseMessage} - {SoundFile.FullScreenPrefix}{baseMessage}\n";

        #endregion

        #region Playnite Interface

        public override Guid Id { get; } = Guid.Parse("9c960604-b8bc-4407-a4e4-e291c6097c7d");

        public override ISettings GetSettings(bool firstRunSettings) => SettingsModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new PlayniteSoundsSettingsView(this);

        // Add code to be executed when game is finished installing.
        public override void OnGameInstalled(OnGameInstalledEventArgs args)
            => PlaySoundFileFromName(SoundFile.GameInstalledSound);

        // Add code to be executed when game is uninstalled.
        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
            => PlaySoundFileFromName(SoundFile.GameUninstalledSound);

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            if (!(_firstSelectSound && Settings.SkipFirstSelectSound))
            {
                PlaySoundFileFromName(SoundFile.GameSelectedSound);
            }
            _firstSelectSound = false;

            if (ShouldPlayMusicOrClose())
            {
                if (args.NewValue.Count == 1)
                {
                    PlayMusicOnSelect(args.NewValue);
                }
                else if (args.NewValue.Count == 0 && Settings.PlayBackgroundWhenNoneSelected)
                {
                    var filePath = GetMusicFileName(SoundFile.DefaultMusicName, string.Empty);
                    PlayMusicFromPath(filePath);
                }
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            // Add code to be executed when game is started running.
            if (Settings.StopMusic)
            {
                PauseMusic();
                _gameRunning = true;
            }
            PlaySoundFileFromName(SoundFile.GameStartedSound);
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

            InitialCopyAudioFiles();
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

            PlaySoundFileFromName(SoundFile.ApplicationStoppedSound);
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
                var games = PlayniteApi.Database.Games.Where(x => x.Added != null && x.Added > Settings.LastAutoLibUpdateAssetsDownload);
                CreateDownloadDialogue(games);
            }

            Settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
            SavePluginSettings(Settings);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
            => GetMenuItems(_gameMenuItems, ConstructGameMenuItem);

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
            => GetMenuItems(_mainMenuItems, ConstructMainMenuItem);

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
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;

            try
            {
                _closeAudioFilesNextPlay = true;
                MusicNeedsReload = true;
                //Restart music:
                ReplayMusic();
            }
            catch (Exception E)
            {
                HandleException(E);
            }
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
                PlayMusicOnSelect(PlayniteApi.MainView.SelectedGames);
            }
        }

        private void PlayMusicOnSelect(IEnumerable<Game> games)
        {
            var (gameName, platform) = GetGameInfoFromGames(games);
            var filePath = MusicTypeToFilePath(gameName, platform);

            PlayMusicFromPath(filePath);
        }

        private void ResumeMusic()
        {
            if (!ShouldPlayMusic() || _musicPlayer.Clock == null) return;

            try
            {
                _musicPlayer.Clock.Controller.Resume();
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void PauseMusic()
        {
            if (!ShouldPlayMusic() || _musicPlayer.Clock == null) return;

            try
            {
                _musicPlayer.Clock.Controller.Pause();
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void CloseMusic()
        {
            if (_musicPlayer.Clock == null) return;

            try
            {
                _musicPlayer.Clock.Controller.Stop();
                _musicPlayer.Clock = null;
                _musicPlayer.Close();
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void PlayMusicFromIndex(int fileNumber)
        {
            if (SingleGame())
            {
                var musicFileName = GetSelectedGameFileName(fileNumber);

                PlayMusicFromPath(musicFileName);

            }
            else
            {
                ShowMessage(Resource.MsgSelectSingleGame);
            }
        }

        private void PlayMusicFromPath(string filePath)
        {
            if (!MusicNeedsReload && filePath == _prevMusicFileName) return;

            try
            {
                CloseMusic();
                MusicNeedsReload = false;
                _prevMusicFileName = string.Empty;
                if (File.Exists(filePath))
                {
                    _prevMusicFileName = filePath;
                    _timeLine.Source = new Uri(filePath);
                    _musicPlayer.Volume = Settings.MusicVolume / 100.0;
                    _musicPlayer.Clock = _timeLine.CreateClock();
                    _musicPlayer.Clock.Controller.Begin();
                }
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void PlaySoundFileFromName(string fileName)
        {
            if (!ShouldPlaySound()) return;

            try
            {
                if (_closeAudioFilesNextPlay)
                {
                    CloseAudioFiles();
                    _closeAudioFilesNextPlay = false;
                }

                _players.TryGetValue(fileName, out var entry);
                if (entry == null)
                {
                    entry = CreatePlayerEntry(fileName);
                }

                if (entry.FileExists)
                {
                    if (entry.MediaPlayer != null)
                    {
                        entry.MediaPlayer.Stop();
                        entry.MediaPlayer.Play();
                    }
                    else
                    {
                        entry.SoundPlayer.Stop();
                        entry.SoundPlayer.PlaySync();
                    }
                }
                else 
                { 
                }
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private PlayerEntry CreatePlayerEntry(string fileName, bool useSoundPlayer = false)
        {
            string FullFileName = Path.Combine(GetPluginUserDataPath(), SoundDirectory.Sound, fileName);

            var fileExists = File.Exists(FullFileName);

            var entry = new PlayerEntry { FileExists = fileExists };
            _players[fileName] = entry;

            if (fileExists)
            {
                // MediaPlayer can play multiple sounds together from mulitple instances, but the SoundPlayer can not
                if (useSoundPlayer)
                {
                    entry.SoundPlayer = new SoundPlayer();
                    entry.SoundPlayer.SoundLocation = FullFileName;
                    entry.SoundPlayer.Load();
                }
                else
                {
                    entry.MediaPlayer = new MediaPlayer();
                    entry.MediaPlayer.Open(new Uri(FullFileName));
                }
            }

            return entry;
        }

        private void CloseAudioFiles()
        {
            try
            {
                foreach (string keyname in _players.Keys)
                {
                    PlayerEntry Entry = _players[keyname];
                    ClosePlayerEntry(Entry);
                }
                _players.Clear();
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void ClosePlayerEntry(PlayerEntry entry)
        {
            if (!entry.FileExists) return;

            if (entry.MediaPlayer != null)
            {
                string filename = entry.MediaPlayer.Source != null
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
            if (Settings.RandomizeOnMusicEnd)
            {
                //will play random song on case of multiple (could be same song)
                CloseMusic();
                MusicNeedsReload = true;
                MusicFilenameNeedsReload = true;
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

        private IEnumerable<TMenuItem> GetMenuItems<TMenuItem>(
            IEnumerable<TMenuItem> defaultMenuItems, 
            Func<string, Action, TMenuItem> 
            menuItemConstructor)
        {
            var mainMenuItems = new List<TMenuItem>(defaultMenuItems);

            if (!SingleGame()) return mainMenuItems;

            for (var i = 0; i < 5; i++)
            {
                var fileExists = MusicFileNameExists(i);

                var fileExistsStr = fileExists ? "[*]" : "[ ]";
                var description = $"{Resource.ActionsCopySelectMusicFile} {i} {fileExistsStr}";

                AddMenuItem(description, mainMenuItems, menuItemConstructor, SelectMusicFilename, i);

                if (fileExists)
                {
                    AddMenuItem($"{Resource.ActionsCopyPlayMusicFile} {i}",
                        mainMenuItems, menuItemConstructor, PlayMusicFromIndex, i);
                    AddMenuItem($"{Resource.ActionsCopyDeleteMusicFile} {i}",
                        mainMenuItems, menuItemConstructor, DeleteMusicFileName, i);
                }
            }

            return mainMenuItems;
        }

        private static void AddMenuItem<TMenuItem>(
            string description,
            List<TMenuItem> menuItems,
            Func<string, Action, TMenuItem> menuItemConstructor,
            Action<int> action,
            int index)
            => menuItems.Add(menuItemConstructor(description, () => { action(index); }));

        private static GameMenuItem ConstructGameMenuItem(string resource, Action action)
            => ConstructGameMenuItem(resource, _ => action());

        private static GameMenuItem ConstructGameMenuItem(string resource, Action<GameMenuItemActionArgs> action)
            => new GameMenuItem
            {
                MenuSection = App.AppName,
                Icon = IconPath,
                Description = resource,
                Action = action
            };

        private static MainMenuItem ConstructMainMenuItem(string resource, Action action)
            => ConstructMainMenuItem(resource, _ => action());

        private static MainMenuItem ConstructMainMenuItem(string resource, Action<MainMenuItemActionArgs> action)
            => new MainMenuItem
            {
                MenuSection = App.MainMenuName,
                Icon = IconPath,
                Description = resource,
                Action = action
            };

        public void ShowMusicFilename()
        {
            if (SingleGame())
            {
                var musicFileName = GetSelectedGameFileName(0, true);
                PlayniteApi.Dialogs.ShowSelectableString(Resource.MsgMusicPath, App.AppName, musicFileName + "\n\n" + musicFileName.Replace(".mp3", ".<1-9>.mp3"));
            }
            else
            {
                ShowMessage(Resource.MsgSelectSingleGame);
            }
                
        }

        #endregion

        #region Download UI

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
            => PlayniteApi.Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(), search, defaultSearch, string.Format(captionFormat, formatArg));

        #endregion

        #region Settings UI

        #region Actions UI

        public void OpenMusicFolder() => OpenFolder(_musicFilesDataPath);

        public void OpenSoundsFolder() => OpenFolder(_soundFilesDataPath);

        private void OpenFolder(string folderPath)
        {
            try
            {
                //need to release them otherwise explorer can't overwrite files even though you can delete them
                CloseAudioFiles();
                // just in case user deleted it
                Directory.CreateDirectory(folderPath);
                Process.Start(folderPath);
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        public void HelpMenu() => PlayniteApi.Dialogs.ShowMessage(HelpMessage.Value, App.AppName);

        #endregion

        #region Sound Manager

        public void LoadSounds()
        {
            try
            {
                //just in case user deleted it
                Directory.CreateDirectory(_soundManagerFilesDataPath);

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "ZIP archive|*.zip",
                    InitialDirectory = _soundManagerFilesDataPath
                };
                bool? result = dialog.ShowDialog(PlayniteApi.Dialogs.GetCurrentAppWindow());
                if (result == true)
                {
                    CloseAudioFiles();
                    string targetPath = dialog.FileName;
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundFilesDataPath);
                    // Have to extract each file one at a time to enabled overwrites
                    using (ZipArchive archive = ZipFile.OpenRead(targetPath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            // If it's a directory, it doesn't have a "Name".
                            if (!String.IsNullOrEmpty(entry.Name))
                            {
                                string entryDestination = Path.GetFullPath(Path.Combine(_soundFilesDataPath, entry.Name));
                                entry.ExtractToFile(entryDestination, true);
                            }
                        }
                    }
                    PlayniteApi.Dialogs.ShowMessage($"{Resource.ManagerLoadConfirm} {Path.GetFileNameWithoutExtension(targetPath)}");
                }
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        public void SaveSounds()
        {
            Window windowExtension = PlayniteApi.Dialogs.CreateWindow(
                new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true
                });

            windowExtension.ShowInTaskbar = false;
            windowExtension.ResizeMode = ResizeMode.NoResize;
            windowExtension.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            windowExtension.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            TextBox saveNameBox = new TextBox
            {
                Margin = new Thickness(5, 5, 10, 5),
                Width = 200
            };
            stackPanel.Children.Add(saveNameBox);

            Button saveNameButton = new Button
            {
                Margin = new Thickness(0, 5, 5, 5)
            };
            saveNameButton.Content = Resource.ManagerSave;
            saveNameButton.IsEnabled = false;
            saveNameButton.IsDefault = true;
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
                    string soundPackName = saveNameBox.Text;
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundFilesDataPath);
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundManagerFilesDataPath);
                    ZipFile.CreateFromDirectory(_soundFilesDataPath, Path.Combine(_soundManagerFilesDataPath, soundPackName + ".zip"));
                    PlayniteApi.Dialogs.ShowMessage($"{Resource.ManagerSaveConfirm} {soundPackName}");
                    windowExtension.Close();
                }
                catch (Exception E)
                {
                    HandleException(E);
                }
            };

            windowExtension.Content = stackPanel;
            windowExtension.SizeToContent = SizeToContent.WidthAndHeight;
            // Workaround for WPF bug which causes black sections to be displayed in the window
            windowExtension.ContentRendered += (s, e) => windowExtension.InvalidateMeasure();
            windowExtension.Loaded += (s, e) => saveNameBox.Focus();
            windowExtension.ShowDialog();
        }

        public void RemoveSounds()
        {
            try
            {
                //just in case user deleted it
                Directory.CreateDirectory(_soundManagerFilesDataPath);

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "ZIP archive|*.zip",
                    InitialDirectory = _soundManagerFilesDataPath
                };
                bool? result = dialog.ShowDialog(PlayniteApi.Dialogs.GetCurrentAppWindow());
                if (result == true)
                {
                    string targetPath = dialog.FileName;
                    File.Delete(targetPath);
                    PlayniteApi.Dialogs.ShowMessage($"{Resource.ManagerDeleteConfirm} {Path.GetFileNameWithoutExtension(targetPath)}");
                }
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        public void ImportSounds()
        {
            List<string> targetPaths = PlayniteApi.Dialogs.SelectFiles("ZIP archive|*.zip");

            if (targetPaths.HasNonEmptyItems())
            {
                try
                {
                    //just in case user deleted it
                    Directory.CreateDirectory(_soundManagerFilesDataPath);
                    foreach (string targetPath in targetPaths)
                    {
                        //just in case user selects a file from the soundmanager location
                        if (!Path.GetDirectoryName(targetPath).Equals(_soundManagerFilesDataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(targetPath, Path.Combine(_soundManagerFilesDataPath, Path.GetFileName(targetPath)), true);
                        }
                    }
                }
                catch (Exception E)
                {
                    HandleException(E);
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
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        #endregion

        #endregion

        public void HandleException(Exception e)
        {
            Logger.Error(e, new StackFrame(1).GetMethod().Name);
            PlayniteApi.Dialogs.ShowErrorMessage(e.Message, App.AppName);
        }

        private bool GetBoolFromYesNoDialog(string caption)
        {
            var selection = PlayniteApi.Dialogs.ShowMessage(
                caption, Resource.DialogCaptionSelectOption, MessageBoxButton.YesNo);

            return selection == MessageBoxResult.Yes;
        }

        #endregion

        #region File Management

        private void InitialCopyAudioFiles()
        {
            try
            {
                string soundFilesInstallPath = Path.Combine(PluginFolder, SoundDirectory.Sound);

                if (Directory.Exists(soundFilesInstallPath) && !Directory.Exists(_soundFilesDataPath))
                {
                    CloseAudioFiles();

                    Directory.CreateDirectory(_soundFilesDataPath);
                    string[] files = Directory.GetFiles(soundFilesInstallPath);
                    foreach (string file in files)
                    {
                        string destPath = Path.Combine(_soundFilesDataPath, Path.GetFileName(file));
                        File.Copy(file, destPath, true);
                    }
                }
            }
            catch (Exception E)
            {
                HandleException(E);
            }
        }

        private void SelectMusicFilename(int forceFileNumber = 0)
        {
            if (SingleGame())
            {
                var musicFileName = GetSelectedGameFileName(forceFileNumber, false);

                ReplaceMusicFile(musicFileName);
            }
            else
            {
                ShowMessage(Resource.MsgSelectSingleGame);
            }
        }

        private void ReplaceMusicFile(string musicFileName)
        {
            CloseMusic();
            string NewMusicFileName = PlayniteApi.Dialogs.SelectFile("MP3 File|*.mp3");
            File.Copy(NewMusicFileName, musicFileName, true);
            MusicNeedsReload = true;
            ReplayMusic();
        }

        #endregion

        #region Download

        private void PromptUserToDownload()
        {
            var albumSelect = GetBoolFromYesNoDialog(Resource.DialogMessageAlbumSelect);
            var songSelect = GetBoolFromYesNoDialog(Resource.DialogMessageSongSelect);
            var overwriteSelect = GetBoolFromYesNoDialog(Resource.DialogMessageOverwriteSelect);

            CloseMusic();

            CreateDownloadDialogue(PlayniteApi.MainView.SelectedGames, albumSelect, songSelect, overwriteSelect);

            ShowMessage(Resource.DialogMessageDone);

            MusicNeedsReload = true;
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

            try
            {
                PlayniteApi.Dialogs.ActivateGlobalProgress(
                    a => { StartDownload(a, games, progressTitle, albumSelect, songSelect, overwriteSelect); },
                    progressOptions);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        private void StartDownload(
            GlobalProgressActionArgs args,
            IEnumerable<Game> games,
            string progressTitle,
            bool albumSelect,
            bool songSelect,
            bool overwrite)
        {
            args.ProgressMaxValue = games.Count();
            foreach (var game in games)
            {
                if (args.CancelToken.IsCancellationRequested)
                {
                    break;
                }

                args.Text = $"{progressTitle}\n\n{args.CurrentProgressValue++}/{args.ProgressMaxValue}\n{game.Name}";

                var platform = GetPlatformName(game.Platforms);
                var MusicFileName =  GetMusicFileName(game.Name, platform, 0, false);

                bool fileCreated = false;
                var fileExists = File.Exists(MusicFileName);

                if (overwrite || !fileExists)
                {
                    var downloadSucceeded = DownloadSongFromGame(game.Name, MusicFileName, songSelect, albumSelect);
                    fileCreated = !fileExists && downloadSucceeded;
                }

                UpdateMissingTag(game, fileCreated);
            }
        }

        private bool DownloadSongFromGame(string gameName, string filePath, bool songSelect, bool albumSelect)
        {
            Logger.Info($"Starting album search for game '{gameName}'");

            var strippedGameName = StringManipulation.StripStrings(gameName);

            var regexGameName = songSelect && albumSelect
                ? string.Empty
                : StringManipulation.ReplaceStrings(strippedGameName);

            GenericItemOption album;
            if (albumSelect)
            {
                album = PromptForAlbum(strippedGameName);
            }
            else
            {
                var albums = DownloadManager.GetAlbumsForGame(strippedGameName);
                if (!albums.Any())
                {
                    Logger.Info($"Did not find any albums for game '{gameName}'");
                }

                album = DownloadManager.BestAlbumPick(albums, strippedGameName, regexGameName);
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

            if (!DownloadManager.DownloadSong(songToPartialUrl, filePath))
            {
                Logger.Info($"Failed to download song '{songToPartialUrl.Name} for album '{album.Name}' of game '{gameName}' from url '{songToPartialUrl.Description}'");
                return false;
            }

            Logger.Info($"Found file for song '{songToPartialUrl.Name}' in album '{album.Name}' of game '{gameName}'");
            return true;
        }

        public void DeleteMusicFileName(int fileNumber)
        {
            if (SingleGame())
            {
                var (gameName, platformName) = GetSelectedGameInfo();

                var musicFileName = GetMusicFileName(gameName, platformName, fileNumber, false);

                CloseMusic();
                File.Delete(musicFileName);
                Thread.Sleep(250);
                //need to force getting new music filename
                //if we were playing music 1 we delete music 2
                //the music type data would remain the same
                //and it would not load another music and start playing it again
                //because we closed the music above
                MusicFilenameNeedsReload = true;
                MusicNeedsReload = true;

                if (ShouldPlayMusic())
                {
                    var musicFile = MusicTypeToFilePath(gameName, platformName, -1);
                    PlayMusicFromPath(musicFile);
                }
            }
            else
            {
                ShowMessage(Resource.MsgSelectSingleGame);
            }
        }

        #endregion

        #region Helpers

        //TODO: Explore creating custom game class with game, platform, and some of the below methods built in
        private string GetSelectedGameFileName(int forceFileNumber = -1, bool setPrevMusicFileName2 = true)
        {
            var (game, platform) = GetSelectedGameInfo();
            return GetMusicFileName(game, platform, forceFileNumber, setPrevMusicFileName2);
        }

        private string GetCurrentMusicFileName(int forceFileNumber = -1)
        {
            var (game, platform) = GetSelectedGameInfo();
            return MusicTypeToFilePath(game, platform, forceFileNumber);
        }

        private (string, string) GetSelectedGameInfo() => GetGameInfoFromGames(PlayniteApi.MainView.SelectedGames);

        private (string, string) GetGameInfoFromGames(IEnumerable<Game> games)
        {
            var game = games.FirstOrDefault();
            var platform = GetPlatformName(game.Platforms);

            return (game.Name, platform);
        }

        private string MusicTypeToFilePath(string fileName, string platform, int fileNumber = -1)
        {
            switch (Settings.MusicType)
            {
                case MusicType.Default:
                    fileName = SoundFile.DefaultMusicName;
                    platform = string.Empty;
                    break;
                case MusicType.Platform:
                    fileName = SoundFile.DefaultMusicName;
                    break;
            }

            return GetMusicFileName(fileName, platform, fileNumber);
        }

        private bool MusicFileNameExists(int forceFileNumber)
            => SingleGame() && File.Exists(GetSelectedGameFileName(forceFileNumber, false));

        private string GetMusicFileName(string gameName, string platform, int forceFileNumber = -1, bool setPrevMusicFileName2 = true)
        {
            try
            {
                string musicDir = Path.Combine(GetPluginUserDataPath(), SoundDirectory.Music, platform);
                Directory.CreateDirectory(musicDir);
                string invalidChars = new string(Path.GetInvalidFileNameChars());
                Regex r = new Regex(string.Format("[{0}]", Regex.Escape(invalidChars)));
                string sanitizedGameName = r.Replace(gameName, string.Empty);

                string filename = _prevMusicFileName2;
                if (forceFileNumber == -1)
                {
                    if (MusicFilenameNeedsReload || Settings.RandomizeOnEverySelect || (_prevMusicGameData != (platform + sanitizedGameName).ToLower()))
                    {
                        MusicFilenameNeedsReload = false;

                        var musicFileRegex = new Regex(sanitizedGameName + @"(\.[12345])?\.mp3");
                        List<string> filePaths = Directory.GetFiles(musicDir).Where(s => musicFileRegex.IsMatch(s)).ToList();
                        if (filePaths.Any())
                        {
                            var rand = new Random();
                            filename = Path.GetFileName(filePaths[rand.Next(filePaths.Count)]);
                        }
                    }

                    _prevMusicGameData = (platform + sanitizedGameName).ToLower();
                }
                else
                {
                    filename = forceFileNumber > 0 
                        ? $"{sanitizedGameName}.{forceFileNumber}.mp3"  
                        : sanitizedGameName + ".mp3";
                }

                if (setPrevMusicFileName2)
                {
                    _prevMusicFileName2 = filename;
                }
                return Path.Combine(musicDir, filename);
            }
            catch (Exception E)
            {
                HandleException(E);
                return string.Empty;
            }
        }

        private void UpdateMissingTag(Game game, bool fileCreated)
        {
            if (Settings.TagMissingEntries)
            {
                var missingTagString = Resource.MissingTag;
                var missingTag = PlayniteApi.Database.Tags.Add(missingTagString);

                if (fileCreated && RemoveTagFromGame(game, missingTag))
                {
                    Logger.Info($"Removed tag from '{game.Name}'");
                }
                else if (AddTagToGame(game, missingTag))
                {
                    Logger.Info($"Added tag to '{game.Name}'");
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
            else if (!game.TagIds.Contains(tag.Id))
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

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    stream.Close();
                }
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
            var DesktopMode = IsDesktop();

            var playOnFullScreen = !DesktopMode && state == AudioState.Fullscreen;
            var playOnBoth = state == AudioState.Always;
            var playOnDesktop = DesktopMode && state == AudioState.Desktop;

            return !_gameRunning && (playOnFullScreen || playOnBoth || playOnDesktop);
        }

        private string GetPlatformName(IEnumerable<Platform> platforms)
            => platforms?.FirstOrDefault()?.Name ?? SoundDirectory.NoPlatform;

        private void ShowMessage(string resource) => PlayniteApi.Dialogs.ShowMessage(resource, App.AppName);

        private bool IsDesktop() => PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop;

        private bool SingleGame() => PlayniteApi.MainView.SelectedGames.Count() == 1;

        #endregion
    }
}