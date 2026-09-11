using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioAdvancedBillingClient _client;

    public MaxioBillingService(IHttpClientFactory factory, IConfiguration config)
    {
        var section = config.GetSection("Maxio");
        var apiKey = section["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var subdomain = section["Subdomain"] ?? "cp-exp-2";
        var baseUrl = section["BaseUrl"];

        _httpClient = factory.CreateClient();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{subdomain}.chargify.com");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
            Environment = ServerEnvironment.Us
        };

        _client = new MaxioAdvancedBillingClient(_httpClient, options);
    }

    public MaxioAdvancedBillingClient Client => _client;
}
