using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets/environment variables; none are committed to the repo.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic-auth username; password is literally "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "my-site" for https://my-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";

        return new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
