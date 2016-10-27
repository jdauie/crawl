using System.Collections.Generic;

namespace Jacere.Crawler.Core
{
    public static class CollectionExtensions
    {
        public static IEnumerable<T> WithProgress<T>(this ICollection<T> value, string title, bool showRate = true)
        {
            using (var progress = new ConsoleProgress(title, value.Count))
            {
                foreach (var item in value)
                {
                    yield return item;

                    progress.Increment();
                }
            }
        }
    }
}
