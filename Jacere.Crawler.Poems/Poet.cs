using System.Data;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    internal class Poet : IActiveRecord
    {
        public string Slug { get; set; }
        public string Name { get; set; }

        public void Save(IDbConnection connection)
        {

        }
    }
}
