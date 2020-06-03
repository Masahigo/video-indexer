using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VideoIndexerApi.Models;

namespace VideoIndexerApi.HealthChecks
{
    internal class ReadinessHealthCheck : IHealthCheck
    {
        private readonly ILogger<ReadinessHealthCheck> _logger;
        private readonly string apiUrl;
        private readonly string apiKey;
        public ReadinessHealthCheck(ILogger<ReadinessHealthCheck> logger,
                                    IConfiguration configuration)
        {
            _logger = logger;
            apiUrl = configuration["VIDEO_API_URL"];
            apiKey = Environment.GetEnvironmentVariable("VIDEO_INDEXER_API_KEY");
        }
        public bool ConnectionToVideoApiOk { get; set; } = false;
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.LogInformation("Readiness health check executed.");
            if(string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey)) {
                _logger.LogWarning("apiUrl or apiKey missing - exiting..");
                throw new Exception("apiUrl or apiKey missing!");
            }
            ConnectionToVideoApiOk = await TestConnectivityToVideoApi();
            if (ConnectionToVideoApiOk)
            {
                _logger.LogInformation("Connection to video indexer API is working.");
                return await Task.FromResult(HealthCheckResult.Healthy());
            }

            _logger.LogInformation("Connection to video indexer API failed.");
            return await Task.FromResult(HealthCheckResult.Unhealthy());
        }

        private async Task<bool> TestConnectivityToVideoApi()
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

            try
            {
                HttpResponseMessage result = await client.GetAsync($"{apiUrl}/auth/trial/Accounts?{queryParams}");
                var json = await result.Content.ReadAsStringAsync();
                var accounts = JsonConvert.DeserializeObject<AccountContractSlim[]>(json);
                var accountInfo = accounts.First();

                return !string.IsNullOrEmpty(accountInfo.AccessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error occured when trying to read from video indexer API: " + ex.Message.ToString());
            }

            return false;
        }
    }
}