using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioClientFactory
{
    public const string ReadClientName = "MaxioRead";
    public const string WriteClientName = "MaxioWrite";

    public MaxioAdvancedBillingClient Read { get; }
    public MaxioAdvancedBillingClient Write { get; }

    public MaxioClientFactory(IHttpClientFactory httpClientFactory, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        Read = new MaxioAdvancedBillingClient(
            httpClientFactory.CreateClient(ReadClientName),
            CreateOptions(settings, RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }));
        Write = new MaxioAdvancedBillingClient(
            httpClientFactory.CreateClient(WriteClientName),
            CreateOptions(settings, RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10) }));
    }

    private static MaxioAdvancedBillingClientOptions CreateOptions(MaxioOptions settings, RetryOptions retry)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Retry = retry
        };

        options.Server.Production.Us.Site = settings.Subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
