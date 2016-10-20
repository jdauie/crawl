using System.Data;

namespace Jacere.Crawler.Core
{
    static class DbConnectionExtensions
    {
        public static void Save(this IDbConnection connection, IActiveRecord record)
        {
            record.Save(connection);
        }
    }
}
