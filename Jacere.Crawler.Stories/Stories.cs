using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Jacere.Crawler.Stories
{
    public class Stories
    {
        private const string StorageRoot = @"C:/tmp/scrape/stories";

        private const string EarliestStorySlug = "macs-day";

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

        private static void SaveImage(string url, Stream outStream)
        {
            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            {
                response.GetResponseStream().CopyTo(outStream);
            }
        }

        private static Chapter GetChapter(HtmlNode root)
        {
            var articleNode = root.SelectSingleNode(@"//article[@class='cm cm-cover-rounded-corner']");
            var chapterTitleNode = root.SelectSingleNode(@"//div[@class='cm-chapter-title']");
            var noteNode = root.SelectSingleNode(@"//article[@class='sidebar-section']/h2[text()='Chapter Note:']")?.ParentNode;
            var detailsNode = root.SelectSingleNode(@"//article[@class='sidebar-section']/h2[text()='Details:']")?.ParentNode;
            var publishedText = detailsNode?.SelectSingleNode(@".//p[contains(text(), 'Published:')]")?.InnerText.Split(':').Last().Trim();
            var updatedText = detailsNode?.SelectSingleNode(@".//p[contains(text(), 'Last Update:')]")?.InnerText.Split(':').Last().Trim();
            
            return new Chapter
            {
                Title = chapterTitleNode?.InnerText,
                Note = noteNode?.SelectSingleNode(@"./p").InnerText,
                Components = articleNode.SelectNodes(@"./div[@class='cm-component cm-text-container'][@id]/div").Select(x => x.InnerText).ToList(),
                Published = publishedText != null ? (DateTime?)DateTime.ParseExact(publishedText, "MMMM dd, yyyy", DateTimeFormatInfo.CurrentInfo) : null,
                Updated = updatedText != null ? (DateTime?)DateTime.ParseExact(updatedText, "MMMM dd, yyyy", DateTimeFormatInfo.CurrentInfo) : null,
            };
        }

        private static Item GetItem(string slug)
        {
            var root = GetHtmlDocument($@"http://storybird.com/chapters/{slug}/").DocumentNode;
            var tagNode = root.SelectSingleNode(@"//article[@class='sidebar-article']/h2[text()='Tags:']")?.ParentNode;

            var item = new Item
            {
                Slug = slug,
                Title = root.SelectSingleNode(@"//head/meta[@property='og:title']").GetAttributeValue("content", ""),
                Image = root.SelectSingleNode(@"//div[@class='cm-title-background-art']/img").GetAttributeValue("src", ""),
                Summary = root.SelectSingleNode(@"//head/meta[@property='og:description']").GetAttributeValue("content", ""),
                Author = root.SelectSingleNode(@"//head/meta[@property='book:author']").GetAttributeValue("content", "")
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last(),
                Artist = root.SelectSingleNode(@"//head/meta[@property='book: author']").GetAttributeValue("content", "")
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last(),
                Tags = tagNode?.SelectNodes(@"./a").Select(x => x.InnerText).ToList(),
            };

            item.Title = Regex.Replace(item.Title, $@" by {Regex.Escape(item.Author)} on Storybird$", "");
            
            item.Chapters = new List<Chapter>();

            if (root.SelectNodes(@"//div[@class='ma20 mb40']") == null)
            {
                item.Chapters.Add(GetChapter(root));
            }
            else
            {
                var chapters = root.SelectNodes(@"//div[@class='ma20 mb40']//a").Select(x => int.Parse(x.GetAttributeValue("href", "")
                    .Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries).Last())).ToList();

                foreach (var chapter in chapters)
                {
                    var chapterNode = GetHtmlDocument($@"http://storybird.com/chapters/{slug}/{chapter}/").DocumentNode;
                    item.Chapters.Add(GetChapter(chapterNode));
                }
            }

            return item;
        }

        private static List<string> GetSlugsFromPage(int page)
        {
            var root = GetHtmlDocument($@"http://storybird.com/read/?sort=recent&page={page}&format=longform").DocumentNode;

            return root.SelectNodes(@"//article[@class='fact-item']")
                .Select(x => x.SelectSingleNode(@".//h1[@class='fact-item-title']/a").GetAttributeValue("href", "")
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[1]).ToList();
        }

        private static int FindLastPage(ConsoleProgress progress)
        {
            var lower = 1;
            var upper = 0;

            // expand search space
            while (true)
            {
                var items = GetSlugsFromPage(lower);
                if (items.Last() == EarliestStorySlug)
                {
                    upper = lower;
                    lower /= 2;
                    break;
                }
                progress.Increment();
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
                var items = GetSlugsFromPage(page);
                if (items.Last() == EarliestStorySlug)
                {
                    upper = page;
                }
                else
                {
                    progress.Increment();
                    lower = page;
                }
            }

            return lower;
        }

        public static void Start(bool debugSampleOnly = false)
        {
            Directory.CreateDirectory(StorageRoot);

            var pageCount = 0;

            var allSlugs = new HashSet<string>();

            var slugsPath = Path.Combine(StorageRoot, "slugs.json");

            if (File.Exists(slugsPath))
            {
                var slugs = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(slugsPath));
                if (debugSampleOnly)
                {
                    slugs = slugs.Take(10).ToList();
                }
                allSlugs.UnionWith(slugs);
            }
            else
            {
                using (var progress = new ConsoleProgress("probe"))
                {
                    if (!debugSampleOnly)
                    {
                        pageCount = FindLastPage(progress);
                    }
                }

                using (var progress = new ConsoleProgress("pages", pageCount))
                {
                    var page = 1;
                    while (true)
                    {
                        var slugs = GetSlugsFromPage(page);
                        allSlugs.UnionWith(slugs);
                        progress.Increment();
                        if (slugs.Last() == EarliestStorySlug)
                        {
                            break;
                        }
                        ++page;

                        if (debugSampleOnly)
                        {
                            break;
                        }
                    }
                }

                if (!debugSampleOnly)
                {
                    File.WriteAllText(slugsPath, JsonConvert.SerializeObject(allSlugs.ToList()));
                }
            }

            using (var progress = new ConsoleProgress("items", allSlugs.Count))
            {
                foreach (var slug in allSlugs)
                {
                    var chunkId = slug.GetHashCode() % 100;
                    var storageChunkPath = Path.Combine(StorageRoot, $"chunk-{chunkId}");
                    Directory.CreateDirectory(storageChunkPath);
                    var itemPath = Path.Combine(storageChunkPath, $"{slug}.json");

                    if (File.Exists(itemPath))
                    {
                        continue;
                    }

                    var item = GetItem(slug);
                    
                    var imagePath = Path.Combine(storageChunkPath, $"{slug}.jpeg");
                    if (!File.Exists(imagePath))
                    {
                        using (var outStream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            SaveImage(item.Image, outStream);
                        }
                    }

                    File.WriteAllText(itemPath, JsonConvert.SerializeObject(item));

                    progress.Increment();
                }
            }
        }
    }
}
