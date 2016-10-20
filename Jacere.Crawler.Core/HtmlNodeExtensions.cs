using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;

namespace Jacere.Crawler.Core
{
    public static class HtmlNodeExtensions
    {
        public static List<HtmlNode> Select(this HtmlNode node, string path)
        {
            return node.SelectNodes(path)?.ToList() ?? new List<HtmlNode>();
        }

        public static bool Has(this HtmlNode node, string path)
        {
            return node.SelectNodes(path) != null;
        }

        public static string GetAttribute(this HtmlNode node, string attribute)
        {
            return node.GetAttributeValue(attribute, null);
        }

        public static int GetAttributeInt(this HtmlNode node, string attribute)
        {
            return node.GetAttributeValue(attribute, 0);
        }

        public static string GetValue(this HtmlNode node)
        {
            return node.InnerHtml;
        }

        public static int GetValueInt(this HtmlNode node)
        {
            return Convert.ToInt32(node.InnerHtml);
        }
    }
}
