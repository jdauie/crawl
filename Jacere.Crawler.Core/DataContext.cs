using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Jacere.Crawler.Core
{
    public abstract class DataContext
    {
        private readonly Uri _baseUri;

        protected TimeSpan DelayInterval { get; }

        public ConsoleProgress Progress { get; set; }

        protected DataContext(Uri baseUri, IDataCommand command)
        {
            _baseUri = baseUri;
            DelayInterval = TimeSpan.FromMilliseconds(command.DelayInterval);
        }

        protected virtual void PopulateClientRequestHeaders(HttpRequestHeaders headers)
        {
        }

        public DataContext SetProgress(ConsoleProgress progress)
        {
            Progress = progress;
            return this;
        }

        protected async Task ForEachHtmlPage(Encoding pageEncoding, Func<int, string> getPageUrl, 
            Func<HtmlNode, int> getLastPage, Action<HtmlNode> handlePage, Func<HtmlNode, bool> hasNextPage, 
            string progressTitle)
        {
            var progress = progressTitle != null 
                ? new ConsoleProgress(progressTitle)
                : null;

            using (progress)
            {
                for (var i = 1;; i++)
                {
                    var nextPage = getPageUrl(i);
                    var root = (await GetHtmlDocument(nextPage, pageEncoding)).DocumentNode;

                    if (progress != null)
                    {
                        var lastPage = getLastPage(root);
                        progress.SetTotal(lastPage);
                    }

                    handlePage(root);

                    progress?.Increment();

                    if (!hasNextPage(root))
                    {
                        break;
                    }

                    await RandomDelay();
                }
            }
        }

        protected async Task ForeachHtmlPageWithNext(Action<string> handleItem, string pageUrlFormat,
            string itemsSelector, string nextSelector, Encoding encoding = null, Func<IEnumerable<string>,
            IEnumerable<string>> filterItems = null, string lastPageSelector = null, string progressTitle = null)
        {
            await ForEachHtmlPage(
                Encoding.UTF8,
                page => string.Format(pageUrlFormat, page),
                root => root.Select(lastPageSelector)
                        .First().GetValueInt(),
                root =>
                {
                    var items = root.Select(itemsSelector)
                        .Select(x => x.GetAttribute("href"));

                    if (filterItems != null)
                    {
                        items = filterItems(items);
                    }

                    foreach (var item in items)
                    {
                        handleItem(item);
                    }
                },
                root => root.Has(nextSelector),
                progressTitle
            );
        }

        private async Task DoMethodWithRetry(Func<HttpClient, Task<HttpResponseMessage>> method, Func<HttpResponseMessage, Task> action = null, Func<Task> cleanupBeforeRetry = null)
        {
            using (var handler = new HttpClientHandler { UseCookies = false })
            using (var client = new HttpClient(handler) { BaseAddress = _baseUri })
            {
                PopulateClientRequestHeaders(client.DefaultRequestHeaders);

                var retryDelaySeconds = 1;

                HttpResponseMessage response;

                while (true)
                {
                    try
                    {
                        response = await method(client);
                        response.EnsureSuccessStatusCode();
                        break;
                    }
                    catch (Exception)
                    {
                        if (cleanupBeforeRetry != null)
                        {
                            await cleanupBeforeRetry();
                        }

                        Progress?.Increment("retries");
                        await RandomDelay(TimeSpan.FromSeconds(retryDelaySeconds));

                        // exponential backoff with maxiumum
                        retryDelaySeconds = Math.Min(retryDelaySeconds * 2, 30);
                    }
                }
                
                if (action != null)
                {
                    await action(response);
                }

                response.Dispose();
            }
        }

        public async Task DoGet(string url, Func<HttpResponseMessage, Task> action = null)
        {
            await DoMethodWithRetry(async client => await client.GetAsync(url), action);
        }

        public async Task<HtmlDocument> GetHtmlDocument(string url, Encoding encoding = null)
        {
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }

            var doc = new HtmlDocument();

            await DoGet(url, async response =>
            {
                doc.Load(await response.Content.ReadAsStreamAsync(), encoding);
            });

            return doc;
        }

        public async Task RandomDelay()
        {
            await RandomDelay(DelayInterval);
        }

        public static async Task RandomDelay(TimeSpan delay)
        {
            var ms = new Random().Next((int)(delay.TotalMilliseconds * 0.5), (int)(delay.TotalMilliseconds * 1.5));
            await Task.Delay(ms);
        }
    }
}
