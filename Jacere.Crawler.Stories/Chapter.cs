using System;
using System.Collections.Generic;

namespace Jacere.Crawler.Stories
{
    internal class Chapter
    {
        public string Title { get; set; }
        public string Note { get; set; }
        public List<string> Components { get; set; }
        public DateTime? Published { get; set; }
        public DateTime? Updated { get; set; }
    }
}
