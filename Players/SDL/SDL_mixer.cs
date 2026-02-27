using System;
using System.Runtime.InteropServices;


namespace PlayniteSounds.Players.SDL
{
    public static class SDL2Mixer
    {
        private const string NativeLibName = "SDL2_mixer";

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_CloseAudio();

        [DllImport(NativeLibName, EntryPoint = "Mix_LoadMUS", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr INTERNAL_Mix_LoadMUS(IntPtr file);
		public static IntPtr Mix_LoadMUS(string file)
        {
            IntPtr fileUtf8 = SDL2.StringToUtf8(file);
            IntPtr handle = INTERNAL_Mix_LoadMUS(fileUtf8);
			Marshal.FreeHGlobal(fileUtf8);
			return handle;
		}

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_FreeMusic(IntPtr music);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_PlayMusic(IntPtr music, int loops);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HaltMusic();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_PauseMusic();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_ResumeMusic();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_SetMusicPosition(double position);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern double Mix_GetMusicPosition(IntPtr music);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_VolumeMusic(int volume);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_PlayingMusic();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_PausedMusic();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HookMusicFinished(IntPtr callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MusicFinishedCallback();

        public static string GetMixError()
        {
            return SDL2.SDL_GetError();
        }
    }
}