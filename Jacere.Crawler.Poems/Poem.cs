using System.Data;

namespace Jacere.Crawler.Poems
{
    internal class Poem
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public string Poet { get; set; }

        public void Save(IDbConnection connection)
        {
            
        }
    }
}
