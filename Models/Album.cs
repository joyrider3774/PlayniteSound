using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlayniteSounds.Models
{
    public class Album : DownloadItem
    {
        public string Type { get; set; }
        public string Url { get; set; }
        public string Artist { get; set; }
        public uint? Count { get; set; }
        public string Year { get; set; }
        public IEnumerable<string> Platforms { get; set; }
        public IEnumerable<Song> Songs { get; set; }
        protected override IEnumerable<PropertyInfo> Properties => typeof(Album).GetProperties();

        public override string ToString()
        {
            var baseString = base.ToString();

            if (Platforms != null)
            {
                var platformsValue = string.Join(",",
                    Platforms.Where(platform => !string.IsNullOrWhiteSpace(platform)));

                if (!string.IsNullOrWhiteSpace(platformsValue))
                {
                    baseString += $", {nameof(Platforms)}: {platformsValue}";
                }
            }

            return baseString;
        }
    }
}
