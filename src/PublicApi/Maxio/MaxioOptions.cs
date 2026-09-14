using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; values are supplied via
/// user-secrets / environment variables and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException($"Missing required setting '{SectionName}:{nameof(ApiKey)}'.");
        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException($"Missing required setting '{SectionName}:{nameof(Subdomain)}'.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException($"Missing required setting '{SectionName}:{nameof(ProductFamilyHandle)}'.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Setting '{SectionName}:{nameof(BaseUrl)}' must be an absolute https URL when provided.");
    }
}
