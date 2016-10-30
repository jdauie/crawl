using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Stories
{
    internal class StoriesContext : DataStorageContext, IDisposable
    {
        private const string BaseUri = @"http://storybird.com/";

        private readonly IDbConnection _connection;

        public StoriesContext(StoriesCommand command) : base(new Uri(BaseUri), command)
        {
            OpenStorage();
            
            _connection = OpenStorageConnection();
            
            var tableCreated = _connection.QuerySingle<bool>(@"
                select count(*) from sqlite_master where type = 'table' and name = 'image'
            ");

            if (tableCreated)
            {
                return;
            }

            _connection.Execute(@"
                create table image (
                    url nvarchar(1000) unique not null,
                    data blob
                )
            ");

            _connection.Execute(@"
                create table story (
                    slug nvarchar(100) unique not null,
                    added datetime not null default current_timestamp,
                    title nvarchar(1000),
                    image nvarchar(1),
                    summary nvarchar(1),
                    author nvarchar(1),
                    artist nvarchar(1),
                    retrieved datetime,
                    foreign key (image) references image(url)
                )
            ");

            _connection.Execute(@"
                create table tag (
                    story nvarchar(100) unique not null,
                    name nvarchar(100),
                    foreign key (story) references story(slug)
                )
            ");

            _connection.Execute(@"
                create table chapter (
                    id integer primary key,
                    number int,
                    title nvarchar(1),
                    note nvarchar(1),
                    published datetime,
                    updated datetime,
                    foreign key (story) references story(slug)
                )
            ");

            _connection.Execute(@"
                create table component (
                    chapter integer not null,
                    html nvarchar(1000000),
                    foreign key (chapter) references chapter(id)
                )
            ");
        }

        public async Task Crawl()
        {
            
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
