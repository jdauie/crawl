using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jacere.Crawler.Recipes
{
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
