namespace PonyProxy.SuperCache
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;

    public sealed class SendingRequestEventArgs
    {
        private HttpRequestMessage request;

        internal SendingRequestEventArgs(HttpRequestMessage request)
        {
            this.request = request;
        }

        public Uri RequestUri
        {
            get
            {
                return request.RequestUri;
            }

            set
            {
                request.RequestUri = value;
            }
        }

        public byte[] ContentAsByteArray
        {
            get
            {
                if (request.Content != null)
                {
                    return request.Content.ReadAsByteArrayAsync().Result;
                }

                return new byte[0];
            }

            set
            {
                var content = new ByteArrayContent(value);
                request.Content = InitializeHttpHeaders(content);
            }
        }

        public string ContentAsString
        {
            get
            {
                if (request.Content != null)
                {
                    return request.Content.ReadAsStringAsync().Result;
                }

                return string.Empty;
            }

            set
            {
                var content = new StringContent(value);
                request.Content = InitializeHttpHeaders(content);
            }
        }

        public string Method
        {
            get
            {
                return request.Method.Method;
            }

            set
            {
                request.Method = new HttpMethod(value);
            }
        }

        public IEnumerable<KeyValuePair<string, IEnumerable<string>>> Headers
        {
            get
            {
                return request.Headers;
            }
        }

        private HttpContent InitializeHttpHeaders(HttpContent content)
        {
            HttpContentHeaders currentHeaders = null;
            if (request.Content.Headers != null)
            {
                currentHeaders = request.Content.Headers;
            }

            var headers = content.Headers;
            foreach (var header in currentHeaders)
            {
                if (string.Compare(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    if (headers.Contains(header.Key))
                    {
                        headers.Remove(header.Key);
                    }

                    headers.Add(header.Key, header.Value);
                }
            }

            return content;
        }
    }
}
