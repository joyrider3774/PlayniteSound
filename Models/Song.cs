using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PlayniteSounds.Models
{
    internal class Song : DownloadItem
    {
        public string Description { get; set; }
        public string SizeInMb { get; set; }
        public TimeSpan? Length { get; set; }
        protected override IEnumerable<PropertyInfo> Properties => typeof(Song).GetProperties();
    }
}
