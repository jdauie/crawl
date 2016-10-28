using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Jacere.Crawler.Core;

namespace Jacere.Crawler.Poems
{
    internal class PoemsContext : DataStorageContext, IDisposable
    {
        private const string BaseUri = @"http://www.poemhunter.com/";

        private readonly IDbConnection _connection;
        private readonly int _delayInterval;
        private readonly bool _skipPoetSearch;
        private readonly bool _skipPoemSearch;

        public PoemsContext(CrawlPoemsCommand command) : base(new Uri(BaseUri), command)
        {
            OpenStorage();

            _delayInterval = command.DelayInterval;
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
                    familyfriendly integer,
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

                    create table output.poem (
                        slug nvarchar(100) unique not null,
                        title nvarchar(1000),
                        html nvarchar(1000000),
                        poet_slug nvarchar(100) not null,
                        poet_name nvarchar(1000) not null
                    );

                    insert into output.poem (slug, title, html, poet_slug, poet_name)
                    select
                        sm.slug,
                        sm.title,
                        sm.html,
                        st.slug,
                        st.name
                    from source.poem as sm
                    inner join source.poet as st on sm.poet = st.slug
                    where sm.retrieved is not null
                    and sm.title is not null;

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

        private async Task ReCrawlOnePoemForEachPoetToGetName()
        {
            
        }

        private async Task CrawlPoetSlugs()
        {
            using (var progress = new ConsoleProgress("poets"))
            {
                for (var i = 1;; i++)
                {
                    var nextPage = $"/p/t/l.asp?a=0&l=All&cinsiyet=&Populer_mi=&Classicmi=&Dogum_Tarihi_yil=&Dogum_Yeri=&p={i}";
                    var root = (await GetHtmlDocument(nextPage, Encoding.UTF8)).DocumentNode;

                    var lastPage = root.Select(@"//div[contains(@class, 'pagination')]//li[last()]/a")
                        .First().GetValueInt();

                    progress.SetTotal(lastPage);

                    var currentSlugs = root.Select(@"//ol[contains(@class, 'poets-grid')]//a[@class='name']")
                        .Select(x => x.GetAttribute("href").Trim('/'));

                    foreach (var slug in currentSlugs)
                    {
                        _connection.Execute(@"
                            insert or ignore into poet (slug) values (@slug)
                        ", new
                        {
                            slug,
                        });
                    }

                    progress.Increment();

                    if (!root.Has(@"//div[contains(@class, 'pagination')]//li[@class='next']"))
                    {
                        break;
                    }

                    await RandomDelay(TimeSpan.FromMilliseconds(_delayInterval));
                }
            }
        }

        private async Task CrawlPoemSlugs()
        {
            var poets = _connection.Query<Poet>(@"
                select * from poet
            ").ToList();

            using (var progress = new ConsoleProgress("poems", poets.Count))
            {
                foreach (var poet in poets)
                {
                    for (var i = 1;; i++)
                    {
                        var nextPage = $@"/{poet.Slug}/poems/page-{i}";
                        var root = (await GetHtmlDocument(nextPage, Encoding.UTF8)).DocumentNode;

                        var currentSlugs = root.Select(@"//table[@class='poems']//td[@class='title']/a")
                            .Select(x => x.GetAttribute("href").Trim('/').Split('/'))
                            .Where(x => x.Length == 2)
                            .Select(x => x[1]).ToList();

                        if (currentSlugs.Any())
                        {
                            foreach (var slug in currentSlugs)
                            {
                                _connection.Execute(@"
                                    insert or ignore into poem (slug, poet) values (@slug, @poet);
                                ", new
                                {
                                    slug,
                                    poet = poet.Slug,
                                });
                            }
                        }

                        if (!root.Has(@"//div[contains(@class, 'pagination')]//li[@class='next']"))
                        {
                            break;
                        }

                        await RandomDelay(TimeSpan.FromMilliseconds(_delayInterval));
                    }

                    progress.Increment();
                }
            }
        }

        private async Task CrawlPoemDetails()
        {
            var poems = _connection.Query<Poem>(@"
                select slug, poet
                from poem
                where retrieved is null
            ").ToList();

            using (var progress = new ConsoleProgress("details", poems.Count))
            {
                foreach (var poem in poems)
                {
                    var root = (await GetHtmlDocument($@"http://www.poemhunter.com/poem/{poem.Slug}", Encoding.UTF8)).DocumentNode;

                    var author = root.Select(@"//meta[@itemprop='author']")
                        .SingleOrDefault()?.GetAttribute("content");

                    if (author != null)
                    {
                        var title = root.Select(@"//h1[@itemprop='name'][starts-with(@class, 'title')]")
                            .Single().GetValue().Until(" - Poem by ");
                        var familyFriendly = root.SelectSingleNode(@"//meta[@itemprop='isFamilyFriendly']")
                            .GetAttribute("content") == "true";
                        var html = root.SelectSingleNode(@"//div[@class='KonaBody']//p").InnerHtml;

                        _connection.Execute(@"
                            update poem set
                                poet = @poet,
                                title = @title,
                                familyfriendly = @familyFriendly,
                                html = @html,
                                retrieved = current_timestamp
                            where slug = @slug
                        ", new {
                            poem.Poet,
                            title,
                            familyFriendly,
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

                    progress.Increment();

                    await RandomDelay(TimeSpan.FromMilliseconds(_delayInterval));
                }
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
