using Newtonsoft.Json;

namespace VideoIndexerApi.Models
{
    public class VideoIndex<T> where T : class
    {
        [JsonProperty("id")]
        public string VideoId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("created")]
        public string Created { get; set; }

       [JsonProperty("durationInSeconds")]
       public int DurationInSeconds { get; set; }

       [JsonProperty("summarizedInsights")]
       public T Summary { get; set; }
    }
}