using System.Collections.Generic;

namespace Jacere.Crawler.Stories
{
    internal class Item
    {
        public string Slug { get; set; }
        public string Title { get; set; }
        public string Image { get; set; }
        public string Summary { get; set; }
        public string Author { get; set; }
        public string Artist { get; set; }
        public List<string> Tags { get; set; }
        public List<Chapter> Chapters { get; set; }
    }
}
