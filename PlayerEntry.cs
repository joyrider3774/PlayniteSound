using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayniteSounds
{
    class PlayerEntry
    {
        public bool FileExists { get; set; }
        public object Player { get; set; }
        public int TypePlayer { get; set; }

        public PlayerEntry(bool aFileExists, object aPlayer, int aTypePlayer)
        {
            FileExists = aFileExists;
            Player = aPlayer;
            TypePlayer = aTypePlayer;
        }
    }
}
