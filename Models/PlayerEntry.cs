using System.Windows.Media;
using System.Media;
using PlayniteSounds.Players;

namespace PlayniteSounds.Models
{
    class PlayerEntry
    {
        public IMusicPlayer MusicPlayer { get; set; }
        public SoundPlayer SoundPlayer { get; set; }
    }
}
