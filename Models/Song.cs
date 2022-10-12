using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlayniteSounds.Models
{
    internal class Song
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Description { get; set; }
        public string SizeInMb { get; set; }
        public TimeSpan? Length { get; set; }
        public Source Source { get; set; }

        private static readonly IEnumerable<PropertyInfo> Properties = typeof(Song).GetProperties();
        private static readonly IEnumerable<string> IgnoredFields = new[]
        {
            // Ignored Types
            "Name",
            "Id"
        };

        public override string ToString()
        {
            var albumStrings =
                from property in Properties
                let propertyValue = property.GetValue(this)
                where propertyValue != null && !IgnoredFields.ContainsString(property.Name) &&
                      (!(propertyValue is string propertyString) || !string.IsNullOrWhiteSpace(propertyString))
                select $"{property.Name}: {propertyValue}";
            
            return string.Join(", ", albumStrings);
        }
    }
}
