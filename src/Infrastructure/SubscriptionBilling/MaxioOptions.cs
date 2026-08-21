using System;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetApiBaseUri()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        var baseUrl = BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
            }

            baseUrl = $"https://{Subdomain}.chargify.com";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URI when set.");
        }

        return uri;
    }
}
