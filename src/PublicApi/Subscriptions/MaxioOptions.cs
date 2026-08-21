using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var overrideUri))
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL.");
            }

            return overrideUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        if (!Regex.IsMatch(Subdomain, "^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$"))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not a valid site subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute);
    }
}

