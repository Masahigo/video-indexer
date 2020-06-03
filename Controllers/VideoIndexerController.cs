using Microsoft.AspNetCore.Mvc;
using VideoIndexerApi.Models;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Dapr.Client;
using VideoIndexerApi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs;
using Azure.Identity;
using Azure.Storage.Sas;
using Azure.Storage.Blobs.Models;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;

namespace VideoIndexerApi.Controllers
{
    // Inspiration from: https://github.com/Azure-Samples/azure-event-grid-viewer/blob/master/viewer/Controllers/UpdatesController.cs
    //[ApiController]
    public class VideoIndexerController : Controller
    {
        public const string StateKey = "statestore";

        #region Public Methods

        public VideoIndexerController(IBackgroundTaskQueue queue,
                                   [FromServices] DaprClient daprClient,
                                   ILogger<VideoIndexerController> logger,
                                   IConfiguration configuration)
        {

            _queue = queue;
            _daprClient = daprClient;
            _logger = logger;
            _configuration = configuration;

            apiUrl =  _configuration["VIDEO_API_URL"];
        }

        //[Route("eventgrid-input")]
        [HttpPost]
        public async Task<IActionResult> Post()
        {
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                var jsonContent = await reader.ReadToEndAsync();

                // Check to see if this is passed in using
                // the CloudEvents schema
                if (IsCloudEvent(jsonContent))
                {
                    return await HandleCloudEvent(jsonContent);
                }

                return BadRequest();
            }
        }

        #endregion

        #region Private Methods
        private readonly ILogger _logger;
        private readonly IBackgroundTaskQueue _queue;
        private readonly DaprClient _daprClient;
        private IConfiguration _configuration;

        private string apiKey = Environment.GetEnvironmentVariable("VIDEO_INDEXER_API_KEY");
        private string apiUrl;

        private async Task<IActionResult> HandleCloudEvent(string jsonContent)
        {
            var details = JsonConvert.DeserializeObject<CloudEvent<dynamic>>(jsonContent);
            var eventData = JObject.Parse(jsonContent);

            if (details.Type == "Microsoft.Storage.BlobCreated" &&
               details.Subject.StartsWith("/blobServices/default/containers"))
            {

                var blobPath = details.Subject;
                var fileName = blobPath.Substring(blobPath.LastIndexOf('/') + 1);

                var subPath = blobPath.Replace("/blobServices/default/containers/", "");
                var containerName = subPath.Substring(0, subPath.IndexOf("/blobs/"));

                var state = await this._daprClient.GetStateEntryAsync<string>(StateKey, fileName);
                state.Value ??= (string)string.Empty;
                state.Value += eventData.ToString();
                await state.SaveAsync();

                Console.WriteLine($"New value for cloud event = {state.Value}");

                await ProcessBlobAsync(fileName, containerName);
            }

            return Ok();
        }

        private async Task UploadToCosmosDbAsync(string stateStoreKey)
        {
            _logger.LogInformation($"Uploading video index - key is {stateStoreKey} ..");

            try
            {

                var videoIndexState = await this._daprClient.GetStateEntryAsync<string>(StateKey, stateStoreKey);

                // Attempt to read one JSON object. 
                var partialVideoIndex = JsonConvert.DeserializeObject<VideoIndex<dynamic>>(videoIndexState.Value);

                JObject o = JObject.FromObject(new
                {
                    data = new
                    {
                        key = "id",
                        id = partialVideoIndex.VideoId,
                        name = partialVideoIndex.Name,
                        created = partialVideoIndex.Created,
                        duration = partialVideoIndex.DurationInSeconds,
                        summary = partialVideoIndex.Summary
                    }
                }
                );

                var jsonContent = JsonConvert.SerializeObject(o);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var dapr_port = _configuration["DAPR_HTTP_PORT"];

                using (var httpClient = new HttpClient())
                {
                    var result = await httpClient.PostAsync(
                        string.Format("http://localhost:{0}/v1.0/bindings/cosmosdb-output", dapr_port), content);

                    _logger.LogInformation($"Result: {result}");
                }

                _logger.LogInformation($"Remove video index from statestore.");
                await videoIndexState.DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occured: {ex.Message.ToString()}");
            }

        }

