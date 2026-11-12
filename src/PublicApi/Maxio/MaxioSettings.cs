using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Bound from the "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (secret; user-secrets or environment configuration only).</summary>
    public string? ApiKey { get; set; }
    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }
    /// <summary>Handle of the product family that holds the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional. Plan handle used when a subscribe request names no plan.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Throws (naming the key, never the value) when the settings cannot run the integration.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException($"{SectionName}:ApiKey is not configured. Set it via user-secrets or environment configuration.");
        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException($"{SectionName}:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException($"{SectionName}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            throw new InvalidOperationException($"{SectionName}:BaseUrl must be an absolute http(s) URL when set.");
    }
}
