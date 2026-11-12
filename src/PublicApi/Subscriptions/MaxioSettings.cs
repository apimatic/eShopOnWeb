using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Bound from the <c>Maxio:</c> configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional. Plan used by POST /api/subscriptions when the caller names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Returns the problems with the settings (names only, never values).</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return "Maxio:ApiKey is not configured.";
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) return "Maxio:ProductFamilyHandle is not configured.";
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain)) return "Maxio:Subdomain is not configured (required when Maxio:BaseUrl is not set).";
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            return "Maxio:BaseUrl is not an absolute URL.";
        }
        return null;
    }
}
