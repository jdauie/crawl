﻿using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Async;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;

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
    /// retry a few times, but if detail isn't available, drop the item?
    /// if image isn't available, ?
    /// </summary>
    public class Recipes
    {
        private const string BaseUrl = @"http://localjournal.submishmash.com";
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

        private static string GetDataResourceString(string file, object parameters = null)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream($"{typeof(Recipes).Namespace}.Data.{file}"))
            {
                return Populate(new StreamReader(stream).ReadToEnd(), parameters);
            }
        }

        private static object GetDataResourceJson(string file, object parameters = null)
        {
            return JsonConvert.DeserializeObject(GetDataResourceString(file, parameters));
        }

        private static string Populate(string str, object parameters)
        {
            parameters?
                .GetType()
                .GetProperties()
                .ToList()
                .ForEach(
                    x => str = str.Replace("{" + x.Name + "}", JsonConvert.SerializeObject(x.GetValue(parameters))));

            return str;
        }

        private static async Task<Submittable.Context> CreateAuthenticatedContext()
        {
            return await Submittable.Context.Login(BaseUrl, "josh+ignore+1@submittable.com", "password");
        }

        public static async Task<int> CreateCategory()
        {
            var context = await CreateAuthenticatedContext();

            var publisherId = 404;
            var productName = "asdf";
            var productId = 0;

            object parameters = new {
                PublisherId = publisherId,
                ProductName = productName,
                ProductId = productId,
            };

            productId = await context.EditCategory(GetDataResourceJson("create-category-1-edit.json", parameters));

            parameters = new {
                PublisherId = publisherId,
                ProductName = productName,
                ProductId = productId,
            };

            // these can probably run in parallel, but just to be safe...
            await context.SaveProductPrices(productId, GetDataResourceJson("create-category-2-saveproductprices.json", parameters));
            await context.SavePaymentAddons(productId, GetDataResourceJson("create-category-3-savepaymentaddons.json", parameters));
            await context.SaveForm(productId, GetDataResourceJson("create-category-4-saveform.json", parameters));
            await context.SaveReviewForm(productId, GetDataResourceJson("create-category-5-savereviewform.json", parameters));

            return productId;
        }

        public static async Task CreateSubmissions()
        {
            var context = await CreateAuthenticatedContext();
            var categoryId = 4332;

            var root = (await context.GetSubmissionPage(categoryId)).DocumentNode;
            
            var controlGroups = root.SelectNodes(
                @"//div[contains(@class, 'ctrlHolder')]")
                .ToList();

            // always included, even if branch is not selected
            var controlLabels = controlGroups.Select(x => x.PreviousSibling.PreviousSibling)
                .Select(x => new KeyValuePair<string, string>(x.GetAttributeValue("name", ""), x.GetAttributeValue("value", ""))).ToList();
            
            var controlInnerLabels = controlGroups.Select(x => x.SelectSingleNode(@"input[@type='hidden']"))
                .Select(x => new KeyValuePair<string, string>(x?.GetAttributeValue("name", ""), x?.GetAttributeValue("value", ""))).ToList();

            var controls = controlGroups.Select(x => x.SelectSingleNode(@"div[contains(@class, 'left-side')]").ChildNodes[1]).ToList();

            var groups = new Dictionary<string, List<KeyValuePair<string, string>>>();

            for (var i = 0; i < controlGroups.Count; i++)
            {
                var pairs = new List<KeyValuePair<string, string>>();
                var getInnerLabel = true;

                var control = controls[i];
                if (control.Name == "ul")
                {
                    // option list
                    var options = control.SelectNodes(@".//input")
                        .Select(x => new KeyValuePair<string, string>(x.GetAttributeValue("name", ""), "")).ToList();
                    // later, fill in the first one
                    pairs.AddRange(options);
                }
                else if (control.Name == "div")
                {
                    // file upload
                    var hidden = controlGroups[i].SelectNodes(@"./input[@type='hidden']")
                        .Take(4)
                        .Select(x => new KeyValuePair<string, string>(x.GetAttributeValue("name", ""), x.GetAttributeValue("value", ""))).ToList();
                    pairs.AddRange(hidden);
                    getInnerLabel = false;
                }
                else
                {
                    pairs.Add(new KeyValuePair<string, string>(control.GetAttributeValue("name", ""), ""));
                }
                
                if (getInnerLabel && !string.IsNullOrEmpty(controlInnerLabels[i].Key))
                {
                    pairs.Add(controlInnerLabels[i]);
                }

                groups[controlLabels[i].Value] = pairs;
            }
            
            var otherHiddenFields = root.SelectNodes(@"//form/input[@type='hidden'][not(contains(@name, 'CustomField'))]")
                .Select(x => new KeyValuePair<string, string>(x.GetAttributeValue("name", ""), x.GetAttributeValue("value", ""))).ToList();
            
            //var items = new List<Item>();

            //// sample
            //items.Add(JsonConvert.DeserializeObject<Item>(
            //    File.ReadAllText(@"C:\tmp\scrape\recipes\chunk-46\item-13000.json")));

            var items = LoadAllItems();//.Take(100);

            var submissionCount = 0;

            await items.ParallelForEachAsync(async item =>
            {
                var data = new List<KeyValuePair<string, string>>();

                // temporarily do this without images
                item.ImageName = null;

                data.AddRange(GetPairs(groups["Title"], item.Title));
                data.AddRange(GetPairs(groups["Description"], item.Description));
                data.AddRange(GetPairs(groups["Cook"], item.Cook));
                data.AddRange(GetPairs(groups["Is there an image?"], (item.ImageName != null) ? "Yes" : "No"));
                if (item.ImageName != null)
                {
                    data.AddRange(GetPairs(groups["Image"]));
                }
                data.AddRange(GetPairs(groups["Ingredients"], string.Join("\n", item.Ingredients)));
                data.AddRange(GetPairs(groups["Directions"], string.Join("\n", item.Directions)));
                data.AddRange(GetPairs(groups["Calories"], item.Calories));
                data.AddRange(GetPairs(groups["Is the preparation time known?"], !string.IsNullOrWhiteSpace(item.PrepTime) ? "Yes" : "No"));
                if (!string.IsNullOrWhiteSpace(item.PrepTime))
                {
                    data.AddRange(GetPairs(groups["Preparation Time"], item.PrepTime));
                }
                data.AddRange(GetPairs(groups["Is the total time known?"], !string.IsNullOrWhiteSpace(item.TotalTime) ? "Yes" : "No"));
                if (!string.IsNullOrWhiteSpace(item.TotalTime))
                {
                    data.AddRange(GetPairs(groups["Total Time"], item.TotalTime));
                }
                data.AddRange(GetPairs(groups["URL"], $"http://allrecipes.com/recipe/{item.Id}"));

                data.AddRange(controlLabels);
                data.AddRange(otherHiddenFields);

                await context.Submit(categoryId, data);

                Interlocked.Increment(ref submissionCount);
                
                Console.Write($"submitted {submissionCount}\r");
            }, maxDegreeOfParalellism: 8);

            Console.Write($"{new string(' ', 80)}\r");
            Console.WriteLine($"submitted {submissionCount}");
        }

        private static IEnumerable<KeyValuePair<string, string>> GetPairs(IReadOnlyList<KeyValuePair<string, string>> group, string value = null)
        {
            yield return new KeyValuePair<string, string>(group[0].Key, value ?? group[0].Value);
            foreach (var pair in group.Skip(1))
            {
                yield return pair;
            }
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