        private async Task ProcessBlobAsync(string stateStoreKey, string containerName)
        {
            _queue.QueueBackgroundWorkItem(async token =>
            {
                var guid = Guid.NewGuid().ToString();
                bool cloudEventProcessed = false;
                string videoId = string.Empty;

                _logger.LogInformation($"Prosessing cloud event - key is {stateStoreKey} ..");

                while (!token.IsCancellationRequested && !cloudEventProcessed)
                {
                    try
                    {
                        // Upload to https://www.videoindexer.ai/

                        var videoUrl = await GenerateSasUrl(containerName, stateStoreKey);
                        videoId = await UploadVideoAndIndex(stateStoreKey, videoUrl);
                        var videoIndex = await GetVideoIndex(stateStoreKey, videoId);

                        var videoIndexState = await this._daprClient.GetStateEntryAsync<string>(StateKey, videoId);
                        videoIndexState.Value ??= (string)string.Empty;
                        videoIndexState.Value += videoIndex;
                        await videoIndexState.SaveAsync();

                        _logger.LogInformation($"New value for video index = {videoIndexState.Value}");
                    }
                    catch (OperationCanceledException)
                    {
                        // Prevent throwing if the Delay is cancelled
                    }

                    _logger.LogInformation($"Remove cloudEvent from statestore.");
                    var cloudEventState = await this._daprClient.GetStateEntryAsync<string>(StateKey, stateStoreKey);
                    await cloudEventState.DeleteAsync();

                    cloudEventProcessed = true;
                }

                // Get the video data and upload to cosmosdb
                await UploadToCosmosDbAsync(videoId);

                _logger.LogInformation(
                        "Queued Background Task {Guid} is complete.", guid);
            });
        }

        private async Task<string> GetVideoIndex(string fileName,
                                               string videoId)
        {
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.ServicePointManager.SecurityProtocol | System.Net.SecurityProtocolType.Tls12;

            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

            // obtain account information and access token
            string queryParams = Helpers.CreateQueryString(
                new Dictionary<string, string>()
                {
                    {"generateAccessTokens", "true"},
                    {"allowEdit", "false"},
                });
            HttpResponseMessage result = await client.GetAsync($"{apiUrl}/auth/trial/Accounts?{queryParams}");
            var json = await result.Content.ReadAsStringAsync();
            var accounts = JsonConvert.DeserializeObject<AccountContractSlim[]>(json);

            // take the relevant account, here we simply take the first, 
            // you can also get the account via accounts.First(account => account.Id == <GUID>);
            var accountInfo = accounts.First();

            // we will use the access token from here on, no need for the apim key
            client.DefaultRequestHeaders.Remove("Ocp-Apim-Subscription-Key");

            queryParams = Helpers.CreateQueryString(
            new Dictionary<string, string>()
            {
                {"accessToken", accountInfo.AccessToken},
                {"language", "English"},
            });

            var videoGetIndexRequestResult = await client.GetAsync($"{apiUrl}/{accountInfo.Location}/Accounts/{accountInfo.Id}/Videos/{videoId}/Index?{queryParams}");
            var videoGetIndexResult = await videoGetIndexRequestResult.Content.ReadAsStringAsync();

            return videoGetIndexResult;
        }

