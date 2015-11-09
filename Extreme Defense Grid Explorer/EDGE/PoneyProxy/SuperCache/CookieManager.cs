namespace PonyProxy.SuperCache
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Newtonsoft.Json;

    using PonyProxy.Diagnostics;

    using Windows.Storage;
    using Windows.Storage.Streams;

    internal class CookieManager
    {
        private const string SetCookieHeader = "Set-Cookie";
        private const string CookieCacheFile = "cookie-cache";

        private Uri proxyUri;
        private CookieContainer knownCookies = new CookieContainer();
        private Dictionary<string, Dictionary<string, Cookie>> persistentCookies = new Dictionary<string, Dictionary<string, Cookie>>();
        private ReaderWriterLockSlim persistentCookiesLock = new ReaderWriterLockSlim();
        private ReaderWriterLockSlim knownCookiesLock = new ReaderWriterLockSlim();
        private AutoResetEvent flushCookies = new AutoResetEvent(false);

        public CookieManager(Uri proxyUri)
        {
            this.proxyUri = proxyUri;

            // start a background task to persist cookies to storage
            var t = Task.Run(async () =>
            {
                var activityId = Guid.NewGuid();
                while (true)
                {
                    flushCookies.WaitOne();
                    await PersistCookiesAsync(activityId);
                }
            });
        }

        /// <summary>
        /// Processes Set-Cookie headers in the response message.
        /// </summary>
        /// <remarks>
        /// Rewrites the domain attribute of Set-Cookie headers to point them at the local web server; 
        /// otherwise, the web browser will not send the cookies when communicating with the local web 
        /// server.
        /// This method also keeps track of every cookie and its domain in a known cookie table. This   
        /// information is persisted to isolated storage and used to filter cookies before they are forwarded 
        /// to the target site since, as far as the browser is concerned, all cookies originate from the 
        /// local web server and therefore the browser will include *all* cookies with every request.
        /// </remarks>
        /// <param name="requestId">The request ID.</param>
        /// <param name="response">The HttpResponseMessage for which to process cookies.</param>
        /// <returns>A string containing the Set-Cookie headers after the domain is rewritten.</returns>
        public string ProcessResponseCookies(Guid requestId, HttpResponseMessage response)
        {
            var setCookieHeaders = new StringBuilder();

            if (response != null)
            {
                var requestUri = response.RequestMessage.RequestUri;

                IEnumerable<string> cookieHeaders;
                if (response.Headers.TryGetValues(SetCookieHeader, out cookieHeaders))
                {
                    Trace.Verbose(requestId, "Processing response cookies from: {0}\r\n    {1}", requestUri, string.Join("\r\n    ", cookieHeaders));

                    response.Headers.Remove(SetCookieHeader);

                    // use a CookieContainer to parse Set-Cookie headers
                    var cookieParser = new CookieContainer();
                    foreach (var header in cookieHeaders)
                    {
                        try
                        {
                            const string DomainAttribute = "domain=";
                            var cookieCrumb = header;
                            var index = header.IndexOf(DomainAttribute, StringComparison.OrdinalIgnoreCase);
                            if (index > 0)
                            {
                                index += DomainAttribute.Length;
                                if (header[index] != '.')
                                {
                                    cookieCrumb = header.Insert(index, ".");
                                }
                            }
                            
                            cookieParser.SetCookies(requestUri, cookieCrumb);
                        }
                        catch (Exception ex)
                        {
                            Trace.Exception(Guid.NewGuid(), ex);
                        }
                    }

                    foreach (Cookie cookie in cookieParser.GetCookies(requestUri))
                    {
                        // if the cookie is persistent, add to the list to track its domain
                        if (cookie.Expires > DateTime.MinValue)
                        {
                            TrackPersistentCookie(requestUri, cookie);
                        }

                        // rewrite cookie domain for sending back to proxy client
                        setCookieHeaders.AppendFormat("{0}: {1}\r\n", SetCookieHeader, GetHttpHeader(cookie, proxyUri.Host));

                        // add to known cookies
                        knownCookiesLock.EnterWriteLock();
                        try
                        {
                            knownCookies.Add(requestUri, cookie);
                        }
                        finally
                        {
                            knownCookiesLock.ExitWriteLock();
                        }
                    }

                    // signal background task to write persistent cookies to storage
                    flushCookies.Set();
                }
            }

            return setCookieHeaders.ToString();
        }

        public async Task PersistCookiesAsync(Guid requestId)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(CookieCacheFile, CreationCollisionOption.ReplaceExisting);
            using (var fs = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var stream = fs.GetOutputStreamAt(0))
                {
                    using (var dataWriter = new DataWriter(stream))
                    {
                        int count = 0;
                        string data;

                        persistentCookiesLock.EnterReadLock();
                        try
                        {
                            data = JsonConvert.SerializeObject(persistentCookies, Formatting.Indented);
                            count = persistentCookies.Count;
                        }
                        finally
                        {
                            persistentCookiesLock.ExitReadLock();
                        }

                        dataWriter.WriteString(data);
                        await dataWriter.StoreAsync();
                        dataWriter.DetachStream();

                        Trace.Verbose(requestId, "Persisted {0} cookies: {1}\r\n", count, data);
                    }

                    await stream.FlushAsync();
                }
            }
        }

        public async Task LoadCookiesAsync(Guid requestId)
        {
            string data = null;
            var cookieContainer = new CookieContainer();

            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(CookieCacheFile, CreationCollisionOption.OpenIfExists);
            using (var fs = await file.OpenAsync(FileAccessMode.Read))
            {
                using (var inStream = fs.GetInputStreamAt(0))
                {
                    using (var dataReader = new DataReader(inStream))
                    {
                        await dataReader.LoadAsync((uint)fs.Size);
                        data = dataReader.ReadString((uint)fs.Size);
                        dataReader.DetachStream();

                        var cookies = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Cookie>>>(data);
                        if (cookies != null)
                        {
                            foreach (var cookieSet in cookies)
                            {
                                foreach (var cookie in cookieSet.Value)
                                {
                                    cookieContainer.Add(new Uri(cookie.Key), cookie.Value);
                                }
                            }
                        }

                        knownCookies = cookieContainer;
                        persistentCookies = cookies ?? new Dictionary<string, Dictionary<string, Cookie>>();
                    }
                }
            }

            Trace.Verbose(requestId, "Loaded {0} cookies: {1}\r\n", cookieContainer.Count, data);
        }

        public string FilterCookiesForCurrentRequest(Uri requestUri, string cookieHeader)
        {
            var filteredCookies = new StringBuilder();

            CookieCollection cookiesForUri = null;
            knownCookiesLock.EnterReadLock();
            try
            {
                cookiesForUri = knownCookies.GetCookies(requestUri);
            }
            finally
            {
                knownCookiesLock.ExitReadLock();
            }

            var requestCookies = cookieHeader.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cookieCrumb in requestCookies)
            {
                var cookieParts = cookieCrumb.Split(new[] { '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (cookieParts.Length > 0)
                {
                    var cookieName = cookieParts[0].Trim();
                    if (!string.IsNullOrEmpty(cookieName))
                    {
                        var cookie = cookiesForUri[cookieName];
                        if (cookie == null)
                        {
                            var cookieSet = GetCookies(cookieName);
                            if (!cookieSet.Values.Any(p =>
                            {
                                var cookieDomain = (p.Domain[0] == '.' ? string.Empty : ".") + p.Domain;
                                return requestUri.Host.EndsWith(cookieDomain);
                            }))
                            {
                                continue;
                            }
                        }
                    }
                }

                if (filteredCookies.Length > 0)
                {
                    filteredCookies.Append("; ");
                }

                filteredCookies.Append(cookieCrumb);
            }

            return filteredCookies.ToString();
        }

        private static string GetHttpHeader(Cookie cookie, string domainOverride = null)
        {
            var cookieHeader = new StringBuilder(1024);

            cookieHeader.AppendFormat("{0}={1}", cookie.Name, cookie.Value);

            // Workaround for bug in Cookie.ToString() which fails to includes the path
            // and domain attributes unless you temporarily set the cookie version to 1.
            if (cookie.Version == 0)
            {
                cookie.Version = 1;
                cookie.Version = 0;
            }

            var cookieString = cookie.ToString();

            if (cookieString.Contains("$Domain=") && !string.IsNullOrWhiteSpace(cookie.Domain))
            {
                cookieHeader.AppendFormat("; Domain={0}", domainOverride ?? cookie.Domain);
            }

            if (!string.IsNullOrWhiteSpace(cookie.Path))
            {
                cookieHeader.AppendFormat("; Path={0}", cookie.Path);
            }

            if (cookie.Expires != DateTime.MinValue)
            {
                var utcExpires = cookie.Expires.Kind == DateTimeKind.Local ? cookie.Expires.ToUniversalTime() : cookie.Expires;
                cookieHeader.AppendFormat("; Expires={0:R}", utcExpires);
            }

            if (!string.IsNullOrWhiteSpace(cookie.Comment))
            {
                cookieHeader.AppendFormat("; Comment={0}", cookie.Comment);
            }

            if (cookie.CommentUri != null)
            {
                cookieHeader.AppendFormat("; CommentURL={0}", cookie.CommentUri);
            }

            if (cookie.HttpOnly)
            {
                cookieHeader.AppendFormat("; HttpOnly");
            }

            if (cookie.Secure && domainOverride == null)
            {
                cookieHeader.AppendFormat("; Secure");
            }

            if (cookie.Discard)
            {
                cookieHeader.AppendFormat("; Discard");
            }

            if (!string.IsNullOrWhiteSpace(cookie.Port))
            {
                cookieHeader.AppendFormat("; Port={0}", cookie.Port);
            }

            if (cookie.Version != 0)
            {
                cookieHeader.AppendFormat("; Version={0}", cookie.Version);
            }

            return cookieHeader.ToString();
        }

        private Dictionary<string, Cookie> GetCookies(string cookieName)
        {
            persistentCookiesLock.EnterUpgradeableReadLock();
            try
            {
                Dictionary<string, Cookie> cookieSet;
                if (!persistentCookies.TryGetValue(cookieName, out cookieSet))
                {
                    cookieSet = new Dictionary<string, Cookie>();
                    persistentCookiesLock.EnterWriteLock();
                    try
                    {
                        persistentCookies[cookieName] = cookieSet;
                    }
                    finally
                    {
                        persistentCookiesLock.ExitWriteLock();
                    }
                }

                return cookieSet;
            }
            finally
            {
                persistentCookiesLock.ExitUpgradeableReadLock();
            }
        }

        private void TrackPersistentCookie(Uri requestUri, Cookie cookie)
        {
            var cookieKey = requestUri.GetComponents(
                UriComponents.KeepDelimiter | UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.Unescaped);

            persistentCookiesLock.EnterUpgradeableReadLock();
            try
            {
                var key = cookie.Name;
                Dictionary<string, Cookie> cookieSet = null;
                bool isEmpty = !persistentCookies.TryGetValue(key, out cookieSet);

                if (isEmpty)
                {
                    cookieSet = new Dictionary<string, Cookie>();
                }

                persistentCookiesLock.EnterWriteLock();
                try
                {
                    cookieSet[cookieKey] = cookie;

                    if (isEmpty)
                    {
                        persistentCookies.Add(key, cookieSet);
                    }
                }
                finally
                {
                    persistentCookiesLock.ExitWriteLock();
                }
            }
            finally
            {
                persistentCookiesLock.ExitUpgradeableReadLock();
            }
        }
    }
}
