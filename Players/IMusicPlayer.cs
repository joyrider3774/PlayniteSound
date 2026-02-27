using System;
using System.Windows.Media;

namespace PlayniteSounds.Players
{
    public interface IMusicPlayer
    {
        event EventHandler MediaEnded;
        event EventHandler<ExceptionEventArgs> MediaFailed;
        double Volume { get; set; }
        bool IsLoaded { get; }
        bool IsActive { get; }
        TimeSpan? CurrentTime { get; }

        string Source { get; }

        void Destroy();
        void Stop();

        void PreLoad(string filePath, TimeSpan startFrom = default);
        void Play(TimeSpan startFrom = default);
        void Load(string filePath);

        void Close();

        void Pause();
        void Resume();
    }
}
