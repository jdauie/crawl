using System;
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
        
        public ConsoleProgress Progress { get; set; }

        protected DataContext(Uri baseUri)
        {
            _baseUri = baseUri;
        }

        protected virtual void PopulateClientRequestHeaders(HttpRequestHeaders headers)
        {
        }

        public DataContext SetProgress(ConsoleProgress progress)
        {
            Progress = progress;
            return this;
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

        public async Task<HtmlDocument> GetHtmlDocument(string url, Encoding encoding)
        {
            var doc = new HtmlDocument();

            await DoGet(url, async response =>
            {
                doc.Load(await response.Content.ReadAsStreamAsync(), encoding);
            });

            return doc;
        }

        public static async Task RandomDelay(TimeSpan delay)
        {
            var ms = new Random().Next((int)(delay.TotalMilliseconds * 0.5), (int)(delay.TotalMilliseconds * 1.5));
            await Task.Delay(ms);
        }
    }
}
