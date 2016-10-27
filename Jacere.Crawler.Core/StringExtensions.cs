using System;

namespace Jacere.Crawler.Core
{
    public static class StringExtensions
    {
        public static string SubstringUntil(this string value, string substring)
        {
            var index = value.IndexOf(substring, StringComparison.InvariantCulture);
            return index > -1 ? value.Substring(0, index) : value;
        }
    }
}
