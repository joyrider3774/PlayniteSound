using PlayniteSounds.Models;

namespace PlayniteSounds.Players
{
    public class MusicPlayer
    {
        static public IMusicPlayer Create(PlayniteSoundsSettings settings)
        {
            if (settings.UseWMPLegacyApp && WMPMusicPlayer.WMPIsInstalled())
            {
                return new WMPMusicPlayer();
            }
            else
            {
                return new SDL2MusicPlayer();
            }
        }
    }
}
