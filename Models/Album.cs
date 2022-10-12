using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlayniteSounds.Models
{
    internal class Album
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public string Artist { get; set; }
        public Source Source { get; set; }
        public uint? Count { get; set; }
        public uint? Year { get; set; }
        public IEnumerable<string> Platforms { get; set; }
        public IEnumerable<Song> Songs { get; set; }

        private static readonly IEnumerable<PropertyInfo> Properties = typeof(Album).GetProperties();
        private static readonly IEnumerable<string> IgnoredFields = new[]
        {
            // Ignored Types
            "Name",
            "Url",
            "Songs",
            // Needs Custom Handling
            "Id",
            "Platforms"
        };

        public override string ToString()
        {
            var albumStrings =
                from property in Properties
                let propertyValue = property.GetValue(this)
                where IsValidField(property, propertyValue)
                select $"{property.Name}: {propertyValue}";
            var albumStringsList = albumStrings.ToList();

            if (Platforms != null)
            {
                var platformsValue = string.Join(",", 
                    Platforms.Where(platform => !string.IsNullOrWhiteSpace(platform)));

                if (!string.IsNullOrWhiteSpace(platformsValue))
                {
                    albumStringsList.Add($"{nameof(Platforms)}: {platformsValue}");
                }
            }

            return string.Join(", ", albumStringsList);
        }

        private static bool IsValidField(PropertyInfo property, object propertyValue)
            => propertyValue != null 
            && !IgnoredFields.ContainsString(property.Name) 
            && !(propertyValue is string propertyString && string.IsNullOrWhiteSpace(propertyString));
    }
}
