using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Media;
using Playnite.SDK.Events;
using System.Windows.Media.Animation;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.IO.Compression;
using System.Threading;
using System.Net.Http;
using HtmlAgilityPack;
using PlayniteSounds.Downloaders;
using PlayniteSounds.Common;

namespace PlayniteSounds
{
    public class PlayniteSounds : GenericPlugin
    {
        private static readonly IResourceProvider resources = new ResourceProvider();
        private static readonly ILogger logger = LogManager.GetLogger();
        public bool MusicNeedsReload { get; set; } = false;
        public bool MusicFilenameNeedsReload { get; set; } = false;
        private PlayniteSoundsSettingsViewModel Settings { get; set; }
        private string prevmusicfilename = "";   //used to prevent same file being restarted 
        private string prevmusicfilename2 = "";  //used with the new don't randomize on every select option in case of multiple files
        private string prevmusicgamedata = "";  //used with the new don't randomize on every select option in case of multiple files

        private MediaPlayer musicplayer; 
        private readonly MediaTimeline timeLine;

        private static readonly HttpClient httpclient = new HttpClient();
        private static readonly HtmlWeb web = new HtmlWeb();

        private static readonly IDownloadManager downloadmanager = new DownloadManager(httpclient, web);

        public static string pluginFolder;

        public override Guid Id { get; } = Guid.Parse("9c960604-b8bc-4407-a4e4-e291c6097c7d");

        private Dictionary<string, PlayerEntry> players = new Dictionary<string, PlayerEntry>();
        private bool closeaudiofilesnextplay = false;
        private bool gamerunning = false;
        private bool firstselectsound = true; 

        protected virtual bool IsFileLocked(FileInfo file)
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

