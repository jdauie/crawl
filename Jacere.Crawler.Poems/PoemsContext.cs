using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    internal class PoemsContext : DataStorageContext, IDisposable
    {
        private const string BaseUri = @"http://www.poemhunter.com/";

        private readonly IDbConnection _connection;
        private readonly bool _skipPoetSearch;
        private readonly bool _skipPoemSearch;

        public PoemsContext(CrawlPoemsCommand command) : base(new Uri(BaseUri), command)
        {
            OpenStorage();
            
            _skipPoetSearch = command.SkipPoetSearch;
            _skipPoemSearch = command.SkipPoemSearch;

            _connection = OpenStorageConnection();
            
            var tableCreated = _connection.QuerySingle<bool>(@"
                select count(*) from sqlite_master where type = 'table' and name = 'poet'
            ");

            if (tableCreated)
            {
                return;
            }

            _connection.Execute(@"
                create table poet (
                    slug nvarchar(100) unique not null,
                    added datetime not null default current_timestamp,
                    name nvarchar(1000),
                    retrieved datetime
                )
            ");

            _connection.Execute(@"
                create table poem (
                    slug nvarchar(100) unique not null,
                    added datetime not null default current_timestamp,
                    poet nvarchar(100) not null,
                    title nvarchar(1000),
                    html nvarchar(1000000),
                    retrieved datetime,
                    foreign key (poet) references poet(slug)
                )
            ");
        }

        public static async Task Export(string sourcePath, string outputPath)
        {
            CreateStorage(outputPath);
            
            using (var output = OpenStorageConnection(":memory:"))
            {
                await output.ExecuteAsync(@"
                    attach database @sourcePath as source;

                    attach database @outputPath as output;

                    create table output.poet (
                        slug nvarchar(100) primary key not null,
                        name nvarchar(1000) not null
                    );

                    insert into output.poet (slug, name)
                    select
                        distinct(st.slug),
                        st.name
                    from source.poem as sm
                    inner join source.poet as st on sm.poet = st.slug
                    where sm.title is not null
                    and st.name is not null;

                    create table output.poem (
                        slug nvarchar(100) not null,
                        title nvarchar(1000) not null,
                        html nvarchar(1000000) not null,
                        poet nvarchar(100) not null,
                        foreign key (poet) references poet(slug)
                    );

                    insert into output.poem (slug, title, html, poet)
                    select
                        sm.slug,
                        sm.title,
                        sm.html,
                        st.slug
                    from source.poem as sm
                    inner join source.poet as st on sm.poet = st.slug
                    where sm.title is not null
                    and st.name is not null;

                    vacuum output;
                ", new
                {
                    sourcePath,
                    outputPath,
                });
            }
        }

        public async Task Crawl()
        {
            if (!_skipPoetSearch)
            {
                await CrawlPoetSlugs();
            }

            if (!_skipPoemSearch)
            {
                await CrawlPoemSlugs();
            }

            await CrawlPoemDetails();
        }

        private async Task CrawlPoetSlugs()
        {
            await ForeachHtmlPageWithNext(
                progressTitle: "poets",
                pageUrlFormat: @"/p/t/l.asp?a=0&l=All&cinsiyet=&Populer_mi=&Classicmi=&Dogum_Tarihi_yil=&Dogum_Yeri=&p={0}",
                lastPageSelector: @"//div[contains(@class, 'pagination')]//li[last()]/a",
                itemsSelector: @"//ol[contains(@class, 'poets-grid')]//a[@class='name']",
                nextSelector: @"//div[contains(@class, 'pagination')]//li[@class='next']",
                filterItems: items => items.Select(x => x.Trim('/')),
                handleItem: slug =>
                {
                    _connection.Execute(@"
                        insert or ignore into poet (slug) values (@slug)
                    ", new {
                        slug,
                    });
                }
            );
        }

        private async Task CrawlPoemSlugs()
        {
            var poets = _connection.Query<Poet>(@"
                select * from poet
            ").ToList();
            
            foreach (var poet in poets.WithProgress("poems"))
            {
                await ForeachHtmlPageWithNext(
                    pageUrlFormat: $@"/{poet.Slug}/poems/page-{{0}}",
                    itemsSelector: @"//table[@class='poems']//td[@class='title']/a",
                    nextSelector: @"//div[contains(@class, 'pagination')]//li[@class='next']",
                    filterItems: items => items
                        .Select(x => x.Trim('/').Split('/'))
                        .Where(x => x.Length == 2)
                        .Select(x => x[1]),
                    handleItem: slug =>
                    {
                        _connection.Execute(@"
                            insert or ignore into poem (slug, poet) values (@slug, @poet)
                        ", new {
                            slug,
                            poet = poet.Slug,
                        });
                    }
                );
            }
        }

        private async Task CrawlPoemDetails()
        {
            var poems = _connection.Query<Poem>(@"
                select slug, poet
                from poem
                where retrieved is null
            ").ToList();
            
            foreach (var poem in poems.WithProgress("details"))
            {
                var root = (await GetHtmlDocument($@"/poem/{poem.Slug}")).DocumentNode;

                var author = root.Select(@"//meta[@itemprop='author']")
                    .SingleOrDefault()?.GetAttribute("content");

                if (author != null)
                {
                    var title = root.Select(@"//h1[@itemprop='name'][starts-with(@class, 'title')]")
                        .Single().GetValue().SubstringUntil(" - Poem by ");
                    var html = root.SelectSingleNode(@"//div[@class='KonaBody']//p").InnerHtml;

                    _connection.Execute(@"
                        update poem set
                            poet = @poet,
                            title = @title,
                            html = @html,
                            retrieved = current_timestamp
                        where slug = @slug
                    ", new {
                        poem.Poet,
                        title,
                        html,
                        poem.Slug,
                    });

                        _connection.Execute(@"
                            update poet set
                                name = @author,
                                retrieved = current_timestamp
                            where slug = @poet
                        ", new {
                            author,
                            poem.Poet,
                        });
                    }
                    else
                    {
                        _connection.Execute(@"
                            update poem set
                                retrieved = current_timestamp
                            where slug = @slug
                        ", new {
                            poem.Slug,
                        });
                    }

                await RandomDelay();
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
