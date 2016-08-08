using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

        private static Card[] GetJsonCards(string url)
        {
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

        private static int FindLastPage(Category category)
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
                    lower = page;
                }
            }

            return lower;
        }

        public static void Start()
        {
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
            Console.WriteLine(categories.Count);

            foreach (var category in categories)
            {
                var page = FindLastPage(category);
                break;
            }
        }
    }
}
