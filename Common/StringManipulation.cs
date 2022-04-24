using System.Collections.Generic;

namespace PlayniteSounds.Common
{
    internal class StringManipulation
    {
        private static readonly string[] _stringsToRemove = { "-", ":"};
        private static readonly IDictionary<string, string> _stringsToReplace = new Dictionary<string, string> { { " & ", @" (&|and) " } };
        private static readonly IDictionary<string, string> _replaceExtraWhitespace = new Dictionary<string, string> { { "  ", " " } };


        public static string StripStrings(string stringToStrip, string[] stringsToRemove = null)
        {
            stringsToRemove = stringsToRemove ?? _stringsToRemove;
            foreach (var str in stringsToRemove)
            {
                stringToStrip = stringToStrip.Replace(str, "");
            }
            return ReplaceStrings(stringToStrip, _replaceExtraWhitespace);
        }

        public static string ReplaceStrings(string stringToSub, IDictionary<string, string> stringsToReplace = null)
        {
            stringsToReplace = stringsToReplace ?? _stringsToReplace;
            foreach (var stringToReplace in stringsToReplace)
            {
                stringToSub = stringToSub.Replace(stringToReplace.Key, stringToReplace.Value);
            }
            return stringToSub;
        }
    }
}
