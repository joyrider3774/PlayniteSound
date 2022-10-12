using Playnite.SDK;

namespace PlayniteSounds.Common.Constants
{
    public class SoundFile
    {
        public static IPlayniteInfoAPI ApplicationInfo { get; set; }


        public const string DefaultMusicName = "_music_.mp3";
        public const string LocalizationSource = "LocSource";
        public const string DefaultNormArgs = "-lrt 20 -c:a libmp3lame";

        public static string ApplicationStartedSound => CurrentPrefix + BaseApplicationStartedSound;
        public static string ApplicationStoppedSound => CurrentPrefix + BaseApplicationStoppedSound;
        public static string GameInstalledSound => CurrentPrefix + BaseGameInstalledSound;
        public static string GameSelectedSound => CurrentPrefix + BaseGameSelectedSound;
        public static string GameStartedSound => CurrentPrefix + BaseGameStartedSound;
        public static string GameStartingSound => CurrentPrefix + BaseGameStartingSound;
        public static string GameStoppedSound => CurrentPrefix + BaseGameStoppedSound;
        public static string GameUninstalledSound => CurrentPrefix + BaseGameUninstalledSound;
        public static string LibraryUpdatedSound => CurrentPrefix + BaseLibraryUpdatedSound;

        //TODO: Move bool logic to some common location
        public static string CurrentPrefix => ApplicationInfo.Mode == ApplicationMode.Desktop ? DesktopPrefix : FullScreenPrefix;
        public const string DesktopPrefix = "D_";
        public const string FullScreenPrefix = "F_";

        public const string BaseApplicationStartedSound = "ApplicationStarted.wav";
        public const string BaseApplicationStoppedSound = "ApplicationStopped.wav";
        public const string BaseGameInstalledSound = "GameInstalled.wav";
        public const string BaseGameSelectedSound = "GameSelected.wav";
        public const string BaseGameStartedSound = "GameStarted.wav";
        public const string BaseGameStartingSound = "GameStarting.wav";
        public const string BaseGameStoppedSound = "GameStopped.wav";
        public const string BaseGameUninstalledSound = "GameUninstalled.wav";
        public const string BaseLibraryUpdatedSound = "LibraryUpdated.wav";
    }
}
