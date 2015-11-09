namespace PonyProxy.SuperCache
{
    using System.Net.Http.Headers;
    using System.Runtime.Serialization;
    using System.Threading;

    [DataContract]
    internal sealed class OfflineEntry
    {
        private ManualResetEvent readyEvent;

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Headers { get; set; }

        [DataMember]
        public string ContentType { get; set; }

        public byte[] Content { get; set; }

        public System.Guid RequestId { get; set; }

        internal ManualResetEvent ReadyEvent
        {
            get { return readyEvent ?? (readyEvent = new ManualResetEvent(false)); }
        }
    }
}
