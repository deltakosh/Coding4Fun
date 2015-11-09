namespace PonyProxy.SuperCache
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
#if SILVERLIGHT
    using Microsoft.Phone.Net.NetworkInformation;
#endif
    using Newtonsoft.Json;
    using PonyProxy.Diagnostics;
    using Windows.Networking.Connectivity;
    using Windows.Storage;
    using Windows.Storage.Streams;

    internal sealed class OfflineRequestResolver : PassThroughRequestResolver
    {
        private const string CacheFolderName = "pony-cache";
        private const string CacheIndexFileName = "pony-cache.index";

        private Dictionary<string, OfflineEntry> cacheIndex;
        private StorageQueue<OfflineEntry> storageQueue = new StorageQueue<OfflineEntry>(100);
        private bool isInternetAvailable = true;

        public OfflineRequestResolver()
        {
            // start a background task to cache HTTP responses to storage
            var t = Task.Run(async () => { await SaveCacheEntriesAsync(); });

            // handle network status changes
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            OnNetworkStatusChanged(this);
        }

        public override async Task<HttpResponseMessage> ResolveRequestAsync(HttpRequestMessage request, Guid requestId)
        {
            if (cacheIndex == null)
            {
                await RestoreCacheIndexAsync(requestId);
            }

            var entry = GetCacheEntry(request.RequestUri, requestId);

            if (isInternetAvailable)
            {
                // request content from target site
                var response = await base.ResolveRequestAsync(request, requestId);

                // store retrieved content for offline use
                if (response.IsSuccessStatusCode)
                {
                    entry.ReadyEvent.Reset();
                    entry.ContentType = response.Content.Headers.ContentType.ToString();
                    entry.Content = await response.Content.ReadAsByteArrayAsync();
                    storageQueue.Enqueue(entry);
                }

                return response;
            }

            if (entry.ReadyEvent.WaitOne(5000))
            {
                if (entry.Path != null)
                {
                    Trace.Information(requestId, "URL: {0} - Retrieving cached content from path '{1}'", request.RequestUri, entry.Path);

                    // Wait for the cache file to be ready
                    // Create new cached response 
                    var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);
                    var file = await cacheFolder.GetFileAsync(entry.Path);
                    var cachedResponse = await RetrieveOfflineResponseMessageAsync(request, entry, file);

                    return cachedResponse;
                }
            }

            return null;
        }

        private static async Task WriteBytesAsync(StorageFile file, byte[] data)
        {
            using (var fs = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var outStream = fs.GetOutputStreamAt(0))
                {
                    using (var dataWriter = new DataWriter(outStream))
                    {
                        dataWriter.WriteBytes(data);
                        await dataWriter.StoreAsync();
                        dataWriter.DetachStream();
                    }

                    await outStream.FlushAsync();
                }
            }
        }

        private static async Task WriteTextAsync(StorageFile file, string data)
        {
            using (var fs = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var outStream = fs.GetOutputStreamAt(0))
                {
                    using (var dataWriter = new DataWriter(outStream))
                    {
                        dataWriter.WriteString(data);
                        await dataWriter.StoreAsync();
                        dataWriter.DetachStream();
                    }

                    await outStream.FlushAsync();
                }
            }
        }

        private static async Task<byte[]> ReadBytesAsync(StorageFile file)
        {
            using (var fs = await file.OpenAsync(FileAccessMode.Read))
            {
                using (var inStream = fs.GetInputStreamAt(0))
                {
                    using (var dataReader = new DataReader(inStream))
                    {
                        await dataReader.LoadAsync((uint)fs.Size);
                        var data = new byte[dataReader.UnconsumedBufferLength];
                        dataReader.ReadBytes(data);
                        dataReader.DetachStream();
                        return data;
                    }
                }
            }
        }

        private static async Task<HttpResponseMessage> RetrieveOfflineResponseMessageAsync(HttpRequestMessage request, OfflineEntry entry, StorageFile file)
        {
            var cachedByteArray = await ReadBytesAsync(file);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.RequestMessage = request;
            response.Content = new ByteArrayContent(cachedByteArray);
            
            MediaTypeHeaderValue contentType;
            if (MediaTypeHeaderValue.TryParse(entry.ContentType, out contentType))
            {
                response.Content.Headers.ContentType = contentType;
            }            

            return response;
        }

