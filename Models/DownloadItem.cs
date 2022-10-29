using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlayniteSounds.Models
{
    public abstract class DownloadItem
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string IconUrl { get; set; }
        public Source Source { get; set; }


        protected abstract IEnumerable<PropertyInfo> Properties { get; }

        protected static readonly IEnumerable<string> IgnoredFields = new[]
        {
            // Ignored Types
            "Name",
            "Url",
            "Songs",
            "IconUrl",
            // Needs Custom Handling
            "Id",
            "Platforms"
        };

        public override string ToString()
        {
            var strings =
                from property in Properties
                let propertyValue = property.GetValue(this)
                where IsValidField(property, propertyValue)
                select $"{property.Name}: {propertyValue}";

            return string.Join(", ", strings);
        }

        private static bool IsValidField(PropertyInfo property, object propertyValue)
            => propertyValue != null
            && !IgnoredFields.ContainsString(property.Name)
            && !(propertyValue is string propertyString && string.IsNullOrWhiteSpace(propertyString));
    }
}
