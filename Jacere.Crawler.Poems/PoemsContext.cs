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
        private const int DelayInterval = 500;

        private readonly IDbConnection _connection;
        
        public PoemsContext(IDataStorageCommand command) : base(new Uri(BaseUri), command)
        {
            OpenStorage();

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
                    name nvarchar(1000) unique,
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

        public async Task Crawl()
        {
            await CrawlPoetSlugs();
            await CrawlPoemSlugs();
            await CrawlPoemDetails();
        }

        private async Task CrawlPoetSlugs()
        {
            var basePagingUrl = "/p/t/l.asp";
            var nextPage = $"{basePagingUrl}?a=0&l=All&cinsiyet=&Populer_mi=&Classicmi=&Dogum_Tarihi_yil=&Dogum_Yeri=&p=1";

            using (var progress = new ConsoleProgress("poets"))
            {
                while (nextPage != null)
                {
                    var root = (await GetHtmlDocument(nextPage, Encoding.UTF8)).DocumentNode;

                    var lastPage = int.Parse(
                        root.SelectSingleNode(@"//div[contains(@class, 'pagination')]//li[last()]/a").InnerText);

                    progress.SetTotal(lastPage);

                    var currentSlugs = root.SelectNodes(@"//ol[contains(@class, 'poets-grid')]//a[@class='name']")
                        .Select(x => x.GetAttributeValue("href", null).Trim('/'));

                    foreach (var slug in currentSlugs)
                    {
                        _connection.Execute(@"
                            insert or ignore into poet (slug) values (@slug)
                        ", new
                        {
                            slug,
                        });
                    }

                    nextPage = root.SelectSingleNode(@"//div[contains(@class, 'pagination')]//li[@class='next']/a")
                        ?.GetAttributeValue("href", null);

                    if (nextPage != null)
                    {
                        nextPage = $"{basePagingUrl}{nextPage}";
                    }

                    progress.Increment();

                    await RandomDelay(TimeSpan.FromMilliseconds(DelayInterval));
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
                    var nextPage = $@"/{poet.Slug}/poems";

                    while (nextPage != null)
                    {
                        var root = (await GetHtmlDocument(nextPage, Encoding.UTF8)).DocumentNode;

                        var currentSlugs = root.SelectNodes(@"//table[@class='poems']//td[@class='title']/a")
                            ?.Select(x => x.GetAttributeValue("href", null).Trim('/').Split('/')[1]);

                        if (currentSlugs != null)
                        {
                            foreach (var slug in currentSlugs)
                            {
                                _connection.Execute(@"
                                    insert or ignore into poem (slug) values (@slug);
                                ", new
                                {
                                    slug,
                                });
                            }
                        }

                        nextPage = root.SelectSingleNode(@"//div[contains(@class, 'pagination')]//li[@class='next']/a")
                            ?.GetAttributeValue("href", null);

                        await RandomDelay(TimeSpan.FromMilliseconds(DelayInterval));
                    }

                    progress.Increment();
                }
            }
        }

        private async Task CrawlPoemDetails()
        {
            var poems = _connection.Query<string>(@"
                select slug
                from poem
            ").ToList();

            using (var progress = new ConsoleProgress("details", poems.Count))
            {
                foreach (var poemSlug in poems)
                {
                    var root = (await GetHtmlDocument($@"http://www.poemhunter.com/poem/{poemSlug}", Encoding.UTF8)).DocumentNode;

                    var author = root.SelectSingleNode(@"//meta[@itemprop='author']")
                        ?.GetAttributeValue("content", "");

                    if (author != null)
                    {
                        var title =
                            root.SelectSingleNode(@"//h1[@itemprop='name'][starts-with(@class, 'title')]").InnerText;
                        title = title.Substring(0, title.IndexOf(" - Poem by ", StringComparison.InvariantCulture));
                        var familyFriendly = root.SelectSingleNode(@"//meta[@itemprop='isFamilyFriendly']")
                            .GetAttributeValue("content", "") == "true";
                        var html = root.SelectSingleNode(@"//div[@class='KonaBody']//p").InnerHtml;

                        _connection.Execute(@"
                            update poem set
                                author = @author,
                                title = @title,
                                familyfriendly = @familyFriendly,
                                html = @html,
                                retrieved = current_timestamp
                            where slug = @poemSlug

                            update poet set
                                name = @author,
                                retrieved = current_timestamp
                            where slug = @poemSlug
                        ", new
                        {
                            author,
                            title,
                            familyFriendly,
                            html,
                            poemSlug,
                        });
                    }
                    else
                    {
                        _connection.Execute(@"
                            update poem set
                                retrieved = current_timestamp
                            where slug = @poemSlug
                        ");
                    }

                    await RandomDelay(TimeSpan.FromMilliseconds(DelayInterval));

                    progress.Increment();
                }
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
