using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var overrideUri) ||
                (overrideUri.Scheme != Uri.UriSchemeHttps && overrideUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
            }

            return BaseUrl;
        }

        if (!Regex.IsMatch(Subdomain, "^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$"))
        {
            throw new InvalidOperationException("Maxio:Subdomain must be a valid DNS label.");
        }

        // The checked-in OpenAPI contract defines this server template.
        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        _ = GetApiBaseUrl();
    }
}
