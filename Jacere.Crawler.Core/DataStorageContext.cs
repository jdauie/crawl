using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Jacere.Crawler.Core
{
    public abstract class DataStorageContext : DataContext
    {
        private readonly string _databasePath;

        protected DataStorageContext(Uri baseUri, IDataStorageCommand command) : base(baseUri, command)
        {
            _databasePath = command.DatabasePath;
        }

        protected void OpenStorage()
        {
            if (!File.Exists(_databasePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_databasePath));
                SQLiteConnection.CreateFile(_databasePath);
            }
        }

        protected IDbConnection OpenStorageConnection()
        {
            var connection = new SQLiteConnection($@"Data Source={_databasePath};Version=3;foreign keys=true;");
            connection.Open();
            return connection;
        }
    }
}
