using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jacere.Submittable
{
    public class Context
    {
        private readonly string _baseUrl;
        private string _sessionCookie;

        public Context(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public void Login(string user, string password)
        {
            var request = WebRequest.Create($"{_baseUrl}/account/ajaxlogin");
            request.Method = "POST";
            request.ContentType = "application/json";
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(JsonConvert.SerializeObject(new
                {
                    UserName = user,
                    Password = password,
                }));
            }

            using (var response = request.GetResponse())
            {
                _sessionCookie = response.Headers["Set-Cookie"];
            }
        }

        public HtmlDocument GetSubmissionPage(int categoryId)
        {
            return GetHtmlDocument($"{_baseUrl}/submit/{categoryId}");
        }

        public int EditCategory(object data)
        {
            var request = CreateHttpRequestWithCookie($"{_baseUrl}/categories/edit");
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(GetFormUrlEncoded(data));
            }

            var response = request.GetResponse();
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return int.Parse(reader.ReadToEnd());
            }
        }

        public void Submit(int categoryId, IEnumerable<KeyValuePair<string, string>> data)
        {
            var boundary = "----WebKitFormBoundaryIBTW73IE8eiDP8ux";

            var request = CreateHttpRequestWithCookie($"{_baseUrl}/submit/{categoryId}/submission");
            request.Method = "POST";
            request.ContentType = $"multipart/form-data; boundary={boundary}";
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(GetMultipartFormDataEncoded(data, boundary));
            }
            
            using (var response = request.GetResponse())
            {
                // ignore
            }
        }

        public void SaveProductPrices(int id, object data)
        {
            PostJsonIgnoreResponse($"{_baseUrl}/categories/saveproductprices/{id}", data);
        }

        public void SavePaymentAddons(int id, object data)
        {
            PostJsonIgnoreResponse($"{_baseUrl}/categories/savepaymentaddons/{id}", data);
        }

        public void SaveForm(int id, object data)
        {
            PostJsonIgnoreResponse($"{_baseUrl}/categories/saveform/{id}", data);
        }

        public void SaveReviewForm(int id, object data)
        {
            PostJsonIgnoreResponse($"{_baseUrl}/categories/savereviewform/{id}", data);
        }

        private void PostJsonIgnoreResponse(string url, object data)
        {
            var request = CreateHttpRequestWithCookie(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            using (var streamWriter = new StreamWriter(request.GetRequestStream()))
            {
                streamWriter.Write(JsonConvert.SerializeObject(data));
            }

            using (request.GetResponse())
            {
                // doesn't matter
            }
        }

        public HtmlDocument GetHtmlDocument(string url)
        {
            var request = CreateHttpRequestWithCookie(url);
            using (var response = request.GetResponse())
            {
                var doc = new HtmlDocument();
                HtmlNode.ElementsFlags.Remove("form");
                doc.Load(response.GetResponseStream());
                return doc;
            }
        }

        private HttpWebRequest CreateHttpRequestWithCookie(string url)
        {
            var request = WebRequest.Create(url);

            if (_sessionCookie == null)
            {
                throw new InvalidOperationException("no session");
            }

            request.Headers["Cookie"] = _sessionCookie;

            return (HttpWebRequest)request;
        }

        private static IEnumerable<KeyValuePair<string, object>> GetProperties(object obj)
        {
            if (obj is JObject)
            {
                return (obj as JObject)
                    .Properties()
                    .Select(x => new KeyValuePair<string, object>(x.Name, x.Value));
            }
            return obj
                .GetType()
                .GetProperties()
                .Select(x => new KeyValuePair<string, object>(x.Name, x.GetValue(obj)));
        }

        private static string GetFormUrlEncoded(object data)
        {
            return string.Join("&",
                GetProperties(data).Select(x => $"{Encode(x.Key)}={Encode(Convert.ToString(x.Value))}"));
        }

        private static string GetMultipartFormDataEncoded(IEnumerable<KeyValuePair<string, string>> data, string boundary)
        {
            var parts = data.Select(x => string.Format(
                "{0}Content-Disposition: form-data; name=\"{1}\"{0}{0}{2}{0}",
                "\r\n", x.Key, x.Value)).ToList();
            parts.Insert(0, "");
            parts.Add("--");
            return string.Join($"--{boundary}", parts);
        }

        private static string Encode(string data)
        {
            return string.IsNullOrEmpty(data)
                ? string.Empty
                : Uri.EscapeDataString(data).Replace("%20", "+");
        }
    }
}
