using System.Text.Json.Serialization;

namespace VideoIndexerApi.Models
{
    public class VideoIndex<T> where T : class
    {
        [JsonPropertyName("id")]
        public string VideoId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("created")]
        public string Created { get; set; }

       [JsonPropertyName("durationInSeconds")]
       public int DurationInSeconds { get; set; }

       [JsonPropertyName("summarizedInsights")]
       public T Summary { get; set; }
    }
}