using System.Data;

namespace Jacere.Crawler.Core
{
    public interface IActiveRecord
    {
        void Save(IDbConnection connection);
    }
}
