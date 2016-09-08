using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using HtmlAgilityPack;

namespace Jacere.Crawler.Stories
{
    public class Stories
    {
        private const string StorageRoot = @"C:/tmp/scrape/stories";

        private const int CrawlDelay = 500;

        private static void RandomDelay()
        {
            var delay = new Random().Next(CrawlDelay / 2, CrawlDelay + CrawlDelay / 2);
            Thread.Sleep(delay);
        }

        private static HtmlDocument GetHtmlDocument(string url)
        {
            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            {
                var doc = new HtmlDocument();
                doc.Load(response.GetResponseStream());
                return doc;
            }
        }

        private static List<Item> GetItemsFromPage(int page)
        {
            RandomDelay();

            var root = GetHtmlDocument($@"http://storybird.com/read/?sort=recent&page={page}&format=longform").DocumentNode;
            return root.SelectNodes(@"//article[@class='fact-item']").Select(x => new Item {
                Slug = x.SelectSingleNode(@".//h1[@class='fact-item-title']/a").GetAttributeValue("href", "")
                    .Split(new []{'/'}, StringSplitOptions.RemoveEmptyEntries)[1],
                Title = x.SelectSingleNode(@".//h1[@class='fact-item-title']/a").InnerText,
                Image = x.SelectSingleNode(@"./div//img").GetAttributeValue("src", ""),
                Author = x.SelectSingleNode(@".//p[@class='avatar-text-inner']").InnerText,
            }).ToList();
        }

        private static int FindLastPage(Action<int, List<Item>> progress)
        {
            // this logic will lose the last page, but whatever
            const string earliestStorySlug = "macs-day";

            var lower = 1;
            var upper = 0;

            // expand search space
            while (true)
            {
                var items = GetItemsFromPage(lower);
                if (items.Last().Slug == earliestStorySlug)
                {
                    upper = lower;
                    lower /= 2;
                    break;
                }
                progress(lower, items);
                lower *= 2;
            }

            // contract search space
            while (true)
            {
                var page = (lower + upper) / 2;
                if (page == lower)
                {
                    break;
                }
                var items = GetItemsFromPage(page);
                if (items.Last().Slug == earliestStorySlug)
                {
                    upper = page;
                }
                else
                {
                    progress(page, items);
                    lower = page;
                }
            }

            return lower;
        }

        public static void Start()
        {
            Directory.CreateDirectory(StorageRoot);

            var cachedPages = new Dictionary<int, List<Item>>();

            var pageCount = FindLastPage((page, items) =>
            {
                cachedPages[page] = items;
                Console.Write($"probing {cachedPages.Count}\r");
            });
        }
    }
}
