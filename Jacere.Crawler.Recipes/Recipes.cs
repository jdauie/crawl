using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jacere.Crawler.Recipes
{
    /// <summary>
    /// get categories and auth token
    /// http://allrecipes.com/recipes/?grouping=all
    /// 
    /// if the items were already cached, load them, otherwise...
    /// 
    ///     for each category, probe for last page (with counter for unbounded progress)
    ///     https://apps.allrecipes.com/v1/assets/hub-feed?id=${categoryId}&pageNumber=${pageNumber}&isSponsored=true&sortType=p
    ///     
    ///     get all pages (with progress)
    /// 
    ///     merge list of unique cards where card.id && card.itemType !== 'Video'
    /// 
    /// for each card, get the detail view and the image (if any)
    /// http://allrecipes.com/recipe/${item.id}
    /// http://images.media-allrecipes.com/userphotos/600x600/${item.image}
    /// 
    /// retry a few times, but if detail isn't available, drop the item
    /// if image isn't available, ?
    /// </summary>
    public class Recipes
    {
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

        private static void GetDetail()
        {
            //http://allrecipes.com/recipe/:id
        }

        private static List<Item> GetItemsFromPage(Category category, int page)
        {
            return GetJsonCards($"https://apps.allrecipes.com/v1/assets/hub-feed?id={category.Id}&pageNumber={page}&isSponsored=true&sortType=p")
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

        public static void Start()
        {
            const string storageRoot = @"C:/tmp/scrape/recipes";
            const bool debugFirstPageOnly = false;
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, true);
            }
            Directory.CreateDirectory(storageRoot);
            while (!Directory.Exists(storageRoot))
            {
                Thread.Sleep(100);
            }

            var doc = GetHtmlDocument(@"http://allrecipes.com/recipes/?grouping=all", true);
            var categories = doc.DocumentNode
                .SelectNodes(@"//section[@id='herolinks']/div[@ng-show='showAll===true']//a[@class='hero-link__item']")
                .Select(node => {
                    var match = Regex.Match(node.GetAttributeValue("href", null), @"^/recipes/(\d+)/([^/]+)/$");
                    return new Category {
                        Id = int.Parse(match.Groups[1].Value),
                        Slug = match.Groups[2].Value,
                        Title = node.GetAttributeValue("title", null),
                        ImageUrl = node.SelectSingleNode("img").GetAttributeValue("src", null),
                    };
                }).ToList();
            if (debugFirstPageOnly)
            {
                categories = new List<Category>()
                {
                    categories[0],
                };
            }
            Console.WriteLine($"{categories.Count} categories");

            var cachedPages = categories.ToDictionary(c => c.Id, c => new Dictionary<int, List<Item>>());

            foreach (var category in categories)
            {
                if (debugFirstPageOnly)
                {
                    category.PageCount = 1;
                    break;
                }
                category.PageCount = FindLastPage(category, (page, items) =>
                {
                    cachedPages[category.Id][page] = items;
                    Console.Write($"probing {cachedPages.Values.Sum(c => c.Count)}\r");
                });
            }

            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"{cachedPages.Values.Sum(c => c.Count)} probes");
            Console.WriteLine($"{categories.Sum(c => c.PageCount)} pages");

            var estimatedTotalItems = categories.Sum(c => c.PageCount) * 20;
            var actualItems = 0;
            var failedItems = 0;
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

                            item.Calories = detailsRoot.SelectNodes(@"//span[@class='calorie-count']/span")
                                .First().InnerText;

                            item.PrepTime = detailsRoot.SelectNodes(@"//time[@itemprop='prepTime']")?
                                .First()
                                .GetAttributeValue("datetime", null)?.Substring(2);

                            item.TotalTime = detailsRoot.SelectNodes(@"//time[@itemprop='totalTime']")?
                                .First()
                                .GetAttributeValue("datetime", null)?.Substring(2);

                            if (item.ImageName != null)
                            {
                                var imageUrl = $"http://images.media-allrecipes.com/userphotos/600x600/{item.ImageName}";
                                using (var outStream = new FileStream(Path.Combine(storageRoot, item.ImageName),
                                    FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                {
                                    SaveImage(imageUrl, outStream);
                                }
                                ++imageItems;
                            }

                            File.WriteAllText(Path.Combine(storageRoot, $"item-{item.Id}.json"),
                                JsonConvert.SerializeObject(item));
                        }
                        catch
                        {
                            ++failedItems;
                        }

                        ++actualItems;
                        
                        var remainingTime = TimeSpan.FromSeconds((DateTime.Now - startTime).TotalSeconds / actualItems * estimatedTotalItems);
                        Console.Write($"crawling {actualItems} ({remainingTime.ToString(@"dd\.hh\:mm\:ss")} remaining)\r");
                    }
                }
            }

            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"{actualItems} items ({imageItems} images, {failedItems} failed)");
        }
    }
}
