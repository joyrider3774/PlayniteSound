using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlayniteSounds.Common
{
    internal class StringUtilities
    {
        private static readonly string[] StringsToRemove = { "-", ":"};
        private static readonly IDictionary<string, string> StringsToReplace = new Dictionary<string, string> { { " & ", @" (&|and) " } };
        private static readonly IDictionary<string, string> ReplaceExtraWhitespace = new Dictionary<string, string> { { "  ", " " } };
        private static readonly string InvalidCharacters = new string(Path.GetInvalidFileNameChars());
        private static readonly Regex InvalidCharsRegex = new Regex($"[{Regex.Escape(InvalidCharacters)}]");

        public static string StripStrings(string stringToStrip, string[] stringsToRemove = null)
        {
            stringsToRemove = stringsToRemove ?? StringsToRemove;
            stringToStrip = stringsToRemove.Aggregate(stringToStrip, (current, str) => current.Replace(str, ""));
            return ReplaceStrings(stringToStrip, ReplaceExtraWhitespace);
        }

        public static string ReplaceStrings(string stringToSub, IDictionary<string, string> stringsToReplace = null)
        {
            stringsToReplace = stringsToReplace ?? StringsToReplace;
            return stringsToReplace.Aggregate(stringToSub, (current, stringToReplace) 
                => current.Replace(stringToReplace.Key, stringToReplace.Value));
        }

        public static string SanitizeGameName(string gameName) => InvalidCharsRegex.Replace(gameName, string.Empty);
    }
}
