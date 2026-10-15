using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Returns a description of the first problem (names the key, never a value), or null if valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return $"{SectionName}:{nameof(ApiKey)} is not configured. Set it via user-secrets or your secret store.";
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            return $"{SectionName}:{nameof(ProductFamilyHandle)} is not configured.";
        var hasBaseUrl = !string.IsNullOrWhiteSpace(BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(Subdomain))
            return $"{SectionName}:{nameof(Subdomain)} is not configured (required unless {SectionName}:{nameof(BaseUrl)} is set).";
        if (hasBaseUrl && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            return $"{SectionName}:{nameof(BaseUrl)} must be an absolute URL.";
        return null;
    }
}