        public PlayniteSounds(IPlayniteAPI api) : base(api)
        {
            try
            {
                Settings = new PlayniteSoundsSettingsViewModel(this);
                Properties = new GenericPluginProperties
                {
                    HasSettings = true
                };

                pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                Localization.SetPluginLanguage(pluginFolder, api.ApplicationSettings.Language);
                musicplayer = new MediaPlayer();
                musicplayer.MediaEnded += MediaEnded;
                timeLine = new MediaTimeline();
                //{
                //    RepeatBehavior = RepeatBehavior.Forever                    
                //};
            }
            catch (Exception E)
            {
                logger.Error(E, "PlayniteSounds");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
            PlayFileName("GameInstalled.wav");
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            // Add code to be executed when game is started running.
            if (Settings.Settings.StopMusic == 1)
            {
                PauseMusic();
                gamerunning = true;
            }
            PlayFileName("GameStarted.wav", true);
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
            if (Settings.Settings.StopMusic == 0)
            {
                PauseMusic();
                gamerunning = true;
            }
            PlayFileName("GameStarting.wav");
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            gamerunning = false;
            // Add code to be executed when game is preparing to be started.
            PlayFileName("GameStopped.wav");
            ResumeMusic();
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
            PlayFileName("GameUninstalled.wav");
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            PlayFileName("ApplicationStarted.wav");
            SystemEvents.PowerModeChanged += OnPowerMode_Changed;
            Application.Current.Deactivated += onApplicationDeactivate;
            Application.Current.Activated += onApplicationActivate;
            Application.Current.MainWindow.StateChanged += onWindowStateChanged;
        }

        private void onWindowStateChanged(object sender, EventArgs e)
        {
            if (Settings.Settings.PauseOnDeactivate)
            {
                switch (Application.Current?.MainWindow?.WindowState)
                {
                    case WindowState.Maximized:
                        ResumeMusic();
                        break;
                    case WindowState.Minimized:
                        PauseMusic();
                        break;
                    case WindowState.Normal:
                        ResumeMusic();
                        break;
                }
            }
        }

        public bool canPlayMusic()
        {
            bool DesktopMode = PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop;
            return (!gamerunning) && ((DesktopMode && ((Settings.Settings.MusicWhere == 1) || (Settings.Settings.MusicWhere == 3))) ||
                (!DesktopMode && ((Settings.Settings.MusicWhere == 2) || (Settings.Settings.MusicWhere == 3))));
        }

        public void onApplicationDeactivate(object sender, EventArgs e)
        {
            if (Settings.Settings.PauseOnDeactivate)
            {
                PauseMusic();
            }
        }

        public void onApplicationActivate(object sender, EventArgs e)
        {
            if (Settings.Settings.PauseOnDeactivate)
            {
                ResumeMusic();
            }
        }

        //fix sounds not playing after system resume
        public void OnPowerMode_Changed(object sender, PowerModeChangedEventArgs e)
        {
            try
            { 
                if (e.Mode == PowerModes.Resume)
                {
                    closeaudiofilesnextplay = true;
                    MusicNeedsReload = true;
                    //Restart music:
                    ReplayMusic();
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "OnPowerMode_Changed");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void ResetMusicVolume()
        {
            if (musicplayer != null)
            {
                musicplayer.Volume = (double)Settings.Settings.MusicVolume / 100;
            }
        }

        public void ReplayMusic()
        {
            if (gamerunning)
            {
                return;
            }

            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {
                    Platform platform = game?.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        PlayMusic(game.Name, platform == null ? "No Platform" : platform.Name, -1);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            PlayMusic("_music_", platform == null ? "No Platform" : platform.Name, -1);
                        }
                        else
                        {
                            PlayMusic("_music_", "", -1);
                        }
                    }
                }
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Add code to be executed when Playnite is shutting down.
            SystemEvents.PowerModeChanged -= OnPowerMode_Changed;
            Application.Current.Deactivated -= onApplicationDeactivate;
            Application.Current.Activated -= onApplicationActivate;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged -= onWindowStateChanged;
            }
            PlayFileName("ApplicationStopped.wav", true);
            CloseAudioFiles();
            CloseMusic();
            musicplayer.MediaEnded -= MediaEnded;
            musicplayer = null;
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            PlayFileName("LibraryUpdated.wav");

            if (Settings.Settings.AutoDownload)
            {
                var progressTitle = Constants.AppName + " - " + ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageLibUpdateAutomaticDownload");
                var progressOptions = new GlobalProgressOptions(progressTitle, true);
                progressOptions.IsIndeterminate = false;
                PlayniteApi.Dialogs.ActivateGlobalProgress((a) =>
                {
                    var games = PlayniteApi.Database.Games.Where(x => x.Added != null && x.Added > Settings.Settings.LastAutoLibUpdateAssetsDownload);
                    DownloadMusicForGames(a, games, false, false, false, true, progressTitle);
                }, progressOptions);
            }

            Settings.Settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
            SavePluginSettings(Settings.Settings);
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            if (firstselectsound)
            {
                firstselectsound = false;
                if (!Settings.Settings.SkipFirstSelectSound)
                {
                    PlayFileName("GameSelected.wav");
                }
            }
            else
            {
                PlayFileName("GameSelected.wav");
            }

            if (args.NewValue.Count == 1) 
            {
                foreach(Game game in args.NewValue)
                {
                    Platform platform = game?.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        PlayMusic(game.Name, platform == null ? "No Platform" : platform.Name, -1);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            PlayMusic("_music_", platform == null ? "No Platform" : platform.Name, -1);
                        }
                        else
                        {
                            PlayMusic("_music_", "", -1);
                        }
                    }
                }
            }
            else if (args.NewValue.Count == 0 && Settings.Settings.PlayBackgroundWhenNoneSelected)
            {
                PlayMusic("_music_", "");
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PlayniteSoundsSettingsView(this);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            List<GameMenuItem> MainMenuItems = new List<GameMenuItem>();
            MainMenuItems.Add(new GameMenuItem {
                MenuSection = "Playnite Sounds",
                Icon = Path.Combine(pluginFolder, "icon.png"),
                Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsShowMusicFilename"),
                Action = (MainMenuItem) =>
                {
                    ShowMusicFilename();
                }
            });

            MainMenuItems.Add(new GameMenuItem
            {
                MenuSection = "Playnite Sounds",
                Icon = Path.Combine(pluginFolder, "icon.png"),
                Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsDownloadMusicForGames"),
                Action = (GameMenuItem) =>
                {
                    DownloadMusicForSelectedGames();
                }
            });

            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                MainMenuItems.Add(new GameMenuItem
                {
                    MenuSection = "Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 1 " + (GetMusicFilenameExists(0) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(0);
                    }
                });

                MainMenuItems.Add(new GameMenuItem
                {
                    MenuSection = "Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 2 " + (GetMusicFilenameExists(1) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(1);
                    }
                });

                MainMenuItems.Add(new GameMenuItem
                {
                    MenuSection = "Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 3 " + (GetMusicFilenameExists(2) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(2);
                    }
                });

                MainMenuItems.Add(new GameMenuItem
                {
                    MenuSection = "Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 4 " + (GetMusicFilenameExists(3) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(3);
                    }
                });

                MainMenuItems.Add(new GameMenuItem
                {
                    MenuSection = "Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 5 " + (GetMusicFilenameExists(4) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(4);
                    }
                });

                if (GetMusicFilenameExists(0))
                {
                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 1",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(0);
                        }
                    });

                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 1",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(0);
                        }
                    });
                }

                if (GetMusicFilenameExists(1))
                {
                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 2",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(1);
                        }
                    });

                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 2",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(1);
                        }
                    });
                }

                if (GetMusicFilenameExists(2))
                {
                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 3",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(2);
                        }
                    });

                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 3",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(2);
                        }
                    });
                }

                if (GetMusicFilenameExists(3))
                {
                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 4",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(3);
                        }
                    });

                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 4",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(3);
                        }
                    });
                }

                if (GetMusicFilenameExists(4))
                {
                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 5",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(4);
                        }
                    });

                    MainMenuItems.Add(new GameMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 5",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(4);
                        }
                    });
                }
            }
        
            return MainMenuItems;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            List<MainMenuItem> MainMenuItems = new List<MainMenuItem>();
            MainMenuItems.Add(new MainMenuItem
            {
                MenuSection = "@Playnite Sounds",
                Icon = Path.Combine(pluginFolder, "icon.png"),
                Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsShowMusicFilename"),
                Action = (MainMenuItem) =>
                {
                    ShowMusicFilename();
                }
            });
            MainMenuItems.Add(new MainMenuItem
            {
                MenuSection = "@Playnite Sounds",
                Icon = Path.Combine(pluginFolder, "icon.png"),
                Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsDownloadMusicForGames"),
                Action = (MainMenuItem) =>
                {
                    DownloadMusicForSelectedGames();
                }
            });
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 1 " + (GetMusicFilenameExists(0) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(0);
                    }
                });

                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 2 " + (GetMusicFilenameExists(1) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(1);
                    }
                });

                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 3 " + (GetMusicFilenameExists(2) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(2);
                    }
                });

                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 4 " + (GetMusicFilenameExists(3) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(3);
                    }
                });

                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopySelectMusicFile") + " 5 " + (GetMusicFilenameExists(4) ? "[*]" : "[ ]"),
                    Action = (MainMenuItem) =>
                    {
                        SelectMusicFilename(4);
                    }
                });

                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsOpenMusicFolder"),
                    Action = (MainMenuItem) =>
                    {
                        OpenMusicFolder();
                    }
                });
                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsOpenSoundsFolder"),
                    Action = (MainMenuItem) =>
                    {
                        OpenSoundsFolder();
                    }
                });
                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsReloadAudioFiles"),
                    Action = (MainMenuItem) =>
                    {
                        ReloadAudioFiles();
                    }
                });
                MainMenuItems.Add(new MainMenuItem
                {
                    MenuSection = "@Playnite Sounds",
                    Icon = Path.Combine(pluginFolder, "icon.png"),
                    Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsHelp"),
                    Action = (MainMenuItem) =>
                    {
                        HelpMenu();
                    }
                });

                if (GetMusicFilenameExists(0))
                {
                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 1",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(0);
                        }
                    });

                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 1",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(0);
                        }
                    });
                }

                if (GetMusicFilenameExists(1))
                {
                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 2",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(1);
                        }
                    });

                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 2",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(1);
                        }
                    });
                }

                if (GetMusicFilenameExists(2))
                {
                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 3",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(2);
                        }
                    });

                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 3",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(2);
                        }
                    });
                }

                if (GetMusicFilenameExists(3))
                {
                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 4",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(3);
                        }
                    });

                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 4",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(3);
                        }
                    });
                }

                if (GetMusicFilenameExists(4))
                {
                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyPlayMusicFile") + " 5",
                        Action = (MainMenuItem) =>
                        {
                            PlayMusicFile(4);
                        }
                    });

                    MainMenuItems.Add(new MainMenuItem
                    {
                        MenuSection = "@Playnite Sounds",
                        Icon = Path.Combine(pluginFolder, "icon.png"),
                        Description = resources.GetString("LOC_PLAYNITESOUNDS_ActionsCopyDeleteMusicFile") + " 5",
                        Action = (MainMenuItem) =>
                        {
                            DeleteMusicFilename(4);
                        }
                    });
                }
            }
            return MainMenuItems;
        }

        public void HelpMenu()
        {
            PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp1") + "\n\n" +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp2") + "\n\n" +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp3") + " " +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp4") + " " +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp5") + "\n\n" +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp6") + "\n\n" +
                "D_ApplicationStarted.wav - F_ApplicationStarted.wav\n" +
                "D_ApplicationStopped.wav - F_ApplicationStopped.wav\n" +
                "D_GameInstalled.wav - F_GameInstalled.wav\n" +
                "D_GameSelected.wav - F_GameSelected.wav\n" +
                "D_GameStarted.wav - F_GameStarted.wav\n" +
                "D_GameStarting.wav - F_GameStarting.wav\n" +
                "D_GameStopped.wav - F_GameStopped.wav\n" +
                "D_GameUninstalled.wav - F_GameUninstalled.wav\n" +
                "D_LibraryUpdated.wav - F_LibraryUpdated.wav\n\n" +
                resources.GetString("LOC_PLAYNITESOUNDS_MsgHelp7"), Constants.AppName);
        }
        
        public bool GetMusicFilenameExists(int fileNr)
        {

            string MusicFileName = "";
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {

                    Platform platform = game.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        MusicFileName = GetMusicFilename(game.Name, platform == null ? "No Platform" : platform.Name, fileNr, true);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            MusicFileName = GetMusicFilename("_music_", platform == null ? "No Platform" : platform.Name, fileNr, true);
                        }
                        else
                        {
                            MusicFileName = GetMusicFilename("_music_", "", fileNr, true);
                        }
                    }
                }
            }
            return File.Exists(MusicFileName);
        }

        public string GetMusicFilename(string gamename, string platform, int forceFileNr = -1, bool dontsetprevmusicfilename2 = false)
        {
            try
            { 
                
                string musicdir = Path.Combine(GetPluginUserDataPath(), "Music Files", platform);
                Directory.CreateDirectory(musicdir);
                string invalidChars = new string(Path.GetInvalidFileNameChars());
                Regex r = new Regex(string.Format("[{0}]", Regex.Escape(invalidChars)));
                string sanitizedgamename = r.Replace(gamename, "");
                string filename = "-";
                if (forceFileNr == -1)
                {
                    if (MusicFilenameNeedsReload || Settings.Settings.RandomizeOnEverySelect || (prevmusicgamedata != (platform + sanitizedgamename).ToLower()))
                    {
                        MusicFilenameNeedsReload = false;
                        List<string> dirs = Directory.GetFiles(musicdir, sanitizedgamename + ".?.mp3").ToList();
                        if (File.Exists(Path.Combine(musicdir, sanitizedgamename + ".mp3")))
                        {
                            dirs.Add(Path.Combine(musicdir, sanitizedgamename + ".mp3"));
                        }
                        if (dirs.Count > 0)
                        {
                            var rand = new Random();
                            filename = Path.GetFileName(dirs[rand.Next(dirs.Count)]);
                        }
                    }
                    else
                    {
                        filename = prevmusicfilename2;
                    }
                    prevmusicgamedata = (platform + sanitizedgamename).ToLower();
                }

                if (forceFileNr > -1)
                {
                    filename = sanitizedgamename + ".mp3";
                    if (forceFileNr > 0)
                    {
                        filename = sanitizedgamename + "." + forceFileNr.ToString() + ".mp3";
                    }
                }

                if (!dontsetprevmusicfilename2)
                {
                    prevmusicfilename2 = filename;
                }
                return Path.Combine(musicdir, filename);
            }
            catch (Exception E)
            {
                logger.Error(E, "GetMusicFilename");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
                return "";
            }
        }

        public void PlayMusicFile(int fileNr)
        {
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {
                    Platform platform = game.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        PlayMusic(game.Name, platform == null ? "No Platform" : platform.Name, fileNr);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            PlayMusic("_music_", platform == null ? "No Platform" : platform.Name, fileNr);
                        }
                        else
                        {
                            PlayMusic("_music_", "", fileNr);
                        }
                    }
                }
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgSelectSingleGame"), Constants.AppName);
            }
        }

        public void DeleteMusicFilename(int fileNr)
        {
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {
                    string MusicFileName;
                    Platform platform = game?.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        MusicFileName = GetMusicFilename(game.Name, platform == null ? "No Platform" : platform.Name, fileNr, true);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            MusicFileName = GetMusicFilename("_music_", platform == null ? "No Platform" : platform.Name, fileNr, true);
                        }
                        else
                        {
                            MusicFileName = GetMusicFilename("_music_", "", fileNr, true);
                        }
                    }

                    CloseMusic();
                    File.Delete(MusicFileName);
                    Thread.Sleep(250);
                    //need to force getting new music filename
                    //if we were playing music 1 we delete music 2
                    //the music type data would remain the same
                    //and it would not load another music and start playing it again
                    //because we closed the music above
                    MusicFilenameNeedsReload = true;
                    MusicNeedsReload = true;

                    if (Settings.Settings.MusicType == 2)
                    {
                        PlayMusic(game.Name, platform == null ? "No Platform" : platform.Name, -1);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            PlayMusic("_music_", platform == null ? "No Platform" : platform.Name, -1);
                        }
                        else
                        {
                            PlayMusic("_music_", "", -1);
                        }
                    }
                }
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgSelectSingleGame"), Constants.AppName);
            }
        }

        public void SelectMusicFilename(int fileNr)
        {
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {
                    string MusicFileName;
                    Platform platform = game.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        MusicFileName = GetMusicFilename(game.Name, platform == null ? "No Platform" : platform.Name, fileNr, true);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            MusicFileName = GetMusicFilename("_music_", platform == null ? "No Platform" : platform.Name, fileNr, true);
                        }
                        else
                        {
                            MusicFileName = GetMusicFilename("_music_", "", fileNr, true);
                        }
                    }
                    
                    CloseMusic();
                    string NewMusicFileName = PlayniteApi.Dialogs.SelectFile("MP3 File|*.mp3");
                    if (!string.IsNullOrEmpty(NewMusicFileName))
                    {
                        File.Copy(NewMusicFileName, MusicFileName, true);
                    }
                    MusicNeedsReload = true;

                    if (Settings.Settings.MusicType == 2)
                    {
                        PlayMusic(game.Name, platform == null ? "No Platform" : platform.Name, fileNr);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            PlayMusic("_music_", platform == null ? "No Platform" : platform.Name, fileNr);
                        }
                        else
                        {
                            PlayMusic("_music_", "", fileNr);
                        }
                    }
                }
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgSelectSingleGame"), Constants.AppName);
            }
        }

        private void DownloadMusicForSelectedGames()
        {
            var albumSelect = PromptForAlbumSelect();
            var songSelect = PromptForSongSelect();
            var overwriteSelect = PromptForOverwriteSelect();


            CloseMusic();

            var progressTitle = ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageDownloadingFiles");
            var progressOptions = new GlobalProgressOptions(progressTitle, true)
            {
                IsIndeterminate = false
            };
            PlayniteApi.Dialogs.ActivateGlobalProgress((a) =>
            {
                DownloadMusicForGames(a, PlayniteApi.MainView.SelectedGames, albumSelect, songSelect, overwriteSelect, false, progressTitle);
            }, progressOptions);

            MusicNeedsReload = true;
            MusicFilenameNeedsReload = true;
            ReplayMusic();
        }

        public void DownloadMusicForGames(GlobalProgressActionArgs args, IEnumerable<Game> games, bool albumSelect, bool songSelect, bool overwrite, bool isLibraryUpdate, string progressTitle)
        {
            args.ProgressMaxValue = games.Count();
            try
            {
                foreach (var game in games)
                {
                    if (args.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    args.CurrentProgressValue++;
                    
                    var gameName = StringManipulation.StripStrings(game.Name);
                    
                    args.Text =  $"{progressTitle}\n\n{args.CurrentProgressValue}/{args.ProgressMaxValue}\n{gameName}";

                    var platform = GetPlatformName(game.Platforms);
                    var MusicFileName = GetMusicFilename(gameName, platform, 0, true);

                    var fileExists = FileExists(MusicFileName);
                    if (overwrite || !fileExists.Value)
                    {
                        DownloadSongFromGame(gameName, MusicFileName, songSelect, albumSelect);
                    }
                    
                    fileExists = FileExists(MusicFileName);
                    
                    UpdateMissingTag(game, gameName, fileExists);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"An error occured while updating music: {ex.Message}");
            }

            if (!isLibraryUpdate && !args.CancelToken.IsCancellationRequested)
            {
                PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageDone"), "Playnite Sounds");
            }
        }

        Lazy<bool> FileExists(string filePath)
            => new Lazy<bool>(() => File.Exists(filePath));

        private void DownloadSongFromGame(string gameName, string filePath, bool songSelect, bool albumSelect)
        {
            logger.Info($"Starting album search for game '{gameName}'");

            GenericItemOption album;
            var regexGameName = songSelect && albumSelect ? string.Empty: StringManipulation.ReplaceStrings(gameName);
            if (albumSelect)
            {
                album = PromptForAlbum(gameName);
            }
            else
            {
                var albums = downloadmanager.GetAlbumsForGame(gameName);
                if (!albums.Any())
                {
                    logger.Info($"Did not find any albums for game '{gameName}'");
                }

                album = downloadmanager.BestAlbumPick(albums, gameName, regexGameName);
            }

            if (album == null)
            {
                return;
            }

            logger.Info($"Selected album '{album.Name}' for game '{gameName}'");

            var songs = downloadmanager.GetSongsFromAlbum(album).ToList();
            if (!songs.Any())
            {
                logger.Info($"Did not find any songs for album '{album.Name}' of game '{gameName}'");
                return;
            }

            logger.Info($"Found songs for album '{album.Name}' of game '{gameName}'");

            var songToPartialUrl = songSelect
                ? PromptForSong(songs, album.Name)
                : downloadmanager.BestSongPick(songs, regexGameName);
            if (songToPartialUrl == null)
            {
                return;
            }

            if (!downloadmanager.DownloadSong(songToPartialUrl, filePath))
            {
                logger.Info($"Failed to download song '{songToPartialUrl.Name} for album '{album.Name}' of game '{gameName}' from url '{songToPartialUrl.Description}'");
                return;
            }

            logger.Info($"Found file for song '{songToPartialUrl.Name}' in album '{album.Name}' of game '{gameName}'");
        }

        private void UpdateMissingTag(Game game, string gameName, Lazy<bool> fileExists)
        {
            if (Settings.Settings.TagMissingEntries)
            {
                var missingTageString = ResourceProvider.GetString("LOC_PLAYNITESOUNDS_MissingTag");
                var missingTag = PlayniteApi.Database.Tags.Add(missingTageString);
                if (fileExists.Value)
                {
                    if (RemoveTagFromGame(game, missingTag))
                    {
                        logger.Info($"Removed tag from '{gameName}'");
                    }
                }
                else
                {
                    if (AddTagToGame(game, missingTag))
                    {
                        logger.Info($"Added tag to '{gameName}'");
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

        private GenericItemOption PromptForAlbum(string gameName)
        {
            var caption = string.Format(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageCaptionAlbum"), gameName);
            return PlayniteApi.Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(), a => downloadmanager.GetAlbumsForGame(a).ToList(), gameName, caption);
        }

        private GenericItemOption PromptForSong(List<GenericItemOption> songsToPartialUrls, string albumName)
        {
            var caption = string.Format(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageCaptionSong"), albumName);
            return PlayniteApi.Dialogs.ChooseItemWithSearch(
                songsToPartialUrls, (a) => songsToPartialUrls.OrderByDescending(s => s.Name.StartsWith(a)).ToList(), "", caption: caption);
        }

        private bool PromptForAlbumSelect()
            => GetBoolFromYesNoDialog(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageAlbumSelect"));

        private bool PromptForSongSelect()
            => GetBoolFromYesNoDialog(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageSongSelect"));

        private bool PromptForOverwriteSelect()
            => GetBoolFromYesNoDialog(ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogMessageOverwriteSelect"));


        private bool GetBoolFromYesNoDialog(string caption)
        {
            var selection = PlayniteApi.Dialogs.ShowMessage(caption,
                ResourceProvider.GetString("LOC_PLAYNITESOUNDS_DialogCaptionSelectOption"),
                MessageBoxButton.YesNo);

            return selection == MessageBoxResult.Yes;
        }

        public void ShowMusicFilename()
        {
            if (PlayniteApi.MainView.SelectedGames.Count() == 1)
            {
                foreach (Game game in PlayniteApi.MainView.SelectedGames)
                {
                    string MusicFileName1, MusicFileName2;
                    Platform platform = game?.Platforms.FirstOrDefault(o => o != null);
                    if (Settings.Settings.MusicType == 2)
                    {
                        MusicFileName1 = GetMusicFilename(game.Name, platform == null ? "No Platform" : platform.Name, 0, true);
                        MusicFileName2 = GetMusicFilename(game.Name, platform == null ? "No Platform" : platform.Name, 1, true);
                    }
                    else
                    {
                        if (Settings.Settings.MusicType == 1)
                        {
                            MusicFileName1 = GetMusicFilename("_music_", platform == null ? "No Platform" : platform.Name, 0, true);
                            MusicFileName2 = GetMusicFilename("_music_", platform == null ? "No Platform" : platform.Name, 1, true);
                        }
                        else
                        {
                            MusicFileName1 = GetMusicFilename("_music_", "", 0, true);
                            MusicFileName2 = GetMusicFilename("_music_", "", 1, true);
                        }
                    }
                    PlayniteApi.Dialogs.ShowMessage(MusicFileName1 + "\n\n" + MusicFileName2.Replace(".1.mp3",".<1-9>.mp3"), Constants.AppName);
                }
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgSelectSingleGame"), Constants.AppName);
            }
        }

        public void PlayFileName(string FileName, bool UseSoundPlayer = false)
        {
            try
            { 
                InitialCopyAudioFiles();

                if (closeaudiofilesnextplay)
                {
                    CloseAudioFiles();
                    closeaudiofilesnextplay = false;
                }

                bool DesktopMode = PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop;
                bool DoPlay = (DesktopMode && ((Settings.Settings.SoundWhere == 1) || (Settings.Settings.SoundWhere == 3))) ||
                    (!DesktopMode && ((Settings.Settings.SoundWhere == 2) || (Settings.Settings.SoundWhere == 3)));

                if (DoPlay)
                {
                    PlayerEntry Entry;
                    if (players.ContainsKey(FileName))
                    {
                        Entry = players[FileName];
                    }
                    else
                    {
                        string Prefix = PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop ? "D_" : "F_";

                        string FullFileName = Path.Combine(GetPluginUserDataPath(), "Sound Files", Prefix + FileName);

                        //MediaPlayer can play multiple sounds together from mulitple instances SoundPlayer can not
                        if (UseSoundPlayer)
                        {
                            Entry = new PlayerEntry(File.Exists(FullFileName), null, new SoundPlayer(), 0);
                        }
                        else
                        {
                            Entry = new PlayerEntry(File.Exists(FullFileName), new MediaPlayer(), null, 1);
                        }

                        if (Entry.FileExists)
                        {
                            if (Entry.TypePlayer == 1)
                            {
                                Entry.MediaPlayer.Open(new Uri(FullFileName));
                            }
                            else
                            {
                                Entry.SoundPlayer.SoundLocation = FullFileName;
                                Entry.SoundPlayer.Load();
                            }
                        }
                        players[FileName] = Entry;
                    }

                    if (Entry.FileExists)
                    {
                        if (Entry.TypePlayer == 1)
                        {
                            Entry.MediaPlayer.Stop();
                            Entry.MediaPlayer.Play();
                        }
                        else
                        {
                            Entry.SoundPlayer.Stop();
                            Entry.SoundPlayer.PlaySync();
                        }
                    }
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "PlayFileName");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }


        public void CloseAudioFiles()
        {
            try
            {
                foreach (string keyname in players.Keys)
                {
                    PlayerEntry Entry = players[keyname];
                    if (Entry.FileExists)
                    {
                        if (Entry.TypePlayer == 1)
                        {
                            string filename = "";
                            if (Entry.MediaPlayer.Source != null)
                            {
                                filename = Entry.MediaPlayer.Source.LocalPath;
                            }
                            Entry.MediaPlayer.Stop();
                            Entry.MediaPlayer.Close();
                            Entry.MediaPlayer = null;
                            if (File.Exists(filename))
                            {
                                int count = 0;
                                while (IsFileLocked(new FileInfo(filename)))
                                {
                                    Thread.Sleep(5);
                                    count += 5;
                                    if (count > 500)
                                        break;
                                }
                            }
                        }
                        else
                        {
                            Entry.SoundPlayer.Stop();
                            Entry.SoundPlayer = null;
                        }
                    }
                }
                players.Clear();
            }
            catch (Exception E)
            {
                logger.Error(E, "CloseAudioFiles");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void ReloadAudioFiles()
        {
            CloseAudioFiles();
            PlayniteApi.Dialogs.ShowMessage(resources.GetString("LOC_PLAYNITESOUNDS_MsgAudioFilesReloaded"), Constants.AppName);
        }

        public void InitialCopyAudioFiles()
        {
            try
            { 
                string SoundFilesInstallPath = Path.Combine(pluginFolder, "Sound Files");
                string SoundFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Files");

                if (!Directory.Exists(SoundFilesDataPath))
                {
                    if (Directory.Exists(SoundFilesInstallPath))
                    {
                        CloseAudioFiles();

                        Directory.CreateDirectory(SoundFilesDataPath);
                        string[] files = Directory.GetFiles(SoundFilesInstallPath);
                        foreach (string file in files)
                        {                        
                            string DestPath = Path.Combine(SoundFilesDataPath, Path.GetFileName(file));
                            File.Copy(file, DestPath, true);
                        }
                    }
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "InitialCopyAudioFiles");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void ResumeMusic()
        {
            try
            {
                if (gamerunning)
                {
                    return;
                }

                if ((musicplayer != null) && (musicplayer.Clock != null))
                {
                    if (musicplayer.Clock.CurrentState == ClockState.Active)
                    {
                        //Is Paused
                        if (musicplayer.Clock.CurrentGlobalSpeed == 0.0)
                        {
                            musicplayer.Clock.Controller.Resume();
                        }
                    }
                    else
                    {
                        if (musicplayer.Clock.CurrentState == ClockState.Stopped)
                        {
                            musicplayer.Clock.Controller.Begin();
                        }
                    }
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "ResumeMusic");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void PauseMusic()
        {
            try
            { 
                if (gamerunning)
                {
                    return;
                }

                if ((musicplayer != null) && (musicplayer.Clock != null))
                {
                    musicplayer.Clock.Controller.Pause();
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "PauseMusic");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void  CloseMusic()
        {
            try
            {
                if ((musicplayer != null) && (musicplayer.Clock != null))
                {
                    musicplayer.Clock.Controller.Stop();
                    musicplayer.Clock = null;
                    musicplayer.Close();
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "CloseMusic");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void PlayMusic(string gamename, string platform, int ForceFileNr)
        {
            try
            { 
                if (canPlayMusic())
                {
                    string MusicFileName = GetMusicFilename(gamename, platform, ForceFileNr);
                    if (MusicNeedsReload || (MusicFileName != prevmusicfilename))
                    {
                        CloseMusic();
                        MusicNeedsReload = false;
                        prevmusicfilename = "";
                        if (File.Exists(MusicFileName))
                        {
                            prevmusicfilename = MusicFileName;
                            timeLine.Source = new Uri(MusicFileName);
                            if (musicplayer != null)
                            {
                                musicplayer.Volume = (double)Settings.Settings.MusicVolume / 100;
                                musicplayer.Clock = timeLine.CreateClock(true) as MediaClock;
                                musicplayer.Clock.Controller.Begin();
                            }
                        }
                    }
                }
                else 
                { 
                    CloseMusic();
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "PlayMusic");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        private void MediaEnded(object sender, EventArgs e)
        {            
            if (Settings.Settings.RandomizeOnMusicEnd)
            {
                //will play random song on case of multiple (could be same song)
                CloseMusic();
                MusicNeedsReload = true;
                MusicFilenameNeedsReload = true;
                ReplayMusic();
            }
            else
            {
                if ((musicplayer != null) && (musicplayer.Clock != null))
                {
                    musicplayer.Clock.Controller.Stop();
                    musicplayer.Clock.Controller.Begin();
                }
            }
        }

        public void OpenSoundsFolder()
        {
            try
            { 
                //need to release them otherwise explorer can't overwrite files even though you can delete them
                CloseAudioFiles();
                string SoundFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Files");
                // just in case user deleted it
                Directory.CreateDirectory(SoundFilesDataPath);
                Process.Start(SoundFilesDataPath);
            }
            catch (Exception E)
            {
                logger.Error(E, "OpenSoundsFolder");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void OpenMusicFolder()
        {
            try
            {
                //need to release them otherwise explorer can't overwrite files even though you can delete them
                CloseMusic();
                string SoundFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Music Files");
                //just in case user deleted it
                Directory.CreateDirectory(SoundFilesDataPath);
                Process.Start(SoundFilesDataPath);
                MusicNeedsReload = true;
            }
            catch (Exception E)
            {
                logger.Error(E, "OpenMusicFolder");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void SaveSounds()
        {
            Window windowExtension = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
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
            saveNameButton.SetResourceReference(Button.ContentProperty, "LOC_PLAYNITESOUNDS_ManagerSave");
            saveNameButton.IsEnabled = false;
            saveNameButton.IsDefault = true;
            stackPanel.Children.Add(saveNameButton);

            saveNameBox.KeyUp += (object sender, System.Windows.Input.KeyEventArgs e) =>
            {
                // Only allow saving if filename is larger than 3 characters
                saveNameButton.IsEnabled = saveNameBox.Text.Trim().Length > 3;
            };

            saveNameButton.Click += (object sender, RoutedEventArgs e) =>
            {
                // Create ZIP file in sound manager folder
                try
                {
                    string soundPackName = saveNameBox.Text;
                    string SoundFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Files");
                    //just in case user deleted it
                    Directory.CreateDirectory(SoundFilesDataPath);
                    string SoundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Manager");
                    //just in case user deleted it
                    Directory.CreateDirectory(SoundManagerFilesDataPath);
                    ZipFile.CreateFromDirectory(SoundFilesDataPath, Path.Combine(SoundManagerFilesDataPath, soundPackName + ".zip"));
                    PlayniteApi.Dialogs.ShowMessage(Application.Current.FindResource("LOC_PLAYNITESOUNDS_ManagerSaveConfirm").ToString() + " " + soundPackName);
                    windowExtension.Close();
                }
                catch (Exception E)
                {
                    logger.Error(E, "SaveSounds");
                    PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
                }
            };

            windowExtension.Content = stackPanel;
            windowExtension.SizeToContent = SizeToContent.WidthAndHeight;
            // Workaround for WPF bug which causes black sections to be displayed in the window
            windowExtension.ContentRendered += (s, e) => windowExtension.InvalidateMeasure();
            windowExtension.Loaded += (s, e) => saveNameBox.Focus();
            windowExtension.ShowDialog();
        }

        public void LoadSounds()
        {
            try
            {
                string SoundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Manager");
                //just in case user deleted it
                Directory.CreateDirectory(SoundManagerFilesDataPath);

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "ZIP archive|*.zip",
                    InitialDirectory = SoundManagerFilesDataPath
                };
                bool? result = dialog.ShowDialog(PlayniteApi.Dialogs.GetCurrentAppWindow());
                if (result == true)
                {
                    CloseAudioFiles();
                    string targetPath = dialog.FileName;
                    string SoundFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Files");
                    //just in case user deleted it
                    Directory.CreateDirectory(SoundFilesDataPath);
                    // Have to extract each file one at a time to enabled overwrites
                    using (ZipArchive archive = ZipFile.OpenRead(targetPath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            // If it's a directory, it doesn't have a "Name".
                            if (!String.IsNullOrEmpty(entry.Name))
                            {
                                string entryDestination = Path.GetFullPath(Path.Combine(SoundFilesDataPath, entry.Name));
                                entry.ExtractToFile(entryDestination, true);
                            }
                        }
                    }
                    PlayniteApi.Dialogs.ShowMessage(Application.Current.FindResource("LOC_PLAYNITESOUNDS_ManagerLoadConfirm").ToString() + " " + Path.GetFileNameWithoutExtension(targetPath));
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "LoadSounds");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }


        }

        public void ImportSounds()
        {
            List<string> targetPaths = PlayniteApi.Dialogs.SelectFiles("ZIP archive|*.zip");

            if (targetPaths.HasNonEmptyItems())
            {
                try
                {
                    string SoundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Manager");
                    //just in case user deleted it
                    Directory.CreateDirectory(SoundManagerFilesDataPath);
                    foreach (string targetPath in targetPaths)
                    {
                        //just in case user selects a file from the soundmanager location
                        if (! Path.GetDirectoryName(targetPath).Equals(SoundManagerFilesDataPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(targetPath, Path.Combine(SoundManagerFilesDataPath, Path.GetFileName(targetPath)), true);
                        }
                    }
                }
                catch (Exception E)
                {
                    logger.Error(E, "ImportSounds");
                    PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
                }
            }
        }

        public void RemoveSounds()
        {
            try
            {
                string SoundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Manager");
                //just in case user deleted it
                Directory.CreateDirectory(SoundManagerFilesDataPath);

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = "ZIP archive|*.zip",
                    InitialDirectory = SoundManagerFilesDataPath
                };
                bool? result = dialog.ShowDialog(PlayniteApi.Dialogs.GetCurrentAppWindow());
                if (result == true)
                {
                    string targetPath = dialog.FileName;
                    File.Delete(targetPath);
                    PlayniteApi.Dialogs.ShowMessage(Application.Current.FindResource("LOC_PLAYNITESOUNDS_ManagerDeleteConfirm").ToString() + " " + Path.GetFileNameWithoutExtension(targetPath));
                }
            }
            catch (Exception E)
            {
                logger.Error(E, "RemoveSounds");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }

        public void OpenSoundManagerFolder()
        {
            try
            {
                string SoundManagerFilesDataPath = Path.Combine(GetPluginUserDataPath(), "Sound Manager");
                //just in case user deleted it
                Directory.CreateDirectory(SoundManagerFilesDataPath);
                Process.Start(SoundManagerFilesDataPath);
            }
            catch (Exception E)
            {
                logger.Error(E, "OpenSoundManagerFolder");
                PlayniteApi.Dialogs.ShowErrorMessage(E.Message, Constants.AppName);
            }
        }
        public string GetPlatformName(IEnumerable<Platform> platforms)
        {
            var platform = platforms?.FirstOrDefault(o => o != null);
            return platform == null ? "No Platform" : platform.Name;
        }
    }
}