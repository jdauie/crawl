using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jacere.Crawler.Recipes
{
    internal class Card
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ItemType { get; set; }
        public string ImageUrl { get; set; }
        public dynamic Cook { get; set; }
        public dynamic Submitter { get; set; }

        public bool IsValid { get { return Id != 0 && ItemType != "Video"; } }
    }
}
