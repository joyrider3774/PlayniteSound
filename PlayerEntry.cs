using System.Windows.Media;
using System.Media;

namespace PlayniteSounds
{
    class PlayerEntry
    {
        public bool FileExists { get; set; }
        public MediaPlayer MediaPlayer { get; set; }
        public SoundPlayer SoundPlayer { get; set; }
    }
}
