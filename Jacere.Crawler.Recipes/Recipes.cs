using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace Jacere.Crawler.Recipes
{
    public class Recipes
    {
        private const string StorageRoot = @"C:/tmp/scrape/recipes";

        private const int CrawlDelay = 500;

        private static string _token;

        private static HtmlDocument GetHtmlDocument(string url, bool token = false)
        {
            var request = WebRequest.Create(url);
            using (var response = request.GetResponse())
            {
                if (token)
                {
                    var match = Regex.Match(response.Headers["Set-Cookie"], @"ARToken=([^;]+);");
                    _token = match.Groups[1].Value;
                }

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

        private static void RandomDelay()
        {
            var delay = new Random().Next(CrawlDelay / 2, CrawlDelay + CrawlDelay / 2);
            Thread.Sleep(delay);
        }

        private static Card[] GetJsonCards(string url)
        {
            RandomDelay();

            var request = WebRequest.Create(url);
            request.Headers.Set("Authorization", $"Bearer {_token}");

            using (var response = request.GetResponse())
            {
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    return JsonConvert.DeserializeObject<Page>(reader.ReadToEnd()).Cards;
                }
            }
        }

        private static List<Item> GetItemsFromPage(Category category, int page)
        {
            Card[] cards = null;

            while (true)
            {
                try
                {
                    cards = GetJsonCards($"https://apps.allrecipes.com/v1/assets/hub-feed?id={category.Id}&pageNumber={page}&isSponsored=true&sortType=p");
                }
                catch (WebException e)
                {
                    if ((e.Response as HttpWebResponse).StatusCode == HttpStatusCode.Unauthorized)
                    {
                        GetHtmlDocument(@"http://allrecipes.com/recipes/?grouping=all", true);
                        continue;
                    }
                    else
                    {
                        throw;
                    }
                }
                break;
            }

            return cards
                .Where(card => card.IsValid)
                .Select(card => new Item {
                    Id = card.Id,
                    CategoryId = category.Id,
                    Title = card.Title,
                    Description = card.Description,
                    Cook = card.Cook?.displayName ?? card.Submitter?.cookHandle,
                    ImageName = Path.GetFileName(card.ImageUrl),
                }).ToList();
        }

        private static int FindLastPage(Category category, Action<int, List<Item>> progress)
        {
            var lower = 1;
            var upper = 0;

            // expand search space
            while (true)
            {
                var items = GetItemsFromPage(category, lower);
                if (items.Count == 0)
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
                var items = GetItemsFromPage(category, page);
                if (items.Count == 0)
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

        public static void Start(bool debugSampleOnly = false)
        {
            Directory.CreateDirectory(StorageRoot);

            var categoriesPath = Path.Combine(StorageRoot, "categories.json");

            List<Category> categories;
            Dictionary<int, Dictionary<int, List<Item>>> cachedPages; ;

            if (File.Exists(categoriesPath))
            {
                categories = JsonConvert.DeserializeObject<List<Category>>(File.ReadAllText(categoriesPath));
                cachedPages = categories.ToDictionary(c => c.Id, c => new Dictionary<int, List<Item>>());
            }
            else
            {
                var doc = GetHtmlDocument(@"http://allrecipes.com/recipes/?grouping=all", true);
                categories = doc.DocumentNode
                    .SelectNodes(@"//section[@id='herolinks']/div[@ng-show='showAll===true']//a[@class='hero-link__item']")
                    .Select(node =>
                    {
                        var match = Regex.Match(node.GetAttributeValue("href", null), @"^/recipes/(\d+)/([^/]+)/$");
                        return new Category {
                            Id = int.Parse(match.Groups[1].Value),
                            Slug = match.Groups[2].Value,
                            Title = node.GetAttributeValue("title", null),
                            ImageUrl = node.SelectSingleNode("img").GetAttributeValue("src", null),
                        };
                    }).ToList();

                Console.WriteLine($"{categories.Count} categories");

                cachedPages = categories.ToDictionary(c => c.Id, c => new Dictionary<int, List<Item>>());

                foreach (var category in categories)
                {
                    category.PageCount = FindLastPage(category, (page, items) =>
                    {
                        cachedPages[category.Id][page] = items;
                        Console.Write($"probing {cachedPages.Values.Sum(c => c.Count)}\r");
                    });
                }

                File.WriteAllText(categoriesPath, JsonConvert.SerializeObject(categories));

                Console.Write($"{new string(' ', 80)}\r");
                Console.WriteLine($"{cachedPages.Values.Sum(c => c.Count)} probes");
            }
            
            if (debugSampleOnly)
            {
                categories = new List<Category>()
                {
                    categories[0],
                };
                categories[0].PageCount = 1;
            }

            Console.WriteLine($"{categories.Sum(c => c.PageCount)} pages");

            var estimatedTotalItems = categories.Sum(c => c.PageCount) * 20;
            var actualItems = 0;
            var failedItems = 0;
            var existingItems = 0;
            var imageItems = 0;

            var startTime = DateTime.Now;

            foreach (var category in categories)
            {
                for (var page = 1; page <= category.PageCount; page++)
                {
                    var pageItems = cachedPages[category.Id].ContainsKey(page)
                        ? cachedPages[category.Id][page]
                        : GetItemsFromPage(category, page);
                    
                    foreach (var item in pageItems)
                    {
                        var storageChunkPath = Path.Combine(StorageRoot, $"chunk-{item.Id % 100}");
                        Directory.CreateDirectory(storageChunkPath);
                        var itemPath = Path.Combine(storageChunkPath, $"item-{item.Id}.json");

                        if (!File.Exists(itemPath))
                        {
                            RandomDelay();

                            try
                            {
                                var detailsRoot = GetHtmlDocument($"http://allrecipes.com/recipe/{item.Id}").DocumentNode;

                                item.Ingredients = detailsRoot.SelectNodes(
                                    @"//section[@class='recipe-ingredients']//li[@class='checkList__line']//span[contains(@class,'recipe-ingred_txt')][@itemprop='ingredients']")
                                    .Select(node => node.InnerText).ToList();

                                item.Directions = detailsRoot.SelectNodes(
                                    @"//ol[@itemprop='recipeInstructions']//span[contains(@class, 'recipe-directions__list--item')]")
                                    .Select(node => node.InnerText).ToList();

                                item.Calories = detailsRoot.SelectNodes(@"//span[@class='calorie-count']/span")?
                                    .First().InnerText;

                                item.PrepTime = detailsRoot.SelectNodes(@"//time[@itemprop='prepTime']")?
                                    .First()
                                    .GetAttributeValue("datetime", null)?.Substring(2);

                                item.TotalTime = detailsRoot.SelectNodes(@"//time[@itemprop='totalTime']")?
                                    .First()
                                    .GetAttributeValue("datetime", null)?.Substring(2);

                                if (item.ImageName == "44555.png")
                                {
                                    item.ImageName = null;
                                }

                                if (item.ImageName != null)
                                {
                                    var imageUrl = $"http://images.media-allrecipes.com/userphotos/600x600/{item.ImageName}";
                                    var imagePath = Path.Combine(storageChunkPath, item.ImageName);
                                    if (!File.Exists(imagePath))
                                    {
                                        using (var outStream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                        {
                                            SaveImage(imageUrl, outStream);
                                        }
                                    }
                                    ++imageItems;
                                }

                                File.WriteAllText(itemPath, JsonConvert.SerializeObject(item));
                            }
                            catch
                            {
                                ++failedItems;
                            }
                        }
                        else
                        {
                            ++existingItems;
                        }

                        ++actualItems;
                        
                        var remainingTime = TimeSpan.FromSeconds((DateTime.Now - startTime).TotalSeconds / (actualItems - existingItems + 1) * (estimatedTotalItems - existingItems + 1));
                        Console.Write($"crawling {actualItems} ({remainingTime.ToString(@"dd\.hh\:mm\:ss")} remaining)\r");
                    }
                }
            }

            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"{actualItems} items ({imageItems} images, {failedItems} failed)");
        }

        internal static IEnumerable<Item> LoadAllItems()
        {
            return Directory.EnumerateDirectories(StorageRoot)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.json")
                    .Select(file =>
                    {
                        var item = JsonConvert.DeserializeObject<Item>(File.ReadAllText(file));
                        if (item.ImageName != null)
                        {
                            item.ImageName = Path.Combine(dir, item.ImageName);
                        }
                        return item;
                    }));
        }

        public static void Analyze()
        {
            var total = 0;
            var hasCook = 0;
            var hasImage = 0;
            var hasCalories = 0;
            var hasPrepTime = 0;
            var hasTotalTime = 0;

            var cooks = new HashSet<string>();

            foreach (var dir in Directory.EnumerateDirectories(StorageRoot))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    var item = JsonConvert.DeserializeObject<Item>(File.ReadAllText(file));
                    ++total;
                    if (!string.IsNullOrWhiteSpace(item.Cook)) ++hasCook;
                    if (!string.IsNullOrWhiteSpace(item.ImageName)) ++hasImage;
                    if (!string.IsNullOrWhiteSpace(item.Calories)) ++hasCalories;
                    if (!string.IsNullOrWhiteSpace(item.PrepTime)) ++hasPrepTime;
                    if (!string.IsNullOrWhiteSpace(item.TotalTime)) ++hasTotalTime;

                    if (string.IsNullOrWhiteSpace(item.Cook))
                    {
                        Console.WriteLine(file);
                    }

                    cooks.Add(item.Cook);
                }
            }

            Console.WriteLine($"total:        {total}");
            Console.WriteLine($"hasCook:      {hasCook}");
            Console.WriteLine($"hasImage:     {hasImage}");
            Console.WriteLine($"hasCalories:  {hasCalories}");
            Console.WriteLine($"hasPrepTime:  {hasPrepTime}");
            Console.WriteLine($"hasTotalTime: {hasTotalTime}");

            Console.WriteLine($"distinct cooks: {cooks.Count}");
        }
    }
}
