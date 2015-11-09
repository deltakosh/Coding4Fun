namespace PonyProxy.SuperCache.Config
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public enum DefenseMode
    {
        Adult,
        WhiteListOnly,
        NoSlangAnalyzer,
        PoneyAugmentedProtectionAnalyzer,
    }

    [DataContract]
    public sealed class SuperCacheConfig
    {
        internal const string AutomaticProxyUriConfiguration = "Auto";

        public SuperCacheConfig()
        {
            ProxyUri = AutomaticProxyUriConfiguration;
        }

        [DataMember]
        public DefenseMode Mode { get; set; }
        
        [DataMember]
        public bool EnableRedirectWindowOpen { get; set; }

        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public PonyProxy.Diagnostics.TraceLevel TraceLevel { get; set; }

        public string ProxyUri { get; set; }
        

        [DataMember]
        public IList<string> WhiteList { get; set; }

    }
}
