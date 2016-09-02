using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jacere.Submittable
{
    public class Context
    {
        private readonly Uri _baseUri;
        private List<string> _sessionCookie;

        private Context(string baseUrl)
        {
            _baseUri = new Uri(baseUrl);
        }

        public static async Task<Context> Login(string baseUrl, string user, string password)
        {
            var context = new Context(baseUrl);
            await context.DoPost("/account/ajaxlogin", new StringContent(JsonConvert.SerializeObject(new {
                UserName = user,
                Password = password,
            }), Encoding.UTF8, "application/json"), response =>
            {
                context._sessionCookie = response.Headers.GetValues("Set-Cookie").ToList();
            });
            return context;
        }

        public async Task<HtmlDocument> GetSubmissionPage(int categoryId)
        {
            return await GetHtmlDocument($"/submit/{categoryId}");
        }

        public async Task<int> EditCategory(object data)
        {
            var form = new FormUrlEncodedContent(GetProperties(data));
            var categoryId = 0;

            await DoPost("/categories/edit", form, async response =>
            {
                categoryId = int.Parse(await response.Content.ReadAsStringAsync());
            });

            return categoryId;
        }

        private async Task DoGet(string url, Func<HttpResponseMessage, Task> action = null)
        {
            using (var handler = new HttpClientHandler { UseCookies = false })
            using (var client = new HttpClient(handler) { BaseAddress = _baseUri })
            {
                client.DefaultRequestHeaders.Add("Cookie", _sessionCookie);
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                if (action != null)
                {
                    await action(response);
                }
            }
        }

        private async Task DoPost(string url, HttpContent content, Action<HttpResponseMessage> action)
        {
            using (var handler = new HttpClientHandler { UseCookies = false })
            using (var client = new HttpClient(handler) { BaseAddress = _baseUri })
            {
                if (_sessionCookie != null)
                {
                    client.DefaultRequestHeaders.Add("Cookie", _sessionCookie);
                }
                var response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                action(response);
            }
        }

        private async Task DoPost(string url, HttpContent content, Func<HttpResponseMessage, Task> action = null)
        {
            using (var handler = new HttpClientHandler { UseCookies = false })
            using (var client = new HttpClient(handler) { BaseAddress = _baseUri })
            {
                client.DefaultRequestHeaders.Add("Cookie", _sessionCookie);
                var response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                if (action != null)
                {
                    await action(response);
                }
            }
        }

        private async Task DoPostJson(string url, object data)
        {
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            await DoPost(url, content);
        }

        public async Task Submit(int categoryId, IEnumerable<KeyValuePair<string, string>> data)
        {
            var form = new MultipartFormDataContent();
            foreach (var pair in data)
            {
                form.Add(new StringContent(pair.Value), pair.Key);
            }
            await DoPost($"/submit/{categoryId}/submission", form);
        }

        public async Task SaveProductPrices(int id, object data)
        {
            await DoPostJson($"/categories/saveproductprices/{id}", data);
        }

        public async Task SavePaymentAddons(int id, object data)
        {
            await DoPostJson($"/categories/savepaymentaddons/{id}", data);
        }

        public async Task SaveForm(int id, object data)
        {
            await DoPostJson($"/categories/saveform/{id}", data);
        }

        public async Task SaveReviewForm(int id, object data)
        {
            await DoPostJson($"/categories/savereviewform/{id}", data);
        }

        public async Task<HtmlDocument> GetHtmlDocument(string url)
        {
            HtmlNode.ElementsFlags.Remove("form");
            var doc = new HtmlDocument();

            await DoGet(url, async response =>
            {
                doc.Load(await response.Content.ReadAsStreamAsync());
            });

            return doc;
        }

        private static IEnumerable<KeyValuePair<string, string>> GetProperties(object obj)
        {
            if (obj is JObject)
            {
                return (obj as JObject)
                    .Properties()
                    .Select(x => new KeyValuePair<string, string>(x.Name, Convert.ToString(x.Value)));
            }
            return obj
                .GetType()
                .GetProperties()
                .Select(x => new KeyValuePair<string, string>(x.Name, Convert.ToString(x.GetValue(obj))));
        }
    }
}
