using System.Threading.Tasks;

namespace PonyProxy
{
    using System;
    using PonyProxy.SuperCache;
    using PonyProxy.SuperCache.Config;
    using Windows.Foundation;
    
    public static class SuperCacheManager
    {
        private static WebServer webServer;

        public static event EventHandler<SendingRequestEventArgs> SendingRequest
        {
            add
            {

#if SILVERLIGHT
                webServer.SendingRequestRegistrationTokenTable.AddEventHandler(value);
#else
                return webServer.SendingRequestRegistrationTokenTable.AddEventHandler(value);
#endif
            }

            remove
            {
                webServer.SendingRequestRegistrationTokenTable.RemoveEventHandler(value);
            }
        }

        public static event EventHandler<ResponseReceivedEventArgs> TextResponseReceived
        {
            add
            {

#if SILVERLIGHT
                webServer.TextResponseReceivedRegistrationTokenTable.AddEventHandler(value);
#else
                return webServer.TextResponseReceivedRegistrationTokenTable.AddEventHandler(value);
#endif
            }

            remove
            {
                webServer.TextResponseReceivedRegistrationTokenTable.RemoveEventHandler(value);
            }
        }

        public static event EventHandler<OfflinePageUnavailableEventArgs> OfflinePageUnavailable
        {
            add
            {

#if SILVERLIGHT
                webServer.OfflinePageUnavailableRegistrationTokenTable.AddEventHandler(value);
#else
                return webServer.OfflinePageUnavailableRegistrationTokenTable.AddEventHandler(value);
#endif
            }

            remove
            {
                webServer.OfflinePageUnavailableRegistrationTokenTable.RemoveEventHandler(value);
            }
        }

        public static IAsyncAction StartAsync(Uri baseUri, SuperCacheConfig configuration)
        {
            webServer = new WebServer();
            return webServer.StartAsync(baseUri, configuration);
        }

        public static IAsyncAction StopAsync()
        {
            if (webServer == null)
            {
                return Task.Delay(0).AsAsyncAction();
            }
            return webServer.StopAsync();
        }

        public static bool OnNavigating(NavigatingEventArgs e)
        {
            return webServer.OnNavigating(e);
        }

        public static string BuildLocalProxyUri(Uri baseUri, string requestUri)
        {
            if (webServer == null)
            {
                return new Uri(baseUri, requestUri).ToString();
            }

            return webServer.BuildCurrentProxyUri(baseUri, requestUri);
        }

        public static Uri ResolveTargetUri(string requestUri)
        {
            if (webServer == null)
            {
                return new Uri(requestUri, UriKind.RelativeOrAbsolute);
            }

            return webServer.ResolveTargetUri(requestUri);
        }

        public static void AddPreloadScript(string script)
        {
            webServer.PreloadScripts.Add(new PreloadScript(script));
        }
        
    }
}