using Newtonsoft.Json;

namespace VideoIndexerApi.Models
{
    // Reference: https://github.com/cloudevents/spec/tree/v1.0-rc1 
    // See also: https://docs.microsoft.com/en-us/azure/event-grid/cloudevents-schema

    public class CloudEvent<T> where T : class
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("specversion")]
        public string SpecVersion { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("time")]
        public string Time { get; set; }

        [JsonProperty("data")]
        public T Data { get; set; }
    }
}