using System;
using System.Net.Http;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds a configured <see cref="AdvancedBillingClient"/> from <see cref="MaxioSettings"/>.
/// </summary>
public static class MaxioClientFactory
{
    public static AdvancedBillingClient Create(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new SubscriptionBillingException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Load it into user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new SubscriptionBillingException(
                "Maxio is not configured: 'Maxio:Subdomain' is missing. Load it into user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(new BasicAuthModel.Builder(settings.ApiKey, "x").Build())
            .Environment(ParseEnvironment(settings.Environment))
            .Site(settings.Subdomain);

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new SubscriptionBillingException($"Maxio:BaseUrl '{settings.BaseUrl}' is not a valid absolute URL.");
            }

            var httpClient = new HttpClient(new MaxioBaseUrlHandler(baseUri) { InnerHandler = new HttpClientHandler() });
            builder.HttpClientConfig(config => config.HttpClientInstance(httpClient));
        }

        return builder.Build();
    }

    private static AdvancedBilling.Standard.Environment ParseEnvironment(string? value) =>
        string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)
            ? AdvancedBilling.Standard.Environment.EU
            : AdvancedBilling.Standard.Environment.US;
}
