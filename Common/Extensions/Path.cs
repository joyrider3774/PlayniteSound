using System.IO;
namespace PlayniteSounds.Common.Extensions
{
    public static class PathExt
    {
        public static string GetFullPath(this string path)
        {
            if (path.StartsWith(".") || !path.Contains(":"))
            {
                return Path.GetFullPath(PlayniteSounds.playniteAPI.Paths.ApplicationPath + "\\" + path);
            }
            if (path.Contains("\\."))
            {
                return Path.GetFullPath(path);
            }
            return path;
        }

    }
}