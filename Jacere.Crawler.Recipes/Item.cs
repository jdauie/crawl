using System.Collections.Generic;
using System.Diagnostics;

namespace Jacere.Crawler.Recipes
{
    [DebuggerDisplay("Id: {Id}, Title: {Title}")]
    internal class Item
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Cook { get; set; }
        public string ImageName { get; set; }

        public List<string> Ingredients { get; set; }
        public List<string> Directions { get; set; }
        public string Calories { get; set; }
        public string PrepTime { get; set; }
        public string TotalTime { get; set; }
    }
}
