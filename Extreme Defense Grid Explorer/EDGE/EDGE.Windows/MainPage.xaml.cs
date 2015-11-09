using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using PonyProxy;
using PonyProxy.Diagnostics;
using PonyProxy.SuperCache;
using PonyProxy.SuperCache.Config;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace EDGE
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage
    {
        private SuperCacheConfig config;
        public MainPage()
        {
            InitializeComponent();
        }

        private async void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            await SuperCacheManager.StopAsync();
        }

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            config = new SuperCacheConfig
            {
                WhiteList = new[] { "www.wikipedia.com", "www.toysrus.com", "blogs.msdn.com", "www.catuhe.com" },
                Mode = DefenseMode.PoneyAugmentedProtectionAnalyzer
            };
        }

        private async void InitiateAsync()
        {
            try
            {
                webView.Focus(FocusState.Programmatic);

                urlText.Select(0, 0);

                if (!urlText.Text.StartsWith("http"))
                {
                    urlText.Text = "http://" + urlText.Text;
                }

                var uri = new Uri(urlText.Text);
                await SuperCacheManager.StopAsync();

                await SuperCacheManager.StartAsync(uri, config);
                var target = SuperCacheManager.BuildLocalProxyUri(uri, urlText.Text);

                Navigate(new Uri(target));
            }
            catch
            {
            }
        }

        private void GoButton_OnClick(object sender, RoutedEventArgs e)
        {
            InitiateAsync();
        }

        void Navigate(Uri uri)
        {
            waitRing.IsActive = true;
            webView.Navigate(uri);
        }

        private void WebView_OnNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs e)
        {
            if (config.WhiteList != null && config.Mode == DefenseMode.WhiteListOnly)
            {
                var root = SuperCacheManager.ResolveTargetUri(e.Uri.ToString()).Authority;

                if (config.WhiteList.Count(u => string.Compare(root, u, StringComparison.CurrentCultureIgnoreCase) == 0) == 0)
                {
                    e.Cancel = true;
                    Navigate(new Uri("http://www.catuhe.com/msdn/notAllowedSite/"));
                    return;
                }
            }

            var args = new NavigatingEventArgs(e.Uri.ToString());
            if (SuperCacheManager.OnNavigating(args))
            {
                e.Cancel = true;
                Navigate(args.TargetUri);
            }
        }

        private void webView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            logoImage.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
            waitRing.IsActive = false;

            if (args.IsSuccess)
            {
                urlText.Text = SuperCacheManager.ResolveTargetUri(args.Uri.ToString()).ToString();
            }
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            webView.GoBack();
        }

        private void urlText_GotFocus(object sender, RoutedEventArgs e)
        {
            urlText.SelectAll();
        }

        private void urlText_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                InitiateAsync();
            }
        }
    }
}
