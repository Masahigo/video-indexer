using System.Text.Json.Serialization;

namespace VideoIndexerApi.Models
{
    // Reference: https://github.com/cloudevents/spec/tree/v1.0-rc1 
    // See also: https://docs.microsoft.com/en-us/azure/event-grid/cloudevents-schema

    public class CloudEvent<T> where T : class
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }

        [JsonPropertyName("specversion")]
        public string SpecVersion { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("subject")]
        public string Subject { get; set; }

        [JsonPropertyName("time")]
        public string Time { get; set; }

        [JsonPropertyName("data")]
        public T Data { get; set; }
    }
}