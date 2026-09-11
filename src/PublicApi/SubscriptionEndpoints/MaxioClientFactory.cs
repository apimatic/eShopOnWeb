using System;
using System.Net.Http;
using AdvancedBilling.Standard;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioClientFactory
{
    private readonly MaxioSettings _settings;
    private AdvancedBillingClient? _client;
    private readonly object _lock = new();

    public MaxioClientFactory(IOptions<MaxioSettings> settings)
    {
        _settings = settings.Value;
    }

    public AdvancedBillingClient GetClient()
    {
        if (_client != null) return _client;

        lock (_lock)
        {
            if (_client != null) return _client;

            var builder = new AdvancedBillingClient.Builder()
                .BasicAuthCredentials(
                    new AdvancedBilling.Standard.Authentication.BasicAuthModel.Builder(
                        _settings.ApiKey,
                        "x")
                    .Build())
                .Environment(AdvancedBilling.Standard.Environment.US)
                .Site(_settings.Subdomain);

            if (!string.IsNullOrEmpty(_settings.BaseUrl))
            {
                var httpClient = new HttpClient { BaseAddress = new Uri(_settings.BaseUrl) };
                builder = builder.HttpClientConfig(config =>
                    config.HttpClientInstance(httpClient));
            }

            _client = builder.Build();
            return _client;
        }
    }
}
