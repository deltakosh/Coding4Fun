namespace PonyProxy.SuperCache
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using HtmlAgilityPack;
    using Windows.Storage;
    using System.Threading.Tasks;

    internal class HtmlProcessor : IEqualityComparer<string>
    {
        static string[] offendingWords;
        private WebServer server;
        private Uri baseUri;
        private HtmlDocument document;


        static async Task<string[]> GetOffendingWords()
        {
            if (offendingWords == null)
            {
                var uri = new Uri("ms-appx:///PonyProxy/SuperCache/Assets/slangs.txt");
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                var fileContent = await file.ReadTextAsync();
                offendingWords = fileContent.Replace("\n", "").Split('\r').Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }

            return offendingWords;
        }

        public HtmlProcessor(string html, WebServer server, Uri baseUri)
        {
            this.server = server;
            this.baseUri = baseUri;

            document = new HtmlDocument();
            document.LoadHtml(html);
        }

        public void RemoveFlashAndObjects()
        {
            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.Descendants()
                    .Where(p =>
                    {
                        if (p.Name == "object" || p.Name == "iframe")
                        {
                            return true;
                        }

                        return false;
                    }).ToArray();

                var random = new Random((int)DateTime.Now.Ticks);
                foreach (var element in nodes)
                {
                    element.Remove();
                }
            }
        }

        public async Task PonyfyTextsAsync()
        {
            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.Descendants()
                    .Where(p =>
                    {
                        if (p.Name == "#text")
                        {
                            return true;
                        }

                        return false;
                    });

                foreach (var element in nodes)
                {
                    var content = element.InnerHtml;
                    var offendings = await GetOffendingWords();

                    element.InnerHtml = string.Join(" ", content.Split().Where(w => !offendings.Contains(w, this)).ToArray());
                }
            }
        }

        public void ChangeVideos()
        {
            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.Descendants()
                    .Where(p =>
                    {
                        if (p.Name == "video" && p.GetAttributeValue("src", null) != null)
                        {
                            return true;
                        }

                        return false;
                    });

                foreach (var element in nodes)
                {
                    element.Attributes["src"].Value = "http://www.catuhe.com/msdn/notAllowedSite/pony/pony.mp4";
                }
            }
        }

        public void ChangeImages()
        {
            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.Descendants()
                    .Where(p =>
                    {
                        if (p.Name == "img" && p.GetAttributeValue("src", null) != null)
                        {
                            return true;
                        }

                        return false;
                    });

                var random = new Random((int)DateTime.Now.Ticks);
                foreach (var element in nodes)
                {
                    element.Attributes["src"].Value = string.Format("http://www.catuhe.com/msdn/notAllowedSite/pony/{0:00}.jpg", random.Next(0, 15));
                }
            }
        }

        public void RedirectLinks(Uri baseUri)
        {
            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.Descendants()
                    .Where(p =>
                    {
                        if (p.Name == "a" && p.GetAttributeValue("href", null) != null)
                        {
                            return true;
                        }
                        else if (p.Name == "link" && p.GetAttributeValue("rel", null) == "stylesheet")
                        {
                            return true;
                        }
                        else if ((p.Name == "script" || p.Name == "img" || p.Name == "iframe") && p.GetAttributeValue("src", null) != null)
                        {
                            return true;
                        }
                        else if (p.Name == "form" && p.GetAttributeValue("action", null) != null)
                        {
                            return true;
                        }

                        return false;
                    });

                foreach (var element in nodes)
                {
                    var attributeName = element.Name == "a" || element.Name == "link" ? "href" : (element.Name == "form" ? "action" : "src");
                    var attribute = element.GetAttributeValue(attributeName, null);
                    if (attribute != null)
                    {
                        var linkUrl = server.BuildCurrentProxyUri(baseUri, attribute);
                        if (linkUrl != null)
                        {
                            element.Attributes[attributeName].Value = linkUrl;
                        }
                    }
                }
            }
        }

        public void InjectHtml(string script)
        {
            if (document.DocumentNode != null)
            {
                var head = document.DocumentNode.Descendants().FirstOrDefault(p => p.Name == "head");
                if (head != null)
                {
                    var scriptNode = document.CreateTextNode(script);
                    head.PrependChild(scriptNode);
                }
            }
        }

        public string GetContent()
        {
            string result = null;
            using (var writer = new StringWriter())
            {
                document.Save(writer);
                result = writer.ToString();
            }

            return result;
        }

        public bool Equals(string x, string y)
        {
            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase) == 0;
        }

        public int GetHashCode(string obj)
        {
            return obj.GetHashCode();
        }
    }
}