#if SILVERLIGHT
        private void OnNetworkStatusChanged(object sender)
        {
            this.isInternetAvailable = NetworkInterface.NetworkInterfaceType != NetworkInterfaceType.None;
            Trace.Information(Guid.Empty, "Networks status change: {0}", this.isInternetAvailable ? "ONLINE" : "OFFLINE");
        }
#else
        private void OnNetworkStatusChanged(object sender)
        {
            ConnectionProfile profile = NetworkInformation.GetInternetConnectionProfile();

            if (profile != null)
            {
                var connectivityLevel = profile.GetNetworkConnectivityLevel();

                // TODO: decide whether ConstrainedAccess and LocalAccess should  
                // also be taken as an indication that a network is available - probably not for most apps
                isInternetAvailable = connectivityLevel == NetworkConnectivityLevel.InternetAccess;
            }
            else
            {
                isInternetAvailable = false;
            }

            Trace.Information(Guid.Empty, "Networks status change: {0}", isInternetAvailable ? "ONLINE" : "OFFLINE");
        }
#endif

        private async Task SaveCacheIndexAsync()
        {
            if (cacheIndex != null)
            {
                var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);
                var file = await cacheFolder.CreateFileAsync(CacheIndexFileName, CreationCollisionOption.ReplaceExisting);
                var data = JsonConvert.SerializeObject(cacheIndex);
                await WriteTextAsync(file, data);
            }
        }

        private async Task RestoreCacheIndexAsync(Guid requestId)
        {
            Trace.Verbose(requestId, "Restoring cache index...");
            var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);
            var file = await cacheFolder.CreateFileAsync(CacheIndexFileName, CreationCollisionOption.OpenIfExists);
            var data = await file.ReadTextAsync();
            cacheIndex = JsonConvert.DeserializeObject<Dictionary<string, OfflineEntry>>(data);
            if (cacheIndex == null)
            {
                cacheIndex = new Dictionary<string, OfflineEntry>();
            }
            else
            {
                // Set all files as ready
                foreach (var e in cacheIndex.Values)
                {
                    e.ReadyEvent.Set();
                }
            }
        }

        private async Task SaveCacheEntriesAsync()
        {
            var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);

            while (true)
            {
                Guid requestId = Guid.Empty;

                try
                {
                    var entry = storageQueue.Dequeue();
                    requestId = entry.RequestId;

                    if (entry.Content != null && entry.Content.Length > 0)
                    {
                        var path = entry.Path ?? Guid.NewGuid().ToString();
                        var file = await cacheFolder.CreateFileAsync(path, CreationCollisionOption.ReplaceExisting);
                        await WriteBytesAsync(file, entry.Content);
                        entry.Content = null;
                        entry.Path = path;
                        entry.ReadyEvent.Set();

                        if (!cacheIndex.ContainsKey(entry.Key))
                        {
                            cacheIndex.Add(entry.Key, entry);
                        }

                        Trace.Information(requestId, "URL: {0} - Cached content at '{1}'", entry.Key, entry.Path);
                    }

                    if (storageQueue.Count == 0)
                    {
                        Trace.Verbose(requestId, "Flushing cache index...");
                        await SaveCacheIndexAsync();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.Exception(requestId, ex);
                }
            }
        }

        private OfflineEntry GetCacheEntry(Uri uri, Guid requestId)
        {
            var filteredQueryParameters = uri.ParseQueryString()
                .Where(p => p.Key != "_")

                // Sanitize empty values (query string wo value)
                .Select(p => p.Key + (p.Value != null ? "=" : string.Empty) + (string.IsNullOrEmpty(p.Value) ? string.Empty : p.Value));

            var mappedUri = new UriBuilder(uri);
            mappedUri.Query = string.Join("&", filteredQueryParameters);

            // TODO: review if trimming trailing slash is correct
            var flatString = mappedUri.Uri.ToString().TrimEnd('/');

            OfflineEntry entry = null;
            lock (cacheIndex)
            {
                if (!cacheIndex.TryGetValue(flatString, out entry))
                {
                    entry = new OfflineEntry { RequestId = requestId, Key = flatString };
                }
            }

            return entry;
        }
    }
}
