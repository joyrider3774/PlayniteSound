using System.Collections.Generic;

namespace PlayniteSounds.Common
{
    internal class StringManipulation
    {
        private static readonly string[] StringsToRemove = { "-", ":"};
        private static readonly IDictionary<string, string> StringsToReplace = new Dictionary<string, string> { { " & ", @" (&|and) " } };
        private static readonly IDictionary<string, string> ReplaceExtraWhitespace = new Dictionary<string, string> { { "  ", " " } };


        public static string StripStrings(string stringToStrip, string[] stringsToRemove = null)
        {
            stringsToRemove = stringsToRemove ?? StringsToRemove;
            foreach (var str in stringsToRemove)
            {
                stringToStrip = stringToStrip.Replace(str, "");
            }
            return ReplaceStrings(stringToStrip, ReplaceExtraWhitespace);
        }

        public static string ReplaceStrings(string stringToSub, IDictionary<string, string> stringsToReplace = null)
        {
            stringsToReplace = stringsToReplace ?? StringsToReplace;
            foreach (var stringToReplace in stringsToReplace)
            {
                stringToSub = stringToSub.Replace(stringToReplace.Key, stringToReplace.Value);
            }
            return stringToSub;
        }
    }
}
