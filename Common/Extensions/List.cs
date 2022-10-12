using System.Collections.Generic;
using System.Linq;

namespace PlayniteSounds.Common.Extensions
{
    public static class List
    {
        public static TItem Pop<TItem>(this IList<TItem> list)
        {
            var item = list.LastOrDefault();

            if (list.Count > 0)
            {
                list.RemoveAt(list.Count - 1);
            }

            return item;
        }
    }
}
