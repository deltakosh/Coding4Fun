namespace PonyProxy.SuperCache
{
    using System;

    public sealed class OfflinePageUnavailableEventArgs
    {
        public OfflinePageUnavailableEventArgs(Uri requestUri)
        {
            RequestUri = requestUri;
        }

        public Uri RequestUri { get; private set; }
    }
}
