using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Media;

namespace PlayniteSounds
{
    class PlayerEntry
    {
        public bool FileExists { get; set; }
        public MediaPlayer MediaPlayer { get; set; }
        public SoundPlayer SoundPlayer { get; set; }
        public int TypePlayer { get; set; }

        public PlayerEntry(bool aFileExists, MediaPlayer aMediaPlayer, SoundPlayer aSoundPlayer, int aTypePlayer)
        {
            FileExists = aFileExists;
            MediaPlayer = aMediaPlayer;
            SoundPlayer = aSoundPlayer;
            TypePlayer = aTypePlayer;
        }
    }
}
