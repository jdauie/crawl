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
                CreateStorage(_databasePath);
            }
        }

        protected static void CreateStorage(string databasePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            SQLiteConnection.CreateFile(databasePath);
        }

        protected IDbConnection OpenStorageConnection()
        {
            return OpenStorageConnection(_databasePath);
        }

        protected static IDbConnection OpenStorageConnection(string databasePath, bool readOnly = false)
        {
            var connection = new SQLiteConnection($@"Data Source={databasePath};Version=3;foreign keys=true;read only={Convert.ToString(readOnly)}");
            connection.Open();
            return connection;
        }
    }
}
