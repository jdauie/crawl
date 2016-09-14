﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jacere.Crawler.Core;
using Newtonsoft.Json;

namespace Jacere.Crawler.Poems
{
    public class Poems
    {
        private const string StorageRoot = @"C:/tmp/scrape/poems";

        private const int CrawlDelay = 500;

        private static void RandomDelay()
        {
            var delay = new Random().Next(CrawlDelay / 2, CrawlDelay + CrawlDelay / 2);
            Thread.Sleep(delay);
        }

        private static HtmlDocument GetHtmlDocument(string url)
        {
            RandomDelay();

            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            {
                var doc = new HtmlDocument();
                doc.Load(response.GetResponseStream());
                return doc;
            }
        }

        private static List<string> GetPages()
        {
            var urls = new List<string>();
            using (var progress = new ConsoleProgress("pages", 3279))
            {
                var page = 1;
                while (true)
                {
                    try
                    {
                        var root = GetHtmlDocument($@"http://www.poemhunter.com/poems/classical-poems/?a=0&l=classic&order=title&p={page}").DocumentNode;

                        progress.Increment();

                        var items = root.SelectNodes(@"//table[@class='poems-listing']//td[@class='title']/a[starts-with(@href, '/poem/')]")
                            .Select(x => x.GetAttributeValue("href", ""));

                        urls.AddRange(items);

                        if (root.SelectSingleNode(@"//div[@class='pagination']//li[@class='next']/a") == null)
                        {
                            break;
                        }

                        ++page;
                    }
                    catch (WebException e)
                    {
                        if ((e.Response as HttpWebResponse).StatusCode == HttpStatusCode.InternalServerError)
                        {
                            // retry
                            continue;
                        }
                        throw;
                    }
                }
            }
            return urls;
        }

        public static void Start()
        {
            Directory.CreateDirectory(StorageRoot);

            var poemUrls = GetPages();

            var slugsPath = Path.Combine(StorageRoot, "slugs.json");

            File.WriteAllText(slugsPath, JsonConvert.SerializeObject(poemUrls));
        }
    }
}
