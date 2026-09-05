using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioSubscriptionServiceTests;

internal static class MaxioSubscriptionServiceTestFactory
{
    public static (MaxioSubscriptionService Service, QueueHttpMessageHandler Handler) Create(
        string productFamilyHandle,
        params (HttpStatusCode Status, string Body)[] responses)
    {
        var handler = new QueueHttpMessageHandler(responses);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(5)
            }
        };
        options.Server.Production.Us.Site = "test-site";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = productFamilyHandle
        });

        var service = new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
        return (service, handler);
    }
}
