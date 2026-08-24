using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets / environment, never from files in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the Basic auth username; password is literally "x" per the OpenAPI spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, templated into https://{site}.chargify.com per the spec's US server definition.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override. When set, used verbatim as the API base address instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public Uri ResolveBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";

        // Trailing slash so relative request URIs ("customers.json", ...) combine correctly.
        return new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