        // https://docs.microsoft.com/en-us/azure/media-services/video-indexer/upload-index-videos#code-sample
        private async Task<string> UploadVideoAndIndex(string fileName, string videoUrl)
        {
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.ServicePointManager.SecurityProtocol | System.Net.SecurityProtocolType.Tls12;

            // create the http client
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

            // obtain account information and access token
            string queryParams = Helpers.CreateQueryString(
                new Dictionary<string, string>()
                {
                    {"generateAccessTokens", "true"},
                    {"allowEdit", "true"},
                });
            HttpResponseMessage result = await client.GetAsync($"{apiUrl}/auth/trial/Accounts?{queryParams}");
            var json = await result.Content.ReadAsStringAsync();
            var accounts = JsonConvert.DeserializeObject<AccountContractSlim[]>(json);

            // take the relevant account, here we simply take the first, 
            // you can also get the account via accounts.First(account => account.Id == <GUID>);
            var accountInfo = accounts.First();

            // we will use the access token from here on, no need for the apim key
            client.DefaultRequestHeaders.Remove("Ocp-Apim-Subscription-Key");

            // upload a video
            var content = new MultipartFormDataContent();
            Console.WriteLine("Uploading...");

            var videoName = fileName;

            queryParams = Helpers.CreateQueryString(
                new Dictionary<string, string>()
                {
                    {"accessToken", accountInfo.AccessToken},
                    {"name", videoName},
                    {"description", "Generated from VideoIndexerApi"},
                    {"privacy", "private"},
                    {"partition", "partition"},
                    {"videoUrl", videoUrl},
                });
            var uploadRequestResult = await client.PostAsync($"{apiUrl}/{accountInfo.Location}/Accounts/{accountInfo.Id}/Videos?{queryParams}", content);
            var uploadResult = await uploadRequestResult.Content.ReadAsStringAsync();

            // get the video ID from the upload result
            string videoId = JsonConvert.DeserializeObject<dynamic>(uploadResult)["id"];
            Console.WriteLine("Uploaded");
            Console.WriteLine("Video ID:");
            Console.WriteLine(videoId);

            // wait for the video index to finish
            while (true)
            {
                await Task.Delay(10000);

                queryParams = Helpers.CreateQueryString(
                    new Dictionary<string, string>()
                    {
                        {"accessToken", accountInfo.AccessToken},
                        {"language", "English"},
                    });

                var videoGetIndexRequestResult = await client.GetAsync($"{apiUrl}/{accountInfo.Location}/Accounts/{accountInfo.Id}/Videos/{videoId}/Index?{queryParams}");
                var videoGetIndexResult = await videoGetIndexRequestResult.Content.ReadAsStringAsync();

                string processingState = JsonConvert.DeserializeObject<dynamic>(videoGetIndexResult)["state"];

                Console.WriteLine("");
                Console.WriteLine("State:");
                Console.WriteLine(processingState);

                // job is finished
                if (processingState != "Uploaded" && processingState != "Processing")
                {
                    if (processingState == "Failed")
                    {
                        string failureCode = JsonConvert.DeserializeObject<dynamic>(videoGetIndexResult)["failureCode"];
                        string failureMessage = JsonConvert.DeserializeObject<dynamic>(videoGetIndexResult)["failureMessage"];
                        Console.WriteLine("Indexing failed!");
                        Console.WriteLine("failureCode: " + failureCode);
                        Console.WriteLine("failureMessage: " + failureMessage);
                        break;
                    }

                    _logger.LogDebug("Full JSON:");
                    _logger.LogDebug(videoGetIndexResult);
                    break;
                }
            }

            return videoId;
        }

        private async Task<string> GenerateSasUrl(string containerName, string blobName)
        {
            var storageAccount = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME");

            // Create a BlobServiceClient that will authenticate through Active Directory
            Uri accountUri = new Uri(String.Format("https://{0}.blob.core.windows.net/", storageAccount));
            BlobServiceClient client = new BlobServiceClient(accountUri, new DefaultAzureCredential());

            var containerClient = client.GetBlobContainerClient(containerName);

            // Create a SAS token that's valid for one hour.
            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };

            // Specify read permissions for the SAS.
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            UserDelegationKey key = await client.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow,
                                                                DateTimeOffset.UtcNow.AddDays(7));

            string sasToken = sasBuilder.ToSasQueryParameters(key, storageAccount).ToString();

            // Construct the full URI, including the SAS token.
            UriBuilder fullUri = new UriBuilder()
            {
                Scheme = "https",
                Host = string.Format("{0}.blob.core.windows.net", storageAccount),
                Path = string.Format("{0}/{1}", containerName, blobName),
                Query = sasToken
            };

            return fullUri.ToString();
        }

        private static bool IsCloudEvent(string jsonContent)
        {
            // Cloud events are sent one at a time, while Grid events
            // are sent in an array. As a result, the JObject.Parse will 
            // fail for Grid events. 
            try
            {
                // Attempt to read one JSON object. 
                var eventData = JObject.Parse(jsonContent);

                // Check for the spec version property.
                var version = eventData["specversion"].Value<string>();
                if (!string.IsNullOrEmpty(version)) return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return false;
        }

        #endregion
    }

}