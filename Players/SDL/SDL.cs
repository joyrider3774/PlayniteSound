using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayniteSounds.Players.SDL
{
    public static class SDL2
    {
        private const string nativeLibName = "SDL2";

        public static IntPtr StringToUtf8(string str)
        {
            if (string.IsNullOrEmpty(str))
                return IntPtr.Zero;

            // Convert string to UTF-8 bytes with null terminator
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(str + "\0");

            // Allocate unmanaged memory
            IntPtr ptr = Marshal.AllocHGlobal(utf8Bytes.Length);

            // Copy bytes to unmanaged memory
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);

            return ptr;
        }
        public static string Utf8ToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;

            // Calculate string length by finding null terminator
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
                length++;

            // Copy to managed byte array
            byte[] buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);

            // Decode UTF8
            return Encoding.UTF8.GetString(buffer);
        }


        [DllImport(nativeLibName, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr INTERNAL_SDL_GetError();
        public static string SDL_GetError()
        {
            return Utf8ToString(INTERNAL_SDL_GetError());
        }

        [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_Init(uint flags);
        public const uint SDL_INIT_AUDIO = 0x00000010;

        public const ushort AUDIO_S16MSB = 0x9010;
        public const ushort AUDIO_S16LSB = 0x8010;
        public static readonly ushort MIX_DEFAULT_FORMAT =
            BitConverter.IsLittleEndian ? AUDIO_S16LSB : AUDIO_S16MSB;

    }
}